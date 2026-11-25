using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;
using WinRT;

namespace Palace.Services;

/// <summary>
/// Diligent D3D12/D3D11 host attached to a WinUI <see cref="SwapChainPanel"/>
/// for Smooth Viewer. Creates a composition DXGI swapchain (RGBA16F scRGB,
/// latency 1), binds via <c>ISwapChainPanelNative</c>, draws the HDR test
/// pattern / V2 SmoothImage viewport, and hosts the V1 tile pool / mip path.
/// Behind <see cref="ViewerSettings.SmoothPresenterEnabled"/> (default off).
/// </summary>
internal sealed class SmoothDiligentPresenter : IDisposable
{
    private readonly SwapChainPanel _panel;
    private readonly SmoothPresenterLifecycle _life = new();
    private readonly string? _logDataDir;
    private readonly SmoothTileCrossfade _crossfade = new();
    private nint _host;
    private nint _swapChain;
    private bool _disposed;
    private string _status = "idle";
    private string _tileStatus = "tiles idle";
    private SmoothTileSession? _session;
    private int _gpuMipCount;
    private float _readbackMaxAbsError;
    private int _readbackOverUlpCount;
    private int _readbackCompared;
    private readonly byte[] _readbackScratch = new byte[SmoothTileMath.BytesPerTileRgba16F()];
    private float _zoom = 1f;
    private float _tx;
    private float _ty;
    private bool _viewportArmed;
    private readonly float[] _viewPack = new float[32];
    private readonly List<(int Tx, int Ty)> _visibleScratch = new();
    private long _lastTickQpc;

    public SmoothDiligentPresenter(SwapChainPanel panel, string? logDataDir = null)
    {
        _panel = panel;
        _logDataDir = logDataDir;
    }

    public string Status => _status;

    public string TileStatus => _tileStatus;

    public SmoothPresenterLifecycle Lifecycle => _life;

    public SmoothPresenterNative.FrameStats LastStats { get; private set; }

    private static bool GpuEvidenceEnabled =>
#if DEBUG
        true
#else
        string.Equals(
            Environment.GetEnvironmentVariable("PALACE_SMOOTH_GPU_EVIDENCE"),
            "1",
            StringComparison.Ordinal)
#endif
    ;

    /// <summary>
    /// Query real VRAM, size the session pool, create the GPU tile array.
    /// Pyramid build (with marshaled <see cref="ExecuteGpuMip"/>) follows.
    /// ApplyVramBudget recreates the LRU — do not also call ResetLru beforehand.
    /// </summary>
    public bool TryPrepareV1TilePool(SmoothTileSession session)
    {
        if (_disposed || _host == 0)
        {
            _tileStatus = "no host";
            return false;
        }

        _session = session;
        _gpuMipCount = 0;
        _readbackMaxAbsError = 0;
        _readbackOverUlpCount = 0;
        _readbackCompared = 0;

        if (SmoothPresenterNative.QueryVideoMemory(_host, out _, out _, out var available) ==
            SmoothPresenterNative.StatusCode.Ok)
        {
            session.ApplyVramBudget(checked((long)Math.Min(long.MaxValue, available)));
        }
        else
        {
            // Query failed — default pool of 128 (not coarse-only floor).
            // CapacityFromVramBytes uses available/8 ÷ bytes-per-tile.
            session.ApplyVramBudget(128L * SmoothTileMath.BytesPerTileRgba16F() * 8);
        }

        var shaderDir = System.IO.Path.Combine(AppContext.BaseDirectory, "shaders");
        _ = SmoothPresenterNative.SetShaderSearchDirs(_host, shaderDir);

        var create = SmoothPresenterNative.TilePoolCreate(_host, (uint)session.PoolCapacity);
        if (create == SmoothPresenterNative.StatusCode.DeviceLost)
        {
            HandleDeviceLost();
            _tileStatus = "tile pool device lost";
            return false;
        }

        if (create != SmoothPresenterNative.StatusCode.Ok)
        {
            _tileStatus = $"tile pool failed ({create}): {SmoothPresenterNative.LastError(_host)}";
            return false;
        }

        _tileStatus =
            $"pool={session.PoolCapacity} vramAvail={session.AvailableVramBytes} " +
            $"mipCS={(SmoothPresenterNative.TileMipReady(_host) ? "yes" : "no")}";
        return true;
    }

