using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;
using WinRT;

namespace Palace.Services;

/// <summary>
/// Diligent D3D12/D3D11 host attached to a WinUI <see cref="SwapChainPanel"/>
/// for Smooth Viewer V0. Creates a composition DXGI swapchain (RGBA16F scRGB,
/// latency 1), binds via <c>ISwapChainPanelNative</c>, draws the HDR test
/// pattern, and logs real frame times.
/// </summary>
internal sealed class SmoothDiligentPresenter : IDisposable
{
    private readonly SwapChainPanel _panel;
    private readonly SmoothPresenterLifecycle _life = new();
    private readonly string? _logDataDir;
    private nint _host;
    private nint _swapChain;
    private bool _disposed;
    private string _status = "idle";
    private string _tileStatus = "tiles idle";

    public SmoothDiligentPresenter(SwapChainPanel panel, string? logDataDir = null)
    {
        _panel = panel;
        _logDataDir = logDataDir;
    }

    public string Status => _status;

    public string TileStatus => _tileStatus;

    public SmoothPresenterLifecycle Lifecycle => _life;

    public SmoothPresenterNative.FrameStats LastStats { get; private set; }

    /// <summary>
    /// V1: create GPU tile pool from session LRU capacity, set shader dirs,
    /// upload coarse pinned tiles. Keeps V0 pattern present until V2 viewport.
    /// </summary>
    public bool TryInitV1TilePool(SmoothTileSession session)
    {
        if (_disposed || _host == 0)
        {
            _tileStatus = "no host";
            return false;
        }

        var shaderDir = System.IO.Path.Combine(AppContext.BaseDirectory, "shaders");
        _ = SmoothPresenterNative.SetShaderSearchDirs(_host, shaderDir);

        var create = SmoothPresenterNative.TilePoolCreate(_host, (uint)session.PoolCapacity);
        if (create != SmoothPresenterNative.StatusCode.Ok)
        {
            _tileStatus = $"tile pool failed ({create}): {SmoothPresenterNative.LastError(_host)}";
            return false;
        }

        var uploaded = 0;
        var lw = SmoothTileMath.LevelWidth(session.FullWidth, session.CoarseLevel);
        var lh = SmoothTileMath.LevelHeight(session.FullHeight, session.CoarseLevel);
        var (nx, ny) = SmoothTileMath.TileCounts(lw, lh);
        for (var ty = 0; ty < ny; ty++)
        {
            for (var tx = 0; tx < nx; tx++)
            {
                var id = new SmoothTileId(session.CoarseLevel, tx, ty);
                if (!session.Cpu.TryGet(id, out var bytes))
                {
                    continue;
                }

                if (!session.Lru.TryGetSlot(id, out var slot))
                {
                    if (!session.Lru.TryInsert(id, out slot, out _))
                    {
                        continue;
                    }
                }

                var up = SmoothPresenterNative.TileUpload(_host, (uint)slot, bytes);
                if (up == SmoothPresenterNative.StatusCode.Ok)
                {
                    uploaded++;
                }
            }
        }

        var mip = SmoothPresenterNative.TileMipReady(_host) ? "mipCS=yes" : "mipCS=no";
        _tileStatus =
            $"pool={session.PoolCapacity} uploaded={uploaded}/{session.CoarseTileCount} " +
            $"vramSlots={session.Lru.ResidentCount} {mip} · {SmoothPresenterNative.LastError(_host)}";
        return uploaded > 0;
    }

