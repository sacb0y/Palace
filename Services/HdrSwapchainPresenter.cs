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
    private float _sdrWhiteLevelNits;
    private float _compositionScaleX = 1f;
    private float _compositionScaleY = 1f;
    private bool _disposed;
    private readonly HdrHalfBuffer _halfBuffer = new();

    public HdrSwapchainPresenter(SwapChainPanel panel)
    {
        _panel = panel;
    }

    public bool DisplayIsHdr { get; private set; }

    public string DisplayProbeText { get; private set; } = "";

    public float DisplayPeakNits { get; private set; }

    public float SdrWhiteLevelNits => _sdrWhiteLevelNits;

    /// <summary>Last SDR Reinhard exposure (0 when HDR present).</summary>
    public float LastSdrExposure { get; private set; }

    /// <summary>Last SDR Reinhard white (0 when HDR present / unused).</summary>
    public float LastSdrReinhardWhite { get; private set; }

    public async Task<HdrPresentOutcome> TryPresentAsync(
        HdrFrame frame,
        ImageScaling scaling,
        float rasterScale,
        double dipW,
        double dipH,
        float? peakOverrideNits,
        float contentMaxNits,
        Func<bool> stillCurrent,
        CancellationToken cancellation,
        HdrProbe contentProbe = default,
        HdrVizMode viz = HdrVizMode.None,
        int? vizPrimaries = null,
        float? sdrExposure = null,
        float? sdrReinhardWhite = null)
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

        // Prefer SwapChainPanel CompositionScale (what DXGI composition uses)
        // over XamlRoot.RasterizationScale — a mismatch sized the buffer in
        // physical pixels without SetMatrixTransform and cropped Fit on DPI-
        // scaled SDR laptops (all HDR formats on the scRGB present path).
        var scaleX = _panel.CompositionScaleX;
        var scaleY = _panel.CompositionScaleY;
        if (scaleX <= 0 || !float.IsFinite(scaleX))
        {
            scaleX = rasterScale > 0 ? rasterScale : 1f;
        }

        if (scaleY <= 0 || !float.IsFinite(scaleY))
        {
            scaleY = rasterScale > 0 ? rasterScale : 1f;
        }

        if (dipW < 2 || dipH < 2)
        {
            // Layout not ready yet — skip, do not count as present failure.
            return HdrPresentOutcome.Cancelled;
        }

        if (scaleX <= 0 || scaleY <= 0)
        {
            return HdrPresentOutcome.Failed;
        }

        _compositionScaleX = scaleX;
        _compositionScaleY = scaleY;
        var (vw, vh) = GalleryPresent.SwapchainPhysicalSize(dipW, dipH, scaleX, scaleY);
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
            HdrPresentMap map = default;
            var displayHdr = false;
            ushort[]? half = null;
            float? resolvedExposure = sdrExposure;
            float? resolvedWhite = sdrReinhardWhite;
            await UiDispatch.RunAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                EnsureDevice();
                ProbeDisplay(contentProbe);
                if (DisplayIsHdr)
                {
                    DisplayPeakNits = GalleryPresent.EffectivePeakNits(
                        _autoDisplayNits,
                        peakOverrideNits is > 0,
                        peakOverrideNits ?? 0);
                    // Absolute scRGB FP16 (1.0 = 80 nits). Decoders already
                    // emit nits/80 — scale 1, clip peak/80. No SdrWhiteLevel.
                    map = GalleryPresent.PresentMap(
                        true,
                        contentMaxNits > 0
                            ? contentMaxNits
                            : GalleryPresent.ContentMaxNits(frame.MaxScrgb, frame.MaxNits),
                        DisplayPeakNits);
                }
                else
                {
                    DisplayPeakNits = _autoDisplayNits > 0
                        ? _autoDisplayNits
                        : GalleryPresent.ScrgbNits;
                }

                displayHdr = DisplayIsHdr;
                half = _halfBuffer.Acquire(HdrRasterize.HalfLength(vw, vh));
            });

            float outcomeExposure = 0f;
            float outcomeWhite = 0f;
            var sdrWhite = _sdrWhiteLevelNits;
            // Same ToneMap.Analyze + Reinhard as mosaic thumbs (StillDecode.FromScrgb).
            // Cached sticky exposure/white reuse Fit stats on 1:1 / resize.
            var matchThumb = GalleryPresent.PresentMatchesThumbToneMap(
                displayHdr, frame.BufferTransfer);
            if (matchThumb)
            {
                if (resolvedExposure is null or <= 0
                    || resolvedWhite is null or <= 0)
                {
                    var pixelCount = checked(frame.Width * frame.Height);
                    var analyzed = await Task.Run(
                        () => ToneMap.Analyze(
                            frame.ScrgbRgba, pixelCount, cancellation),
                        cancellation).ConfigureAwait(false);
                    resolvedExposure = analyzed.Exposure;
                    resolvedWhite = analyzed.White;
                }

                outcomeExposure = resolvedExposure!.Value;
                outcomeWhite = resolvedWhite!.Value;
                map = GalleryPresent.PresentMapThumbMatch(outcomeExposure, outcomeWhite);
            }
            else
            {
                // HDR passthrough (PQ/HLG): still Analyze for log fields only.
                var pixelCount = checked(frame.Width * frame.Height);
                var analyzed = await Task.Run(
                    () => ToneMap.Analyze(
                        frame.ScrgbRgba, pixelCount, cancellation),
                    cancellation).ConfigureAwait(false);
                outcomeExposure = analyzed.Exposure;
                outcomeWhite = analyzed.White;
            }

            var p995Log = outcomeExposure > 1e-6f && outcomeWhite > 0
                ? outcomeWhite / Math.Max(outcomeExposure, 1e-6f)
                : outcomeWhite;
            // When Analyze skipped tonemap (p995≤1 → white=1), p995≈1/exposure.
            if (outcomeWhite <= 1f)
            {
                p995Log = frame.MaxScrgb > 0 ? frame.MaxScrgb : p995Log;
            }

            DisplayHdr.WriteDebug(
                $"SdrTonemap {frame.Width}x{frame.Height} exposure={outcomeExposure:0.####} "
                + $"white={outcomeWhite:0.####} sdrWhite={(sdrWhite > 0 ? sdrWhite : GalleryPresent.ScrgbNits):0.#} "
                + $"peakScrgb={frame.MaxScrgb:0.####} maxNits={frame.MaxNits:0.#} "
                + $"shoulder={(matchThumb && map.ReinhardWhite > 1f ? 1 : 0)}"
                + (displayHdr ? (matchThumb ? " hdr-display+thumb" : " hdr-display") : ""));
            SdrTonemapLog.TryAppendPresent(
                path: frame.SourcePath,
                decoder: displayHdr ? "hdr-display" : frame.PresentDecoder,
                intensityTarget: frame.IntensityTarget,
                scale: frame.IntensityScale,
                bufferTransfer: HdrJxlPresent.BufferTransferLabel(frame.BufferTransfer),
                p995: p995Log,
                white: outcomeWhite,
                exposure: outcomeExposure,
                sdrWhiteLevelNits: sdrWhite,
                shoulder: matchThumb && map.ReinhardWhite > 1f ? 1 : 0);

            if (_disposed)
            {
                return HdrPresentOutcome.Cancelled;
            }

            if (half is null)
            {
                return HdrPresentOutcome.Failed;
            }

            await Task.Run(
                () => HdrRasterize.Fill(
                    frame.ScrgbRgba,
                    frame.Width,
                    frame.Height,
                    scaling,
                    vw,
                    vh,
                    HdrPixelMap.From(map, viz, displayHdr, vizPrimaries),
                    half,
                    cancellation),
                cancellation).ConfigureAwait(false);

            if (_disposed || cancellation.IsCancellationRequested || !stillCurrent())
            {
                return HdrPresentOutcome.Cancelled;
            }

            // Upload / Present must run on the UI thread even when the await
            // resume lost the WinUI SynchronizationContext (pool continuation).
            // LastSdr* is written here with the outcome — never off the UI thread.
            var outcome = HdrPresentOutcome.Cancelled;
            await UiDispatch.RunAsync(() =>
            {
                if (_disposed || cancellation.IsCancellationRequested || !stillCurrent())
                {
                    outcome = HdrPresentOutcome.Cancelled;
                    return;
                }

                EnsureSwapChain(vw, vh);
                ApplyCompositionScaleTransform(_compositionScaleX, _compositionScaleY);
                Upload(half, vw, vh);
                Present();
                AppendCompositionScaleLog();
                // Sticky thumb Analyze stats when the map used them.
                LastSdrExposure = matchThumb ? outcomeExposure : 0f;
                LastSdrReinhardWhite = matchThumb ? outcomeWhite : 0f;
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

    private void ProbeDisplay(HdrProbe content)
    {
        DisplayIsHdr = false;
        DisplayProbeText = "";
        _autoDisplayNits = 0;
        DisplayPeakNits = 0;
        try
        {
            var dxgiDevice = TryQuery(_device, IidDxgiDevice);
            if (dxgiDevice == IntPtr.Zero)
            {
                ApplyUnknownDisplay("no DXGI device", content);
                return;
            }

            try
            {
                var adapter = CallGetAdapter(dxgiDevice);
                if (adapter == IntPtr.Zero)
                {
                    ApplyUnknownDisplay("no adapter", content);
                    return;
                }

                try
                {
                    var output = FindOutputForWindow(adapter);
                    if (output == IntPtr.Zero)
                    {
                        ApplyUnknownDisplay("no output", content);
                        return;
                    }

                    try
                    {
                        var output6 = TryQuery(output, IidDxgiOutput6);
                        if (output6 == IntPtr.Zero)
                        {
                            // Packaged / some GPUs omit Output6; still ask CCD
                            // (and PQ/HLG content) so we do not SDR-tonemap
                            // while Windows HDR is on (FenrirDesktop).
                            ApplyWithoutOutput6(output, content);
                            return;
                        }

                        try
                        {
                            var desc = new DxgiOutputDesc1();
                            var hr = CallGetDesc1(output6, ref desc);
                            if (hr < 0)
                            {
                                ApplyWithoutOutput6(output, content, $"descHr=0x{hr:X8}");
                                return;
                            }

                            var windowsHdr = DisplayHdr.WindowsHdrForNamedOutput(
                                DisplayHdr.TryWindowsHdrEnabled(desc.DeviceName));

                            DisplayIsHdr = GalleryPresent.IsHdrOutput(
                                desc.ColorSpace,
                                desc.MaxLuminance,
                                desc.MaxFullFrameLuminance,
                                (int)desc.BitsPerColor,
                                windowsHdr);
                            _sdrWhiteLevelNits = DisplayHdr.TrySdrWhiteLevelNits(desc.DeviceName);
                            DisplayProbeText = GalleryPresent.FormatDisplayProbe(
                                desc.ColorSpace,
                                desc.MaxLuminance,
                                desc.MaxFullFrameLuminance,
                                (int)desc.BitsPerColor,
                                windowsHdr,
                                DisplayIsHdr,
                                DisplayHdr.LastQuery.Summary,
                                GalleryPresent.ScrgbSwapChainFormat,
                                GalleryPresent.ScrgbColorSpace,
                                _sdrWhiteLevelNits);
                            DisplayHdr.WriteDebug(DisplayProbeText);
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
        catch (Exception ex)
        {
            ApplyUnknownDisplay("throw " + ex.GetType().Name, content);
        }
    }

    private void ApplyWithoutOutput6(IntPtr output, HdrProbe content, string? reason = null)
    {
        var deviceName = TryGetOutputDeviceName(output);
        // Named output: CCD for that GDI name only. No name (rare): any-path
        // CCD so Advanced Color Enabled still wins without Output6.
        var windowsHdr = string.IsNullOrEmpty(deviceName)
            ? DisplayHdr.TryWindowsHdrEnabled(null)
            : DisplayHdr.WindowsHdrForNamedOutput(DisplayHdr.TryWindowsHdrEnabled(deviceName));
        var assume = GalleryPresent.DisplayHdrWithoutOutput6(windowsHdr, content);
        DisplayIsHdr = assume;
        _autoDisplayNits = 0;
        DisplayPeakNits = 0;
        _sdrWhiteLevelNits = DisplayHdr.TrySdrWhiteLevelNits(deviceName);
        var label = reason ?? "no Output6";
        DisplayProbeText = GalleryPresent.FormatUnknownDisplayProbe(
            label,
            assume,
            DisplayHdr.LastQuery.Summary);
        AppendPresentProbeExtras();
        DisplayHdr.WriteDebug(DisplayProbeText);
    }

    private void ApplyUnknownDisplay(string reason, HdrProbe content)
    {
        // No DXGI output at all — still ask any-path CCD, then PQ/HLG content.
        var windowsHdr = DisplayHdr.TryWindowsHdrEnabled(null);
        var assume = GalleryPresent.DisplayHdrWithoutOutput6(windowsHdr, content);
        DisplayIsHdr = assume;
        _autoDisplayNits = 0;
        DisplayPeakNits = 0;
        _sdrWhiteLevelNits = DisplayHdr.TrySdrWhiteLevelNits(null);
        DisplayProbeText = GalleryPresent.FormatUnknownDisplayProbe(
            reason,
            assume,
            DisplayHdr.LastQuery.Summary);
        AppendPresentProbeExtras();
        DisplayHdr.WriteDebug(DisplayProbeText);
    }

    private void AppendPresentProbeExtras()
    {
        // SDR Reinhard present uses SdrWhiteLevel for exposure (fallback 80).
        if (_sdrWhiteLevelNits > 0 && float.IsFinite(_sdrWhiteLevelNits))
        {
            DisplayProbeText += $" sdrWhite={_sdrWhiteLevelNits:0.#}";
        }

        // compScale is logged after the first Present (see TryPresentAsync), not
        // during probe — scale may still be a fallback before layout settles.
        if (DisplayIsHdr)
        {
            DisplayProbeText +=
                " · swap=" + GalleryPresent.FormatSwapChainLabel(
                    GalleryPresent.ScrgbSwapChainFormat,
                    GalleryPresent.ScrgbColorSpace);
        }
    }

    private void AppendCompositionScaleLog()
    {
        if (_compositionScaleX <= 0 || _compositionScaleY <= 0)
        {
            return;
        }

        var tag = $" compScale={_compositionScaleX:0.###}x{_compositionScaleY:0.###}";
        if (DisplayProbeText.Contains("compScale=", StringComparison.Ordinal))
        {
            return;
        }

        DisplayProbeText += tag;
        DisplayHdr.WriteDebug(DisplayProbeText);
    }

    /// <summary>IDXGIOutput::GetDesc DeviceName (<c>\\.\DISPLAYn</c>) without Output6.</summary>
    private static string? TryGetOutputDeviceName(IntPtr output)
    {
        if (output == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var desc = new DxgiOutputDesc();
            if (CallGetDesc(output, ref desc) < 0 || string.IsNullOrWhiteSpace(desc.DeviceName))
            {
                return null;
            }

            return desc.DeviceName;
        }
        catch
        {
            return null;
        }
    }

    private IntPtr FindOutputForWindow(IntPtr adapter)
    {
        if (adapter == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

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
            var output6 = TryQuery(output, IidDxgiOutput6);
            if (output6 == IntPtr.Zero)
            {
                return false;
            }

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

            // Attach first. SetColorSpace1 before SetSwapChain often returns
            // DXGI_ERROR_INVALID_CALL on HDR outputs and aborted present
            // (Info stayed “SDR preview”).
            AttachPanel();
            TrySetScrgbColorSpace(_swapChain);
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
        var ppv = TryQuery(unk, iid);
        if (ppv == IntPtr.Zero)
        {
            throw new InvalidOperationException("QueryInterface failed.");
        }

        return ppv;
    }

    private static IntPtr TryQuery(IntPtr unk, Guid iid)
    {
        if (unk == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            var fn = Marshal.GetDelegateForFunctionPointer<QueryInterface>(Vtbl(unk, 0));
            var hr = fn(unk, ref iid, out var ppv);
            return hr < 0 ? IntPtr.Zero : ppv;
        }
        catch
        {
            return IntPtr.Zero;
        }
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

    private static int CallGetDesc(IntPtr output, ref DxgiOutputDesc desc)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(Vtbl(output, VtblGetDesc));
        return fn(output, ref desc);
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

    private static void TrySetScrgbColorSpace(IntPtr swapChain)
    {
        _ = CallSetColorSpace1(swapChain, DxgiColorSpaceRgbFullG10NoneP709);
    }

    /// <summary>
    /// Invert CompositionScale so a physical-pixel buffer is not scaled again
    /// by XAML (DIP×scale buffer without this transform crops Fit).
    /// </summary>
    private void ApplyCompositionScaleTransform(float scaleX, float scaleY)
    {
        if (_swapChain == IntPtr.Zero || scaleX <= 0 || scaleY <= 0)
        {
            return;
        }

        var matrix = new DxgiMatrix3x2F
        {
            _11 = 1f / scaleX,
            _12 = 0,
            _21 = 0,
            _22 = 1f / scaleY,
            _31 = 0,
            _32 = 0
        };
        var hr = CallSetMatrixTransform(_swapChain, ref matrix);
        if (hr < 0)
        {
            DisplayHdr.WriteDebug($"SetMatrixTransform hr=0x{hr:X8} scale={scaleX:0.###}x{scaleY:0.###}");
        }
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

    private const int ENoInterface = unchecked((int)0x80004002);

    private static int CallSetMatrixTransform(IntPtr swapChain, ref DxgiMatrix3x2F matrix)
    {
        var swap2 = TryQuery(swapChain, IidDxgiSwapChain2);
        if (swap2 == IntPtr.Zero)
        {
            return ENoInterface;
        }

        try
        {
            var fn = Marshal.GetDelegateForFunctionPointer<SetMatrixTransformDelegate>(
                Vtbl(swap2, VtblSetMatrixTransform));
            return fn(swap2, ref matrix);
        }
        finally
        {
            Release(ref swap2);
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

    // IDXGIFactory2::CreateSwapChainForComposition, IDXGIOutput::GetDesc,
    // IDXGIOutput6::GetDesc1, IDXGISwapChain2::SetMatrixTransform,
    // IDXGISwapChain3::SetColorSpace1.
    // DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709 is scRGB.
    private const int VtblCreateSwapChainForComposition = 24;
    private const int VtblGetDesc = 7;
    private const int VtblGetDesc1 = 27;
    // See GalleryPresent.DxgiSwapChain2SetMatrixTransformVtbl (34). Slot 32 is
    // GetMaximumFrameLatency; SetColorSpace1 stays 38.
    private const int VtblSetMatrixTransform = GalleryPresent.DxgiSwapChain2SetMatrixTransformVtbl;
    private const int VtblSetColorSpace1 = 38;
    private const int DxgiColorSpaceRgbFullG10NoneP709 = 1;
    private const uint MonitorDefaultToNearest = 2;

    private static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IidDxgiFactory2 = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    private static readonly Guid IidDxgiSwapChain2 = new("a8be2ac4-199f-4946-b331-79599fb98de7");
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
    private struct DxgiOutputDesc
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
    private delegate int GetDescDelegate(IntPtr self, ref DxgiOutputDesc desc);
    private delegate int GetDesc1Delegate(IntPtr self, ref DxgiOutputDesc1 desc);
    private delegate int GetParentDelegate(IntPtr self, ref Guid iid, out IntPtr parent);
    private delegate int CreateSwapChainForCompositionDelegate(IntPtr self, IntPtr device, ref DxgiSwapChainDesc1 desc, IntPtr restrict, out IntPtr swapChain);
    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiMatrix3x2F
    {
        public float _11;
        public float _12;
        public float _21;
        public float _22;
        public float _31;
        public float _32;
    }

    private delegate int SetColorSpace1Delegate(IntPtr self, int colorSpace);
    private delegate int SetMatrixTransformDelegate(IntPtr self, ref DxgiMatrix3x2F matrix);
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