    /// <summary>Presenter-thread GPU upload + TileMipBox (called via UI marshal).</summary>
    public void ExecuteGpuMip(SmoothGpuMipRequest req)
    {
        if (_host == 0 || _disposed)
        {
            return;
        }

        foreach (var (_, slot, bytes) in req.ChildUploads)
        {
            var up = SmoothPresenterNative.TileUpload(_host, slot, bytes);
            if (up == SmoothPresenterNative.StatusCode.DeviceLost)
            {
                HandleDeviceLost();
                return;
            }
        }

        var mip = SmoothPresenterNative.TileMipBox(
            _host,
            (uint)req.ChildLevelWidth,
            (uint)req.ChildLevelHeight,
            req.ParentContentOriginX,
            req.ParentContentOriginY,
            req.DstSlice,
            req.Slices4x4);
        if (mip == SmoothPresenterNative.StatusCode.DeviceLost)
        {
            HandleDeviceLost();
            return;
        }

        if (mip != SmoothPresenterNative.StatusCode.Ok)
        {
            return;
        }

        _gpuMipCount++;

        if (!GpuEvidenceEnabled)
        {
            return;
        }

        var rb = SmoothPresenterNative.TileReadback(_host, req.DstSlice, _readbackScratch);
        if (rb == SmoothPresenterNative.StatusCode.DeviceLost)
        {
            HandleDeviceLost();
            return;
        }

        if (rb != SmoothPresenterNative.StatusCode.Ok)
        {
            return;
        }

        CompareReadback(req.ParentBytes, _readbackScratch);
    }

    private void CompareReadback(byte[] cpuParent, byte[] gpuParent)
    {
        var n = Math.Min(cpuParent.Length, gpuParent.Length) / 2;
        var over = 0;
        var maxAbs = 0f;
        for (var i = 0; i < n; i++)
        {
            var c = (ushort)(cpuParent[i * 2] | (cpuParent[i * 2 + 1] << 8));
            var g = (ushort)(gpuParent[i * 2] | (gpuParent[i * 2 + 1] << 8));
            var cf = HalfBitsToFloat(c);
            var gf = HalfBitsToFloat(g);
            var err = MathF.Abs(cf - gf);
            if (err > maxAbs)
            {
                maxAbs = err;
            }

            // >1 half-ULP at the larger magnitude (approx via nextafter-ish bit delta).
            // ±0 (0x0000 vs 0x8000) is equal, not 32768 ULP apart.
            if (c != g && cf != gf && Math.Abs(c - g) > 1)
            {
                over++;
            }
        }

        _readbackCompared++;
        if (maxAbs > _readbackMaxAbsError)
        {
            _readbackMaxAbsError = maxAbs;
        }

        _readbackOverUlpCount += over;
        SmoothPresenterLog.AppendNote(
            _logDataDir,
            $"gpu-vs-cpu mip#{_gpuMipCount} maxAbs={maxAbs:G6} over1ulp={over}");
    }

    private static float HalfBitsToFloat(ushort h) => (float)BitConverter.UInt16BitsToHalf(h);

    public void FlushGpu()
    {
        if (_host == 0 || _disposed)
        {
            return;
        }

        var flush = SmoothPresenterNative.Flush(_host);
        if (flush == SmoothPresenterNative.StatusCode.DeviceLost)
        {
            HandleDeviceLost();
        }
    }

    /// <summary>After CPU/GPU pyramid: ensure coarse CPU tiles are resident on GPU.</summary>
    public void FinalizeV1CoarseUploads(SmoothTileSession session)
    {
        if (_host == 0 || _disposed)
        {
            return;
        }

        // V1 authoritative path: CPU coarse parents (GPU mips verified via readback in evidence mode).
        var uploaded = UploadCoarseTiles(session);
        FlushGpu();
        _tileStatus =
            $"pool={session.PoolCapacity} uploaded={uploaded}/{session.CoarseTileCount} " +
            $"gpuMips={_gpuMipCount} vramAvail={session.AvailableVramBytes} " +
            $"resident={session.Lru.ResidentCount} " +
            $"rbMax={_readbackMaxAbsError:G4} rbOverUlp={_readbackOverUlpCount} rbN={_readbackCompared} " +
            $"· {SmoothPresenterNative.LastError(_host)}";
    }

