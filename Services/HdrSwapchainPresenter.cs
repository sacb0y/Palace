using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;
using WinRT;

namespace Palace.Services;

/// <summary>
/// Composition swapchain in scRGB. Falls back silently; callers keep BitmapImage.
/// </summary>
internal sealed class HdrSwapchainPresenter : IDisposable
{
    private readonly SwapChainPanel _panel;
    private IntPtr _device;
    private IntPtr _context;
    private IntPtr _swapChain;
    private int _bufferW;
    private int _bufferH;
    private bool _attached;
    private float _autoDisplayNits;
    private bool _disposed;
    private readonly HdrHalfBuffer _halfBuffer = new();

    public HdrSwapchainPresenter(SwapChainPanel panel)
    {
        _panel = panel;
    }

    public bool DisplayIsHdr { get; private set; }

    public float DisplayPeakNits { get; private set; }

    public async Task<HdrPresentOutcome> TryPresentAsync(
        HdrFrame frame,
        ImageScaling scaling,
        float rasterScale,
        double dipW,
        double dipH,
        float? peakOverrideNits,
        Func<bool> stillCurrent,
        CancellationToken cancellation)
    {
        // UI thread only for COM / XAML: layout, device, DXGI probe, resize,
        // upload and Present. The half-float raster runs on the thread pool
        // and a stale result is dropped before it touches the swapchain.
        // One call at a time (GalleryStillSurface serializes them).
        if (_disposed)
        {
            return HdrPresentOutcome.Cancelled;
        }

        if (dipW < 2 || dipH < 2)
        {
            dipW = _panel.ActualWidth;
            dipH = _panel.ActualHeight;
        }

        if ((dipW < 2 || dipH < 2)
            && !double.IsNaN(_panel.Width) && !double.IsNaN(_panel.Height))
        {
            dipW = _panel.Width;
            dipH = _panel.Height;
        }

        if (dipW < 2 || dipH < 2 || rasterScale <= 0)
        {
            return HdrPresentOutcome.Failed;
        }

        var vw = Math.Max(1, (int)Math.Round(dipW * rasterScale));
        var vh = Math.Max(1, (int)Math.Round(dipH * rasterScale));
        if (vw > 8192 || vh > 8192)
        {
            return HdrPresentOutcome.Failed;
        }

        try
        {
            // Device / DXGI probe / buffer acquire need the UI apartment.
            // Re-check _disposed inside the callback: Unloaded may Dispose after
            // the opening guard and before this runs; EnsureDevice must not
            // recreate COM objects that Dispose will not release again.
            float clip = 0;
            float scale = 1f;
            ushort[]? half = null;
            await UiDispatch.RunAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                EnsureDevice();
                ProbeDisplay();
                if (DisplayIsHdr)
                {
                    DisplayPeakNits = GalleryPresent.EffectivePeakNits(
                        _autoDisplayNits,
                        peakOverrideNits is > 0,
                        peakOverrideNits ?? 0);
                }
                else
                {
                    DisplayPeakNits = _autoDisplayNits > 0
                        ? _autoDisplayNits
                        : GalleryPresent.ScrgbNits;
                }

                var map = GalleryPresent.PresentMap(
                    DisplayIsHdr,
                    GalleryPresent.ContentMaxNits(frame.MaxScrgb, frame.MaxNits),
                    DisplayPeakNits);
                clip = map.ClipScrgb;
                scale = map.Scale;
                half = _halfBuffer.Acquire(HdrRasterize.HalfLength(vw, vh));
            });
            if (_disposed)
            {
                return HdrPresentOutcome.Cancelled;
            }

            if (half is null)
            {
                return HdrPresentOutcome.Failed;
            }

            await Task.Run(
                () => HdrRasterize.Fill(frame.ScrgbRgba, frame.Width, frame.Height, scaling, vw, vh, clip, half, cancellation, scale),
                cancellation).ConfigureAwait(false);

            if (_disposed || cancellation.IsCancellationRequested || !stillCurrent())
            {
                return HdrPresentOutcome.Cancelled;
            }

