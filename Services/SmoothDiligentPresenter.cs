using System.Collections.Concurrent;
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
    private long _lastTileStatusQpc;
    private int _lastTileStatusLevel = int.MinValue;
    private int _lastTileStatusVisible = int.MinValue;
    private int _lastTileStatusResident = int.MinValue;
    private int _lastTileStatusPending = int.MinValue;
    private int _lastTileStatusUploads = int.MinValue;
    private float _readbackMaxAbsError;
    private int _readbackOverUlpCount;
    private int _readbackCompared;
    private readonly byte[] _readbackScratch = new byte[SmoothTileMath.BytesPerTileRgba16F()];
    private float _zoom = 1f;
    private float _tx;
    private float _ty;
    private SmoothViewport.ExifOrient _orient = SmoothViewport.ExifOrient.Identity(1, 1);
    private bool _viewportArmed;
    private bool _pyramidBuildRunning;
    private readonly float[] _viewPack = new float[32];
    private readonly List<(int Tx, int Ty)> _visibleScratch = new();
    private readonly HashSet<SmoothTileId> _underlayScratch = new();
    private readonly List<SmoothViewportFrame.DrawTile> _drawScratch = new();
    private SmoothPresenterNative.TileInstance[] _instanceArray = new SmoothPresenterNative.TileInstance[64];
    private readonly ConcurrentQueue<SmoothTileId> _streamEnsureQueue = new();
    private readonly ConcurrentDictionary<SmoothTileId, byte> _streamEnsurePending = new();
    private readonly ConcurrentDictionary<SmoothTileId, int> _streamVisibleGen = new();
    private int _streamFrameGen;
    private int _streamEnsureRunning;
    private readonly Func<SmoothTileId, uint?> _trySlice;
    private long _lastTickQpc;
    private int _lastUploads;
    /// <summary>GPU stream uploads since this session's pool attach (not coarse Finalize).</summary>
    private int _streamedUploadsSinceLoad;
    private bool _testRemoveDeviceFired;
    /// <summary>True after one successful armed present — next Tick may fire RemoveDevice.</summary>
    private bool _sawArmedPresent;
    private readonly SmoothTickReentrancyGate _tickGate = new();
    private const int MaxUploadsPerFrame = 4;

    /// <summary>True while a background pyramid build owns the session.</summary>
    public bool PyramidBuildRunning
    {
        get => _pyramidBuildRunning;
        set => _pyramidBuildRunning = value;
    }

    public SmoothDiligentPresenter(SwapChainPanel panel, string? logDataDir = null)
    {
        _panel = panel;
        _logDataDir = logDataDir;
        // Cached once — avoid per-frame closure alloc for BuildInto.
        _trySlice = TryGetSlice;
    }

    private uint? TryGetSlice(SmoothTileId id) =>
        _session is not null && _session.Lru.TryGetSlot(id, out var s) ? (uint)s : null;

    public string Status => _status;

    public string TileStatus => _tileStatus;

    public SmoothPresenterLifecycle Lifecycle => _life;

    public SmoothPresenterNative.FrameStats LastStats { get; private set; }

    /// <summary>Uploads performed in the last armed V2 frame (status line).</summary>
    public int LastUploads => _lastUploads;

    public bool IsViewportArmed => _viewportArmed;

    /// <summary>
    /// True when the last <see cref="TryStart"/> failed because the adapter was
    /// still removed (TDR sticky). Drives recreate backoff.
    /// </summary>
    public bool LastCreateWasDeviceLost { get; private set; }

    /// <summary>
    /// Debug: always on. Release: <c>PALACE_SMOOTH_GPU_EVIDENCE=1</c>.
    /// Labels <c>firstCoarseMs</c> as ON vs OFF for Fenrir evidence.
    /// </summary>
    public static bool GpuEvidenceEnabled =>
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
        // New device: counter starts at 0. When the CPU pyramid already finished
        // (device-lost rearm), restore the recorded mip count so evidence is not
        // stuck at gpuMips=0 — tiles re-upload from CPU; mips were built for this image.
        _gpuMipCount = 0;
        _streamedUploadsSinceLoad = 0;
        _readbackMaxAbsError = 0;
        _readbackOverUlpCount = 0;
        _readbackCompared = 0;
        _lastTileStatusQpc = 0;

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

        if (session.PyramidBuilt && session.RecordedGpuMipCount > 0)
        {
            _gpuMipCount = session.RecordedGpuMipCount;
            _tileStatus =
                $"pool={session.PoolCapacity} vramAvail={session.AvailableVramBytes} " +
                $"mipCS={(SmoothPresenterNative.TileMipReady(_host) ? "yes" : "no")} " +
                $"gpuMips={_gpuMipCount} (restored after recreate)";
        }
        else
        {
            _tileStatus =
                $"pool={session.PoolCapacity} vramAvail={session.AvailableVramBytes} " +
                $"mipCS={(SmoothPresenterNative.TileMipReady(_host) ? "yes" : "no")}";
        }

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
        _session?.NoteGpuMipCompleted();

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
        // Path before psv_create so DRED enable / create sticky-lost notes land in
        // the per-run file (native AppendFormatted no-ops until path is set).
        // Never delete before harvest — Fenrir continue must not Remove-Item.
        var runStamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var debugLogDir = string.IsNullOrWhiteSpace(_logDataDir)
            ? Path.GetTempPath()
            : _logDataDir;
        var debugLog = Path.Combine(debugLogDir, "smooth-diligent-debug-" + runStamp + ".log");
        _ = SmoothPresenterNative.SetDebugLogPath(debugLog);
        AppendSmoothEnvNote(debugLog, "PALACE_SMOOTH_DRED");
        AppendSmoothEnvNote(debugLog, "PALACE_SMOOTH_GPU_EVIDENCE");