    private int UploadCoarseTiles(SmoothTileSession session)
    {
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
                if (up == SmoothPresenterNative.StatusCode.DeviceLost)
                {
                    HandleDeviceLost();
                    return uploaded;
                }

                if (up == SmoothPresenterNative.StatusCode.Ok)
                {
                    uploaded++;
                }
            }
        }

        return uploaded;
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
        _status = $"attached {w}x{h} {SmoothPresenterNative.GetBackend(_host)}";
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
            _status = $"resize failed ({resize}): {SmoothPresenterNative.LastError(_host)}";
            return;
        }

        _life.CommitResize(w, h);
        _ = Tick();
    }

    /// <summary>
    /// Arm V2 SmoothImage pass with fit zoom + tone LUT. No-op when the setting
    /// is off or the tile pool is missing. Panel-sized SC — no 8192 DIP zoom.
    /// </summary>
    public bool TryArmV2Viewport(
        float zoom,
        float tx,
        float ty,
        float[]? toneCb16,
        float[]? lutSamples)
    {
        if (_disposed || _host == 0 || _session is null || !ViewerSettings.SmoothPresenterEnabled)
        {
            return false;
        }

        _zoom = zoom;
        _tx = tx;
        _ty = ty;
        if (toneCb16 is { Length: >= 16 })
        {
            _ = SmoothPresenterNative.ViewportSetTone(
                _host,
                SmoothPresenterNative.ToneCb.FromPacked(toneCb16));
        }

        if (lutSamples is { Length: >= 2 })
        {
            _ = SmoothPresenterNative.ViewportSetLut(_host, lutSamples);
        }

        var enable = SmoothPresenterNative.ViewportEnable(_host, true);
        _viewportArmed = enable == SmoothPresenterNative.StatusCode.Ok;
        return _viewportArmed;
    }

    public void SetViewportTransform(float zoom, float tx, float ty)
    {
        _zoom = zoom;
        _tx = tx;
        _ty = ty;
    }

    public bool Tick()
    {
        if (_disposed || _host == 0)
        {
            return false;
        }

        PushV2FrameIfArmed();

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
            var note = SmoothPresenterNative.LastError(_host);
            SmoothPresenterLog.AppendStats(_logDataDir, stats, note);
            _status =
                $"frame {stats.FrameIndex} {stats.Width}x{stats.Height} " +
                $"total={stats.TotalMs:F2}ms wait={stats.WaitMs:F2} render={stats.RenderMs:F2} present={stats.PresentMs:F2}" +
                (_viewportArmed ? $" z={_zoom:F2}" : "");
        }

        _life.MarkRunning();
        return true;
    }

    private void PushV2FrameIfArmed()
    {
        if (!_viewportArmed || _session is null || _host == 0)
        {
            return;
        }

        var w = _life.Width;
        var h = _life.Height;
        if (w < 2 || h < 2)
        {
            return;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var dtMs = _lastTickQpc == 0
            ? 16f
            : (float)((now - _lastTickQpc) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        _lastTickQpc = now;
        dtMs = Math.Clamp(dtMs, 1f, 100f);

        var imgToDev = SmoothViewport.Affine2x3.FromZoomPan(_zoom, _tx, _ty);
        SmoothViewport.PackViewCb(_viewPack, imgToDev, w, h, _zoom);
        _ = SmoothPresenterNative.ViewportSetView(
            _host,
            SmoothPresenterNative.ViewCb.FromPacked(_viewPack));

        // Stream visible tiles into the GPU LRU (cross-fade on first residency).
        var level = SmoothViewport.LevelForZoom(_zoom, _session.MaxLevel);
        SmoothViewport.CollectVisibleTiles(
            imgToDev, w, h, level, _session.FullWidth, _session.FullHeight, _visibleScratch);
        foreach (var (tx, ty) in _visibleScratch)
        {
            var id = new SmoothTileId(level, tx, ty);
            if (_session.Lru.TryGetSlot(id, out _))
            {
                continue;
            }

            if (!_session.Cpu.TryGet(id, out var bytes))
            {
                continue;
            }

            if (!_session.Lru.TryInsert(id, out var slot, out _))
            {
                continue;
            }

            _ = SmoothPresenterNative.TileUpload(_host, (uint)slot, bytes);
        }

        uint? TrySlice(SmoothTileId id) =>
            _session.Lru.TryGetSlot(id, out var s) ? (uint)s : null;

        var draws = SmoothViewportFrame.Build(
            imgToDev,
            w,
            h,
            _zoom,
            _session.FullWidth,
            _session.FullHeight,
            _session.MaxLevel,
            TrySlice,
            _crossfade,
            dtMs);

        var instances = new SmoothPresenterNative.TileInstance[draws.Count];
        for (var i = 0; i < draws.Count; i++)
        {
            var d = draws[i];
            instances[i] = new SmoothPresenterNative.TileInstance
            {
                ImgOriginX = d.ImgOriginX,
                ImgOriginY = d.ImgOriginY,
                ImgSizeX = d.ImgSizeX,
                ImgSizeY = d.ImgSizeY,
                ParentOriginX = d.ParentOriginX,
                ParentOriginY = d.ParentOriginY,
                LevelScale = d.LevelScale,
                Level = d.Level,
                Slice = d.Slice,
                ParentSlice = d.ParentSlice,
                Opacity = d.Opacity,
                Pad = 0
            };
        }

        _ = SmoothPresenterNative.ViewportSetInstances(_host, instances);
    }

    public bool TryRecreateAfterDeviceLost(
        double dipW,
        double dipH,
        float scaleX,
        float scaleY,
        SmoothTileSession? session = null)
    {
        if (_disposed || !_life.NeedsRecreate)
        {
            return false;
        }

        _life.BeginRecreate();
        if (!TryStart(dipW, dipH, scaleX, scaleY))
        {
            return false;
        }

        var target = session ?? _session;
        if (target is null)
        {
            return true;
        }

        // ApplyVramBudget inside TryPrepare recreates the LRU — no separate Reset.
        return TryPrepareV1TilePool(target);
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
