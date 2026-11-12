using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;
using WinRT;

namespace Palace.Services;

/// <summary>
/// Diligent D3D12 host attached to a WinUI <see cref="SwapChainPanel"/> for the
/// mesh3d spike. Creates a composition DXGI swapchain via the native host,
/// binds it with <c>ISwapChainPanelNative.SetSwapChain</c>, loads a glTF, and
/// exposes the Streamline device/command-list probe.
/// </summary>
internal sealed class Mesh3dDiligentPresenter : IDisposable
{
    private readonly SwapChainPanel _panel;
    private nint _host;
    private nint _swapChain;
    private bool _disposed;
    private string _status = "idle";

    public Mesh3dDiligentPresenter(SwapChainPanel panel)
    {
        _panel = panel;
    }

    public string Status => _status;

    public Mesh3dStreamlineProbe LastProbe { get; private set; } =
        Mesh3dStreamlineProbe.Unavailable("not started");

    public string? LoadedPath { get; private set; }

    public bool TryStart(string? gltfPath, double dipW, double dipH, float rasterScale)
    {
        if (_disposed)
        {
            _status = "disposed";
            return false;
        }

        if (!Mesh3dSpike.IsSupportedArchitecture)
        {
            _status = "x64 only — " + Mesh3dSpike.ArchitectureCaption;
            LastProbe = Mesh3dStreamlineProbe.Unavailable(_status);
            return false;
        }

        if (!Mesh3dNative.IsAvailable)
        {
            _status = Mesh3dNative.AvailabilityCaption;
            LastProbe = Mesh3dStreamlineProbe.Unavailable(_status);
            return false;
        }

        DestroyHost();
        _host = Mesh3dNative.CreateHost();
        if (_host == 0)
        {
            _status = "pm3_create failed: " + Mesh3dNative.LastError(0);
            LastProbe = Mesh3dStreamlineProbe.Unavailable(_status);
            return false;
        }

        var (w, h) = PhysicalSize(dipW, dipH, rasterScale);
        var create = Mesh3dNative.CreateCompositionSwapChain(_host, w, h, out _swapChain);
        if (create != Mesh3dNative.StatusCode.Ok || _swapChain == 0)
        {
            _status = $"composition swapchain failed ({create}): {Mesh3dNative.LastError(_host)}";
            LastProbe = Mesh3dStreamlineProbe.Unavailable(_status);
            DestroyHost();
            return false;
        }

        try
        {
            var native = _panel.As<ISwapChainPanelNative>();
            var hr = native.SetSwapChain(_swapChain);
            if (hr != 0)
            {
                _status = $"SetSwapChain HRESULT 0x{hr:X8}";
                LastProbe = Mesh3dStreamlineProbe.Unavailable(_status);
                DestroyHost();
                return false;
            }
        }
        catch (Exception ex)
        {
            _status = "SetSwapChain threw " + ex.GetType().Name + ": " + ex.Message;
            LastProbe = Mesh3dStreamlineProbe.Unavailable(_status);
            DestroyHost();
            return false;
        }

        var path = Mesh3dGltf.ResolveLoadPath(gltfPath, AppContext.BaseDirectory);
        LoadedPath = path;
        if (path is null)
        {
            _status = "swapchain attached; " + Mesh3dGltf.DescribeLoad(null);
        }
        else
        {
            var load = Mesh3dNative.LoadGltf(_host, path);
            _status = load == Mesh3dNative.StatusCode.Ok
                ? "loaded " + Mesh3dGltf.DescribeLoad(path) + " (PBR+IBL path)"
                : $"load {Mesh3dGltf.DescribeLoad(path)} failed ({load}): {Mesh3dNative.LastError(_host)}";
        }

        LastProbe = Mesh3dNative.ProbeStreamline(_host);
        _ = Mesh3dNative.RenderFrame(_host);
        return true;
    }

    public void Resize(double dipW, double dipH, float rasterScale)
    {
        if (_disposed || _host == 0)
        {
            return;
        }

        var (w, h) = PhysicalSize(dipW, dipH, rasterScale);
        _ = Mesh3dNative.Resize(_host, w, h);
        _ = Mesh3dNative.RenderFrame(_host);
        LastProbe = Mesh3dNative.ProbeStreamline(_host);
    }

    public void Tick()
    {
        if (_disposed || _host == 0)
        {
            return;
        }

        _ = Mesh3dNative.RenderFrame(_host);
        LastProbe = Mesh3dNative.ProbeStreamline(_host);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_swapChain != 0)
            {
                var native = _panel.As<ISwapChainPanelNative>();
                _ = native.SetSwapChain(IntPtr.Zero);
            }
        }
        catch
        {
            // panel may already be gone
        }

        DestroyHost();
    }

    private void DestroyHost()
    {
        if (_swapChain != 0)
        {
            Marshal.Release(_swapChain);
            _swapChain = 0;
        }

        if (_host != 0)
        {
            Mesh3dNative.DestroyHost(_host);
            _host = 0;
        }
    }

    private static (uint W, uint H) PhysicalSize(double dipW, double dipH, float rasterScale)
    {
        var scale = rasterScale <= 0 ? 1f : rasterScale;
        var w = Math.Max(2, (int)Math.Ceiling(dipW * scale));
        var h = Math.Max(2, (int)Math.Ceiling(dipH * scale));
        return ((uint)w, (uint)h);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
    private interface ISwapChainPanelNative
    {
        [PreserveSig]
        int SetSwapChain(IntPtr swapChain);
    }
}