#if DEBUG
        AppendSmoothEnvNote(debugLog, "PALACE_SMOOTH_TEST_REMOVE_DEVICE");
#endif
        try
        {
            File.AppendAllText(
                debugLog,
                "smooth-debug source=palace severity=NOTE psv=- msg=debugLogPath="
                + debugLog
                + " func=- file=- line=0"
                + Environment.NewLine);
        }
        catch
        {
            // logging must not block start
        }

        _host = SmoothPresenterNative.CreateHost(SmoothPresenterNative.Backend.Auto);
        if (_host == 0)
        {
            var createErr = SmoothPresenterNative.LastError(0);
            _status = "psv_create failed: " + createErr;
            // Sticky TDR: native SetError("device still removed") before delete.
            if (createErr.Contains("device still removed", StringComparison.Ordinal))
            {
                LastCreateWasDeviceLost = true;
            }

            return false;
        }

        LastCreateWasDeviceLost = false;
        _life.MarkCreated();

        _ = SmoothPresenterNative.SetDpiScale(_host, scaleX, scaleY);
        _life.TrySetDpi(scaleX, scaleY);

        var (w, h) = SmoothPresenterLifecycle.PhysicalSize(dipW, dipH, scaleX, scaleY);
        var create = SmoothPresenterNative.CreateCompositionSwapChain(_host, w, h, out _swapChain);
        if (create == SmoothPresenterNative.StatusCode.DeviceLost)
        {
            LastCreateWasDeviceLost = true;
            _status = $"composition swapchain device lost: {SmoothPresenterNative.LastError(_host)}";
            DestroyHost();
            return false;
        }

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

#if DEBUG
    /// <summary>
    /// Fenrir evidence: fire once at Tick <b>start</b> after the first armed present
    /// (not mid-Tick after Push, not at attach) so recreate + coarse re-upload +
    /// re-arm run without TileUpload-on-dead-device.
    /// Env <c>PALACE_SMOOTH_TEST_REMOVE_DEVICE=1</c>. Native needs CMake Debug
    /// (<c>PSV_TEST_HOOKS</c>); Release/CI DLLs stub without calling RemoveDevice.
    /// Returns true when device-lost was handled (caller must skip Push/Render).
    /// </summary>
    private bool MaybeInvokeTestRemoveDeviceOnceArmed()
    {
        if (_testRemoveDeviceFired || !_sawArmedPresent || !_viewportArmed || _host == 0)
        {
            return false;
        }

        if (!string.Equals(
                Environment.GetEnvironmentVariable("PALACE_SMOOTH_TEST_REMOVE_DEVICE"),
                "1",
                StringComparison.Ordinal))
        {
            return false;
        }

        _testRemoveDeviceFired = true;
        QuiesceStreamEnsure();
        var st = SmoothPresenterNative.DebugRemoveDevice(_host);
        _status = $"test RemoveDevice → {st}";
        HandleDeviceLost();
        return true;
    }

    private void QuiesceStreamEnsure()
    {
        Interlocked.Increment(ref _streamFrameGen);
        while (_streamEnsureQueue.TryDequeue(out _))
        {
        }

        _streamEnsurePending.Clear();
        _streamVisibleGen.Clear();
    }