            // Upload / Present must run on the UI thread even when the await
            // resume lost the WinUI SynchronizationContext (pool continuation).
            var outcome = HdrPresentOutcome.Cancelled;
            await UiDispatch.RunAsync(() =>
            {
                if (_disposed || cancellation.IsCancellationRequested || !stillCurrent())
                {
                    outcome = HdrPresentOutcome.Cancelled;
                    return;
                }

                EnsureSwapChain(vw, vh);
                Upload(half, vw, vh);
                Present();
                outcome = HdrPresentOutcome.Presented;
            });
            return outcome;
        }
        catch (OperationCanceledException)
        {
            return HdrPresentOutcome.Cancelled;
        }
        catch
        {
            return HdrPresentOutcome.Failed;
        }
    }
    public void Clear()
    {
        try
        {
            if (_swapChain != IntPtr.Zero)
            {
                Present();
            }
        }
        catch
        {
            // Ignore teardown blit errors.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _halfBuffer.Release();
        Release(ref _swapChain);
        Release(ref _context);
        Release(ref _device);
        _attached = false;
    }

    private void EnsureDevice()
    {
        if (_device != IntPtr.Zero)
        {
            return;
        }

        var hr = D3D11CreateDevice(
            IntPtr.Zero,
            1,
            IntPtr.Zero,
            0x20,
            IntPtr.Zero,
            0,
            7,
            out _device,
            out _,
            out _context);
        if (hr < 0 || _device == IntPtr.Zero || _context == IntPtr.Zero)
        {
            throw new InvalidOperationException("D3D11CreateDevice failed.");
        }
    }

    private void ProbeDisplay()
    {
        DisplayIsHdr = false;
        _autoDisplayNits = 0;
        DisplayPeakNits = 0;
        try
        {
            var dxgiDevice = Query(_device, IidDxgiDevice);
            try
            {
                var adapter = CallGetAdapter(dxgiDevice);
                    try
                    {
                        var output = FindOutputForWindow(adapter);
                        if (output == IntPtr.Zero)
                        {
                            return;
                        }

                        try
                        {
                            var output6 = Query(output, IidDxgiOutput6);
                            try
                            {
                                var desc = new DxgiOutputDesc1();
                                var hr = CallGetDesc1(output6, ref desc);
                                if (hr >= 0)
                                {
                                    DisplayIsHdr = GalleryPresent.IsAdvancedColor(desc.ColorSpace);
                                    if (DisplayIsHdr)
                                    {
                                        var peak = GalleryPresent.ProbedDisplayLuminance(
                                            desc.MaxLuminance, desc.MaxFullFrameLuminance);
                                        _autoDisplayNits = peak;
                                        DisplayPeakNits = peak;
                                    }
                                    else
                                    {
                                        var sdr = GalleryPresent.SdrPresentPeakNits(
                                            desc.MaxLuminance, desc.MaxFullFrameLuminance);
                                        _autoDisplayNits = sdr;
                                        DisplayPeakNits = sdr;
                                    }
                                }
                            }
                            finally
                            {
                                Release(ref output6);
                            }
                        }
                        finally
                        {
                            Release(ref output);
                        }
                    }
                finally
                {
                    Release(ref adapter);
                }
            }
            finally
            {
                Release(ref dxgiDevice);
            }
        }
        catch
        {
            DisplayIsHdr = false;
            _autoDisplayNits = 0;
            DisplayPeakNits = 0;
        }
    }

    private IntPtr FindOutputForWindow(IntPtr adapter)
    {
        var want = TryWindowMonitor();
        IntPtr chosen = IntPtr.Zero;
        for (uint i = 0; ; i++)
        {
            var output = CallEnumOutputs(adapter, i);
            if (output == IntPtr.Zero)
            {
                break;
            }

            if (chosen == IntPtr.Zero)
            {
                chosen = output;
                if (want == IntPtr.Zero || OutputMatchesMonitor(output, want))
                {
                    break;
                }

                continue;
            }

            if (OutputMatchesMonitor(output, want))
            {
                Release(ref chosen);
                chosen = output;
                break;
            }

            Release(ref output);
        }

        return chosen;
    }

    private bool OutputMatchesMonitor(IntPtr output, IntPtr monitor)
    {
        try
        {
            var output6 = Query(output, IidDxgiOutput6);
            try
            {
                var desc = new DxgiOutputDesc1();
                return CallGetDesc1(output6, ref desc) >= 0 && desc.Monitor == monitor;
            }
            finally
            {
                Release(ref output6);
            }
        }
        catch
        {
            return false;
        }
    }

    private IntPtr TryWindowMonitor()
    {
        try
        {
            var root = _panel.XamlRoot;
            if (root?.ContentIslandEnvironment is null)
            {
                return IntPtr.Zero;
            }

            var hwnd = Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId);
            return hwnd == IntPtr.Zero ? IntPtr.Zero : MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private void EnsureSwapChain(int width, int height)
    {
        if (_swapChain != IntPtr.Zero && _bufferW == width && _bufferH == height)
        {
            return;
        }

        if (_swapChain != IntPtr.Zero)
        {
            var hrResize = CallResizeBuffers(_swapChain, 2, (uint)width, (uint)height, 10, 0);
            if (hrResize >= 0)
            {
                _bufferW = width;
                _bufferH = height;
                return;
            }

            Release(ref _swapChain);
            _attached = false;
        }

        var dxgiDevice = Query(_device, IidDxgiDevice);
        IntPtr adapter = IntPtr.Zero;
        IntPtr factory = IntPtr.Zero;
        try
        {
            adapter = CallGetAdapter(dxgiDevice);
            factory = CallGetParent(adapter, IidDxgiFactory2);
            var desc = new DxgiSwapChainDesc1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = 10,
                Stereo = 0,
                SampleCount = 1,
                SampleQuality = 0,
                BufferUsage = 0x20,
                BufferCount = 2,
                Scaling = 0,
                SwapEffect = 3,
                AlphaMode = 3,
                Flags = 0
            };
            var hr = CallCreateSwapChainForComposition(factory, _device, ref desc, IntPtr.Zero, out _swapChain);
            if (hr < 0 || _swapChain == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreateSwapChainForComposition failed.");
            }

            var hrColor = CallSetColorSpace1(_swapChain, DxgiColorSpaceRgbFullG10NoneP709);
            if (hrColor < 0)
            {
                Release(ref _swapChain);
                throw new InvalidOperationException("SetColorSpace1 scRGB failed.");
            }

            AttachPanel();
            _bufferW = width;
            _bufferH = height;
        }
        finally
        {
            Release(ref factory);
            Release(ref adapter);
            Release(ref dxgiDevice);
        }
    }

    private void AttachPanel()
    {
        if (_attached)
        {
            return;
        }

        var native = _panel.As<ISwapChainPanelNative>();
        var hr = native.SetSwapChain(_swapChain);
        if (hr < 0)
        {
            throw new InvalidOperationException("SetSwapChain failed.");
        }

        _attached = true;
    }

    private void Upload(ushort[] half, int width, int height)
    {
        var desc = new D3D11Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = 10,
            SampleCount = 1,
            SampleQuality = 0,
            Usage = 0,
            BindFlags = 0x8,
            CpuAccessFlags = 0,
            MiscFlags = 0
        };
        var hr = CallCreateTexture2D(_device, ref desc, IntPtr.Zero, out var texture);
        if (hr < 0 || texture == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateTexture2D failed.");
        }

        try
        {
            var handle = GCHandle.Alloc(half, GCHandleType.Pinned);
            try
            {
                CallUpdateSubresource(_context, texture, 0, IntPtr.Zero, handle.AddrOfPinnedObject(), (uint)(width * 8), 0);
            }
            finally
            {
                handle.Free();
            }

            var back = CallGetBuffer(_swapChain, 0, IidTexture2D);
            try
            {
                CallCopyResource(_context, back, texture);
            }
            finally
            {
                Release(ref back);
            }
        }
        finally
        {
            Release(ref texture);
        }
    }

    private void Present()
    {
        var hr = CallPresent(_swapChain, 1, 0);
        if (hr < 0)
        {
            throw new InvalidOperationException("Present failed.");
        }
    }

    private static IntPtr Query(IntPtr unk, Guid iid)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<QueryInterface>(Vtbl(unk, 0));
        var hr = fn(unk, ref iid, out var ppv);
        if (hr < 0 || ppv == IntPtr.Zero)
        {
            throw new InvalidOperationException("QueryInterface failed.");
        }

        return ppv;
    }

    private static void Release(ref IntPtr unk)
    {
        if (unk == IntPtr.Zero)
        {
            return;
        }

        var fn = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(Vtbl(unk, 2));
        fn(unk);
        unk = IntPtr.Zero;
    }

    private static IntPtr Vtbl(IntPtr unk, int index)
    {
        var table = Marshal.ReadIntPtr(unk);
        return Marshal.ReadIntPtr(table, index * IntPtr.Size);
    }

    private static IntPtr CallGetAdapter(IntPtr dxgiDevice)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GetAdapterDelegate>(Vtbl(dxgiDevice, 7));
        var hr = fn(dxgiDevice, out var adapter);
        return hr < 0 ? IntPtr.Zero : adapter;
    }

    private static IntPtr CallEnumOutputs(IntPtr adapter, uint index)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<EnumOutputsDelegate>(Vtbl(adapter, 7));
        var hr = fn(adapter, index, out var output);
        return hr < 0 ? IntPtr.Zero : output;
    }

    private static int CallGetDesc1(IntPtr output6, ref DxgiOutputDesc1 desc)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(Vtbl(output6, VtblGetDesc1));
        return fn(output6, ref desc);
    }

    private static IntPtr CallGetParent(IntPtr obj, Guid iid)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GetParentDelegate>(Vtbl(obj, 6));
        var hr = fn(obj, ref iid, out var parent);
        if (hr < 0 || parent == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetParent failed.");
        }

        return parent;
    }

    private static int CallCreateSwapChainForComposition(
        IntPtr factory,
        IntPtr device,
        ref DxgiSwapChainDesc1 desc,
        IntPtr output,
        out IntPtr swapChain)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<CreateSwapChainForCompositionDelegate>(Vtbl(factory, VtblCreateSwapChainForComposition));
        return fn(factory, device, ref desc, output, out swapChain);
    }

    private static int CallSetColorSpace1(IntPtr swapChain, int colorSpace)
    {
        var swap3 = Query(swapChain, IidDxgiSwapChain3);
        try
        {
            var fn = Marshal.GetDelegateForFunctionPointer<SetColorSpace1Delegate>(Vtbl(swap3, VtblSetColorSpace1));
            return fn(swap3, colorSpace);
        }
        finally
        {
            Release(ref swap3);
        }
    }

    private static int CallResizeBuffers(IntPtr swap, uint count, uint w, uint h, int format, uint flags)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<ResizeBuffersDelegate>(Vtbl(swap, 13));
        return fn(swap, count, w, h, format, flags);
    }

    private static int CallCreateTexture2D(IntPtr device, ref D3D11Texture2DDesc desc, IntPtr init, out IntPtr texture)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(Vtbl(device, 5));
        return fn(device, ref desc, init, out texture);
    }

    private static void CallUpdateSubresource(IntPtr context, IntPtr resource, uint sub, IntPtr box, IntPtr data, uint pitch, uint depth)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<UpdateSubresourceDelegate>(Vtbl(context, 48));
        fn(context, resource, sub, box, data, pitch, depth);
    }

    private static IntPtr CallGetBuffer(IntPtr swap, uint index, Guid iid)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GetBufferDelegate>(Vtbl(swap, 9));
        var hr = fn(swap, index, ref iid, out var buffer);
        if (hr < 0 || buffer == IntPtr.Zero)
        {
            throw new InvalidOperationException("GetBuffer failed.");
        }

        return buffer;
    }

    private static void CallCopyResource(IntPtr context, IntPtr dest, IntPtr src)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(Vtbl(context, 47));
        fn(context, dest, src);
    }

    private static int CallPresent(IntPtr swap, uint sync, uint flags)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<PresentDelegate>(Vtbl(swap, 8));
        return fn(swap, sync, flags);
    }

    // IDXGIFactory2::CreateSwapChainForComposition, IDXGIOutput6::GetDesc1,
    // IDXGISwapChain3::SetColorSpace1. DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709 is scRGB.
    private const int VtblCreateSwapChainForComposition = 24;
    private const int VtblGetDesc1 = 27;
    private const int VtblSetColorSpace1 = 38;
    private const int DxgiColorSpaceRgbFullG10NoneP709 = 1;
    private const uint MonitorDefaultToNearest = 2;

    private static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IidDxgiFactory2 = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    private static readonly Guid IidDxgiSwapChain3 = new("94d99bdb-f1f8-4ab0-b236-7da0170edab1");
    private static readonly Guid IidDxgiOutput6 = new("068346e8-aaec-4b84-add5-13ff8c7033c8");
    private static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr context);

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
    private interface ISwapChainPanelNative
    {
        [PreserveSig]
        int SetSwapChain(IntPtr swapChain);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiSwapChainDesc1
    {
        public uint Width;
        public uint Height;
        public int Format;
        public int Stereo;
        public uint SampleCount;
        public uint SampleQuality;
        public int BufferUsage;
        public uint BufferCount;
        public int Scaling;
        public int SwapEffect;
        public int AlphaMode;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11Texture2DDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public uint SampleCount;
        public uint SampleQuality;
        public int Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiOutputDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public int DesktopLeft;
        public int DesktopTop;
        public int DesktopRight;
        public int DesktopBottom;
        public int AttachedToDesktop;
        public uint Rotation;
        public IntPtr Monitor;
        public uint BitsPerColor;
        public int ColorSpace;
        public float RedPrimaryX;
        public float RedPrimaryY;
        public float GreenPrimaryX;
        public float GreenPrimaryY;
        public float BluePrimaryX;
        public float BluePrimaryY;
        public float WhitePointX;
        public float WhitePointY;
        public float MinLuminance;
        public float MaxLuminance;
        public float MaxFullFrameLuminance;
    }

    private delegate int QueryInterface(IntPtr self, ref Guid iid, out IntPtr ppv);
    private delegate uint ReleaseDelegate(IntPtr self);
    private delegate int GetAdapterDelegate(IntPtr self, out IntPtr adapter);
    private delegate int EnumOutputsDelegate(IntPtr self, uint index, out IntPtr output);
    private delegate int GetDesc1Delegate(IntPtr self, ref DxgiOutputDesc1 desc);
    private delegate int GetParentDelegate(IntPtr self, ref Guid iid, out IntPtr parent);
    private delegate int CreateSwapChainForCompositionDelegate(IntPtr self, IntPtr device, ref DxgiSwapChainDesc1 desc, IntPtr restrict, out IntPtr swapChain);
    private delegate int SetColorSpace1Delegate(IntPtr self, int colorSpace);
    private delegate int ResizeBuffersDelegate(IntPtr self, uint count, uint w, uint h, int format, uint flags);
    private delegate int CreateTexture2DDelegate(IntPtr self, ref D3D11Texture2DDesc desc, IntPtr init, out IntPtr texture);
    private delegate void UpdateSubresourceDelegate(IntPtr self, IntPtr resource, uint sub, IntPtr box, IntPtr data, uint pitch, uint depth);
    private delegate int GetBufferDelegate(IntPtr self, uint index, ref Guid iid, out IntPtr surface);
    private delegate void CopyResourceDelegate(IntPtr self, IntPtr dest, IntPtr src);
    private delegate int PresentDelegate(IntPtr self, uint sync, uint flags);
}

internal enum HdrPresentOutcome
{
    Presented,
    Failed,
    Cancelled
}