    public bool TryStart(double dipW, double dipH, float scaleX, float scaleY)
    {
        if (_disposed)
        {
            _status = "disposed";
            return false;
        }

        if (!SmoothPresenterNative.IsSupportedArchitecture)
        {
            _status = "x64 only — " + SmoothPresenterNative.ArchitectureCaption;
            return false;
        }

        if (!SmoothPresenterNative.IsAvailable)
        {
            _status = SmoothPresenterNative.AvailabilityCaption;
            return false;
        }

        DestroyHost();
        _host = SmoothPresenterNative.CreateHost(SmoothPresenterNative.Backend.Auto);
        if (_host == 0)
        {
            _status = "psv_create failed: " + SmoothPresenterNative.LastError(0);
            return false;
        }

        _life.MarkCreated();
        _ = SmoothPresenterNative.SetDpiScale(_host, scaleX, scaleY);
        _life.TrySetDpi(scaleX, scaleY);

        var (w, h) = SmoothPresenterLifecycle.PhysicalSize(dipW, dipH, scaleX, scaleY);
        var create = SmoothPresenterNative.CreateCompositionSwapChain(_host, w, h, out _swapChain);
        if (create != SmoothPresenterNative.StatusCode.Ok || _swapChain == 0)
        {
            _status = $"composition swapchain failed ({create}): {SmoothPresenterNative.LastError(_host)}";
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
                DestroyHost();
                return false;
            }
        }
        catch (Exception ex)
        {
            _status = "SetSwapChain threw " + ex.GetType().Name + ": " + ex.Message;
            DestroyHost();
            return false;
        }

        _life.MarkAttached(w, h);
        _status = $"attached {w}x{h} {SmoothPresenterNative.GetBackend(_host)} — {SmoothPresenterNative.LastError(_host)}";
        return Tick();
    }

    public void Resize(double dipW, double dipH, float scaleX, float scaleY)
    {
        if (_disposed || _host == 0)
        {
            return;
        }

        _ = SmoothPresenterNative.SetDpiScale(_host, scaleX, scaleY);
        if (_life.TrySetDpi(scaleX, scaleY))
        {
            _status = $"dpi {_life.DpiScaleX:F2}x{_life.DpiScaleY:F2}";
        }

        if (dipW < 2 || dipH < 2)
        {
            // Minimize / zero-size: skip resize; keep last good buffers.
            return;
        }

        var (w, h) = SmoothPresenterLifecycle.PhysicalSize(dipW, dipH, scaleX, scaleY);
        if (!_life.TryResize(w, h))
        {
            return;
        }

        var resize = SmoothPresenterNative.Resize(_host, w, h);
        if (resize == SmoothPresenterNative.StatusCode.DeviceLost)
        {
            HandleDeviceLost();
            return;
        }

        if (resize != SmoothPresenterNative.StatusCode.Ok)
        {
            // Do not CommitResize — lifecycle size stays at last good so we retry.
            _status = $"resize failed ({resize}): {SmoothPresenterNative.LastError(_host)}";
            return;
        }

        _life.CommitResize(w, h);
        _ = Tick();
    }

    public bool Tick()
    {
        if (_disposed || _host == 0)
        {
            return false;
        }

        var result = SmoothPresenterNative.RenderFrame(_host);
        if (result == SmoothPresenterNative.StatusCode.DeviceLost)
        {
            HandleDeviceLost();
            return false;
        }

        if (result != SmoothPresenterNative.StatusCode.Ok)
        {
            _status = $"render failed ({result}): {SmoothPresenterNative.LastError(_host)}";
            return false;
        }

        if (SmoothPresenterNative.TryLastFrameStats(_host, out var stats))
        {
            LastStats = stats;
            // lastError cleared natively after a good present — note is empty unless skip/wait.
            var note = SmoothPresenterNative.LastError(_host);
            SmoothPresenterLog.AppendStats(_logDataDir, stats, note);
            _status =
                $"frame {stats.FrameIndex} {stats.Width}x{stats.Height} " +
                $"total={stats.TotalMs:F2}ms wait={stats.WaitMs:F2} render={stats.RenderMs:F2} present={stats.PresentMs:F2}";
        }

        _life.MarkRunning();
        return true;
    }

    public bool TryRecreateAfterDeviceLost(double dipW, double dipH, float scaleX, float scaleY)
    {
        if (_disposed || !_life.NeedsRecreate)
        {
            return false;
        }

        _life.BeginRecreate();
        return TryStart(dipW, dipH, scaleX, scaleY);
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
        _life.MarkDisposed();
    }

    private void HandleDeviceLost()
    {
        _life.MarkDeviceLost();
        _status = "device lost — recreate required";
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
        if (_host != 0)
        {
            SmoothPresenterNative.TilePoolDestroy(_host);
        }

        if (_swapChain != 0)
        {
            Marshal.Release(_swapChain);
            _swapChain = 0;
        }

        if (_host != 0)
        {
            SmoothPresenterNative.DestroyHost(_host);
            _host = 0;
        }
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