#endif

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

        // Image-scoped pyramid — ResizeBuffers + present only. Never StartBackgroundPyramid.
        // Caller (SizeChanged) recomputes fit/transform for the new panel size.
        _life.CommitResize(w, h);
        _ = Tick();
    }

    /// <summary>
    /// Arm V2 SmoothImage pass with fit zoom + tone LUT. Call only from build
    /// completion (never while a pyramid build is running). Panel-sized SC —
    /// no 8192 DIP zoom. Cleared on device-lost / DestroyHost.
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

        if (_pyramidBuildRunning)
        {
            // Contract: call only from build completion. Soft-fail — Debug.Assert
            // FailFast killed R during an in-flight pyramid (v2-r-root-cause.md).
            System.Diagnostics.Debug.WriteLine(
                "TryArmV2Viewport while pyramid build running — ignored");
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

    /// <summary>EXIF 1–8 matrix + translation (storage→upright); call before arm.</summary>
    public void SetOrientation(in SmoothViewport.ExifOrient orient) => _orient = orient;

    public SmoothViewport.ExifOrient Orientation => _orient;

    public (float Zoom, float Tx, float Ty) GetViewportTransform() => (_zoom, _tx, _ty);

    public int OrientedWidth => _orient.OrientedWidth;

    public int OrientedHeight => _orient.OrientedHeight;

    public bool Tick()
    {
        if (_disposed || _host == 0)
        {
            return false;
        }

        // Nested Tick (Diligent MessageBox / dispatcher pump) must not touch native.
        if (!_tickGate.TryEnter())
        {
            _status = "tick reentered — skipped";
            return false;
        }

        try
        {
#if DEBUG
            // After one armed present: RemoveDevice at Tick start (before any native).
            if (MaybeInvokeTestRemoveDeviceOnceArmed())
            {
                return false;
            }
#endif

            PushV2FrameIfArmed();
            if (_host == 0)
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
                var note = SmoothPresenterNative.LastError(_host);
                SmoothPresenterLog.AppendStats(_logDataDir, stats, note);
                _status =
                    $"frame {stats.FrameIndex} {stats.Width}x{stats.Height} " +
                    $"total={stats.TotalMs:F2}ms wait={stats.WaitMs:F2} render={stats.RenderMs:F2} present={stats.PresentMs:F2}" +
                    (_viewportArmed ? $" z={_zoom:F2} uploads={_lastUploads}" : "");
            }

            if (_viewportArmed)
            {
                _sawArmedPresent = true;
            }

            _life.MarkRunning();
            return true;
        }
        finally
        {
            _tickGate.Exit();
        }
    }

    private void PushV2FrameIfArmed()
    {
        if (!_viewportArmed || _session is null || _host == 0)
        {
            return;
        }

        // Never stream mid-build. Soft-fail — Debug.Assert FailFast is banned here
        // (same class of crash as TryArmV2Viewport on R; see v2-r-root-cause.md).
        if (_pyramidBuildRunning)
        {
            System.Diagnostics.Debug.WriteLine(
                "PushV2FrameIfArmed while pyramid build running — skipped");
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

        var imgToDev = SmoothViewport.Affine2x3.FromZoomPan(_zoom, _tx, _ty, in _orient);
        SmoothViewport.PackViewCb(_viewPack, imgToDev, w, h, _zoom);
        _ = SmoothPresenterNative.ViewportSetView(
            _host,
            SmoothPresenterNative.ViewCb.FromPacked(_viewPack));

        // Stream visible tiles from retained Working → Cpu → GPU (cap uploads/frame).
        // Also stream Level+1 when minifying (z<1) so ParentSlice can bind (N1).
        // Bump frame gen so the stream worker can drop stale (no longer visible) ensures.
        var frameGen = Interlocked.Increment(ref _streamFrameGen);
        var level = SmoothViewport.LevelForZoom(_zoom, _session.MaxLevel);
        var uploads = StreamVisibleLevel(imgToDev, w, h, level, MaxUploadsPerFrame, frameGen);
        if (uploads < 0)
        {
            return;
        }

        // Snapshot visible count for the LevelForZoom level before parent-level
        // StreamVisibleLevel may rewrite _visibleScratch for L+1 underlays.
        var visibleCount = _visibleScratch.Count;

        var totalUploads = uploads;
        if (_zoom < 1f && level < _session.MaxLevel && uploads < MaxUploadsPerFrame)
        {
            var parentUploads = StreamVisibleLevel(
                imgToDev, w, h, level + 1, MaxUploadsPerFrame - uploads, frameGen);
            if (parentUploads < 0)
            {
                return;
            }

            totalUploads += parentUploads;
        }

        _lastUploads = totalUploads;
        if (totalUploads > 0)
        {
            _streamedUploadsSinceLoad += totalUploads;
        }

        SmoothViewportFrame.BuildInto(
            imgToDev,
            w,
            h,
            _zoom,
            _session.FullWidth,
            _session.FullHeight,
            _session.MaxLevel,
            _trySlice,
            _crossfade,
            dtMs,
            _visibleScratch,
            _underlayScratch,
            _drawScratch);

        // Live TILES segment — throttle string build (Debug always; Release ≤10 Hz or on change).
        MaybeUpdateTileStatus(level, visibleCount);

        var drawCount = _drawScratch.Count;
        if (_instanceArray.Length < drawCount)
        {
            _instanceArray = new SmoothPresenterNative.TileInstance[Math.Max(drawCount, _instanceArray.Length * 2)];
        }

        for (var i = 0; i < drawCount; i++)
        {
            var d = _drawScratch[i];
            _instanceArray[i] = new SmoothPresenterNative.TileInstance
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

        _ = SmoothPresenterNative.ViewportSetInstances(_host, _instanceArray, drawCount);
    }

    /// <summary>
    /// Upload up to <paramref name="budget"/> missing tiles at <paramref name="level"/>.
    /// CPU ensure runs on a worker; this path only uploads already-resident CPU tiles.
    /// Returns -1 on device-lost.
    /// </summary>
    private int StreamVisibleLevel(
        in SmoothViewport.Affine2x3 imgToDev,
        float panelW,
        float panelH,
        int level,
        int budget,
        int frameGen)
    {
        if (_session is null || _host == 0 || budget < 1)
        {
            return 0;
        }

        SmoothViewport.CollectVisibleTiles(
            imgToDev, panelW, panelH, level, _session.FullWidth, _session.FullHeight, _visibleScratch);
        var uploaded = 0;
        foreach (var (tx, ty) in _visibleScratch)
        {
            var id = new SmoothTileId(level, tx, ty);
            _streamVisibleGen[id] = frameGen;

            if (uploaded >= budget)
            {
                continue;
            }

            if (_session.Lru.TryGetSlot(id, out _))
            {
                continue;
            }

            if (!_session.Cpu.TryGet(id, out var bytes))
            {
                // Off present/UI thread: queue rematerialize; upload next frames.
                EnqueueStreamEnsure(id);
                continue;
            }

            if (!_session.Lru.TryInsert(id, out var slot, out _))
            {
                continue;
            }

            var up = SmoothPresenterNative.TileUpload(_host, (uint)slot, bytes);
            if (up == SmoothPresenterNative.StatusCode.DeviceLost)
            {
                HandleDeviceLost();
                return -1;
            }

            if (up == SmoothPresenterNative.StatusCode.Ok)
            {
                uploaded++;
            }
        }

        return uploaded;
    }

    private void EnqueueStreamEnsure(SmoothTileId id)
    {
        if (!_streamEnsurePending.TryAdd(id, 0))
        {
            return;
        }

        _streamEnsureQueue.Enqueue(id);
        if (Interlocked.CompareExchange(ref _streamEnsureRunning, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(DrainStreamEnsureQueue);
    }

    private void DrainStreamEnsureQueue()
    {
        try
        {
            while (_streamEnsureQueue.TryDequeue(out var id))
            {
                _ = _streamEnsurePending.TryRemove(id, out _);
                var session = _session;
                if (session is null || _disposed || _pyramidBuildRunning)
                {
                    continue;
                }

                // Drop stale: scrolled/zoomed away before the worker ran.
                var gen = Volatile.Read(ref _streamFrameGen);
                if (!_streamVisibleGen.TryGetValue(id, out var marked) || marked != gen)
                {
                    continue;
                }

                _ = session.TryEnsureCpuTile(id, trackStreamLru: true);
            }

            // Bound the gen map (keys for tiles no longer current).
            if (_streamVisibleGen.Count > 256)
            {
                var keep = Volatile.Read(ref _streamFrameGen);
                foreach (var kv in _streamVisibleGen)
                {
                    if (kv.Value != keep)
                    {
                        _ = _streamVisibleGen.TryRemove(kv.Key, out _);
                    }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _streamEnsureRunning, 0);
            // Race: items enqueued after we thought the queue was empty.
            if (!_streamEnsureQueue.IsEmpty
                && Interlocked.CompareExchange(ref _streamEnsureRunning, 1, 0) == 0)
            {
                _ = Task.Run(DrainStreamEnsureQueue);
            }
        }
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
            // Stay in DeviceLost so HandleNeedsRecreate can backoff-retry.
            if (_life.Phase != SmoothPresenterPhase.DeviceLost)
            {
                _life.MarkDeviceLost();
            }

            if (LastCreateWasDeviceLost)
            {
                _status = "device lost — recreate deferred (device still removed)";
            }

            return false;
        }

        var target = session ?? _session;
        if (target is null)
        {
            return true;
        }

        // ApplyVramBudget inside TryPrepare recreates the LRU — no separate Reset.
        if (!TryPrepareV1TilePool(target))
        {
            if (_life.Phase != SmoothPresenterPhase.DeviceLost)
            {
                _life.MarkDeviceLost();
            }

            return false;
        }

        return true;
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
        _viewportArmed = false;
        _crossfade.Clear();
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

    /// <summary>
    /// Log env visibility into the per-run diligent debug log (Release OK).
    /// Packaged launches may not inherit launcher env — surface what we see.
    /// </summary>
    private static void AppendSmoothEnvNote(string debugLog, string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        var shown = string.IsNullOrEmpty(value) ? "(unset)" : value;
        try
        {
            File.AppendAllText(
                debugLog,
                "smooth-debug source=palace severity=NOTE psv=- msg="
                + name
                + "="
                + shown
                + " func=- file=- line=0"
                + Environment.NewLine);
        }
        catch
        {
            // logging must not block start
        }
    }

    private void MaybeUpdateTileStatus(int level, int visibleCount)
    {
        if (_session is null)
        {
            return;
        }

        var resident = _session.Lru.ResidentCount;
        var pending = _streamEnsurePending.Count;
        var changed =
            level != _lastTileStatusLevel
            || visibleCount != _lastTileStatusVisible
            || resident != _lastTileStatusResident
            || pending != _lastTileStatusPending
            || _lastUploads != _lastTileStatusUploads;

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var freq = System.Diagnostics.Stopwatch.Frequency;
        var due = _lastTileStatusQpc == 0
            || ((now - _lastTileStatusQpc) * 1000 / freq) >= 100;

#if !DEBUG
        // Release: rebuild string only on change or ≤10 Hz heartbeat.
        if (!changed && !due)
        {
            return;
        }
#endif

        _lastTileStatusQpc = now;
        _lastTileStatusLevel = level;
        _lastTileStatusVisible = visibleCount;
        _lastTileStatusResident = resident;
        _lastTileStatusPending = pending;
        _lastTileStatusUploads = _lastUploads;
        _tileStatus =
            $"level={level} visible={visibleCount} resident={resident} " +
            $"streamPending={pending} streamed={_streamedUploadsSinceLoad} " +
            $"uploads={_lastUploads} gpuMips={_gpuMipCount}";
    }

    private void DestroyHost()
    {
        _viewportArmed = false;
        _sawArmedPresent = false;
        while (_streamEnsureQueue.TryDequeue(out _))
        {
        }

        _streamEnsurePending.Clear();
        _streamVisibleGen.Clear();
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
