using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Palace.Helpers;
using Palace.Imaging;
using Palace.Services;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Palace;

/// <summary>
/// Experimental Diligent SwapChainPanel host for Smooth Viewer V3 (input feel +
/// streamed decode). Primary path: Working decode (not procedural). Opened from
/// Settings when the smooth presenter toggle is on (default off).
/// </summary>
public sealed partial class SmoothPresenterWindow : Window
{
    private SmoothDiligentPresenter? _presenter;
    private SmoothTileSession? _tiles;
    private SmoothWorkingDecode? _workingDecode;
    private SmoothWorkingDecodeResult? _working;
    private DispatcherQueueTimer? _timer;
    private CancellationTokenSource? _buildCts;
    private Task? _buildTask;
    private bool _activated;
    private bool _hostStarted;
    private bool _poolReady;
    private int _deviceLostRetryCount;
    private long _nextRecreateDueQpc;
    private bool _gpuUnavailable;

    private float _zoom = 1f;
    private float _tx;
    private float _ty;
    private float _targetZoom = 1f;
    private float _fitZoom = 1f;
    private bool _panning;
    private bool _pinching;
    /// <summary>Captured pointer for Mouse/Pen pan only (not Touch).</summary>
    private uint? _panPointerId;
    /// <summary>Last zoom-about / pan point in device pixels (not DIP).</summary>
    private float _lastPanX;
    private float _lastPanY;
    private bool _transformDirty;
    private bool _panSnapApplied = true;
    /// <summary>Last panel size used for fit/pan (device px) — SizeChanged preserve.</summary>
    private float _panelDevW = 1f;
    private float _panelDevH = 1f;
    private int _exifOrientation = 1;
    private int _storedW = 1;
    private int _storedH = 1;
    private string? _sourcePath;
    private long _lastEaseQpc;

    // V4 Zoom HUD + pixel grid (behind ViewerSmoothPresenter; off by default).
    private readonly SmoothZoomHudFade _hudFade = new();
    private bool _pixelGridArmed;
    private float _lastAnnouncedZoom = float.NaN;
    private float _zoomAtGestureStart = 1f;
    private bool _hudPointerOver;
    private bool _hudFocusWithin;
    private long _lastHudQpc;

    public SmoothPresenterWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
        Closed += OnClosed;
        Activated += OnActivated;
        if (Content is FrameworkElement rootFe)
        {
            rootFe.ActualThemeChanged += (_, _) => ApplyHudTheme();
            ApplyHudTheme();
        }

        // SwapChainPanel is not focusable, so its KeyDown never fires; window-scoped
        // accelerators make R / Shift+R / 0 / 1 / G / +/- reachable.
        if (Content is UIElement root)
        {
            AddAccel(root, VirtualKey.R, () => RotateView(clockwise: true));
            AddAccel(root, VirtualKey.R, () => RotateView(clockwise: false), VirtualKeyModifiers.Shift);
            AddAccel(root, VirtualKey.Number0, ApplyFitZoom);
            AddAccel(root, VirtualKey.Number0, ApplyFitZoom, VirtualKeyModifiers.Control);
            AddAccel(root, VirtualKey.NumberPad0, ApplyFitZoom);
            AddAccel(root, VirtualKey.Number1, ApplyActualZoom);
            AddAccel(root, VirtualKey.Number1, ApplyActualZoom, VirtualKeyModifiers.Control);
            AddAccel(root, VirtualKey.NumberPad1, ApplyActualZoom);
            AddAccel(root, VirtualKey.G, TogglePixelGrid);
            AddAccel(root, VirtualKey.Add, () => NudgeZoom(inward: true));
            AddAccel(root, VirtualKey.Subtract, () => NudgeZoom(inward: false));
        }
    }

    private static void AddAccel(
        UIElement root,
        VirtualKey key,
        Action action,
        VirtualKeyModifiers modifiers = VirtualKeyModifiers.None)
    {
        var accel = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accel.Invoked += (_, a) =>
        {
            action();
            a.Handled = true;
        };
        root.KeyboardAccelerators.Add(accel);
    }

    private void ApplyHudTheme()
    {
        var light = Content is FrameworkElement fe
            && fe.ActualTheme == ElementTheme.Light;
        _hudFade.SetTheme(isLightTheme: light);
        if (_hudFade.IsResting)
        {
            BrdZoomHud.Opacity = SmoothZoomHud.RestOpacity(light);
        }
    }

    private void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (_activated || args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        _activated = true;
        PreparePresenter();
    }

    private void PreparePresenter()
    {
        SmoothSourceAppLoad.Register();
        // Source images come ONLY from PALACE_DATA_DIR (synthetic test data); never
        // scan or write the real catalog data dir. The frame log keeps its V1 dir.
        var sourceDir = Environment.GetEnvironmentVariable("PALACE_DATA_DIR");
        var dataDir = string.IsNullOrWhiteSpace(sourceDir) ? CatalogStore.DataDir() : sourceDir;

        _workingDecode = new SmoothWorkingDecode();
        TxtSmoothTiles.Text = "decoding Working…";

        _presenter = new SmoothDiligentPresenter(ScpSmooth, dataDir);
        TryStartWhenPanelSized();
        _ = StartWorkingDecodeAsync(string.IsNullOrWhiteSpace(sourceDir) ? null : sourceDir);

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) =>
        {
            if (_presenter is null)
            {
                return;
            }

            if (!_hostStarted)
            {
                TryStartWhenPanelSized();
                TxtSmoothStatus.Text = _presenter.Status;
                return;
            }

            TickTransformEase();
            TickZoomHud();

            if (_gpuUnavailable)
            {
                TxtSmoothStatus.Text = SmoothDeviceLostRecreatePolicy.ExhaustedMessage;
                return;
            }

            if (_presenter.Lifecycle.NeedsRecreate)
            {
                HandleNeedsRecreate();
            }
            else
            {
                if (_transformDirty)
                {
                    _presenter.SetViewportTransform(_zoom, _tx, _ty);
                    _transformDirty = false;
                }

                _ = _presenter.Tick();
            }

            TxtSmoothStatus.Text = _presenter.Status;
            /* Live TILES: session StatusLine (resident/…) + presenter stream segment. */
            if (_poolReady && _tiles is not null && !_gpuUnavailable)
            {
                TxtSmoothTiles.Text = TilesStatusLine() + " · " + _presenter.TileStatus;
            }
        };
        _timer.Start();
    }

    private async Task StartWorkingDecodeAsync(string? sourceDir)
    {
        if (_workingDecode is null)
        {
            return;
        }

        try
        {
            SmoothWorkingDecodeResult? result = null;
            _exifOrientation = 1;

            // Prefer a real file under PALACE_DATA_DIR when present.
            // Within-budget DecodePath: decoders return upright pixels → orient=1.
            // ≥64MP strip: IgnoreExif tiles + probe EXIF → _exifOrientation for
            // ExifOrient fit/pan/R (Doktor #212). Decode once — no second load.
            _sourcePath = sourceDir is null ? null : FindSmoothSource(sourceDir);
            if (_sourcePath is not null)
            {
                var (decoded, fatal) = await DecodeFileSourceAsync(_sourcePath);
                result = decoded;
                if (fatal)
                {
                    return;
                }

                if (result is null)
                {
                    TxtSmoothTiles.Text =
                        "Working decode failed: " + Path.GetFileName(_sourcePath);
                    return;
                }
            }

            if (result is null)
            {
                // No source file: synthetic SFW ≥64MP strip under temp PALACE_DATA_DIR only.
                const int w = 10000;
                const int h = 7000; // 70 MP
                var route = SmoothWorkingDecode.ClassifyRoute(w, h, stripSamplerAvailable: true);
                if (route != SmoothWorkingDecode.WorkingRoute.Strip)
                {
                    TxtSmoothTiles.Text = "Synthetic strip routing failed";
                    return;
                }

                TxtSmoothTiles.Text = $"strip decode synthetic {w}x{h} (≥64MP)…";
                result = await _workingDecode.DecodeStripToRgba16FAsync(
                    w,
                    h,
                    (x, y) =>
                    {
                        var u = x / (float)Math.Max(1, w - 1);
                        var v = y / (float)Math.Max(1, h - 1);
                        // Checker-ish SFW procedural (no personal assets).
                        var cell = ((x / 64) + (y / 64)) & 1;
                        var baseC = cell == 0 ? 0.2f : 0.55f;
                        return (baseC + 0.3f * u, baseC * 0.8f + 0.2f * v, 0.25f + 0.4f * (1f - u) * v, 1f);
                    });
            }

            if (result is null)
            {
                TxtSmoothTiles.Text = "Working decode failed";
                return;
            }

            await ApplyWorkingResultAsync(result);
        }
        catch (OperationCanceledException)
        {
            // image change / close
        }
        catch (Exception ex)
        {
            TxtSmoothTiles.Text = "Working decode error: " + ex.Message;
        }
    }

    /// <summary>
    /// Route a real file: within budget → DecodePathAsync; over-budget WIC
    /// strip formats → streamed DecodeFileStripAsync; else explicit error.
    /// Returns (result, fatal) where fatal means the UI already shows the error.
    /// </summary>
    private async Task<(SmoothWorkingDecodeResult? Result, bool Fatal)> DecodeFileSourceAsync(
        string path)
    {
        if (_workingDecode is null)
        {
            return (null, true);
        }

        var name = Path.GetFileName(path);
        var size = await SmoothWicStripDecode.TryGetSizeAndOrientationAsync(path);
        if (size is { } s)
        {
            var route = SmoothWorkingDecode.ClassifyFileRoute(path, s.Width, s.Height);
            switch (route)
            {
                case SmoothWorkingDecode.WorkingRoute.Strip:
                    // Tiles stay storage-oriented (IgnoreExif); probe EXIF so
                    // fit/pan/R compose via ExifOrient oriented size (Doktor).
                    _exifOrientation = s.Orientation is >= 1 and <= 8 ? s.Orientation : 1;
                    TxtSmoothTiles.Text =
                        $"strip decode WIC {s.Width}x{s.Height} {name} (≥64MP) orient={_exifOrientation}…";
                    // Same CatalogStore.DataDir() as ImageStats / DisplayTarget notes
                    // (Wimbly: not raw PALACE_DATA_DIR env — Studio scrapes one log).
                    SmoothPresenterLog.AppendNote(
                        CatalogStore.DataDir(),
                        $"strip-file begin {name} {s.Width}x{s.Height} orient={_exifOrientation}");
                    var peakBefore = Environment.WorkingSet;
                    var strip = await SmoothWicStripDecode.DecodeFileStripAsync(
                        _workingDecode, path);
                    var peakAfter = Environment.WorkingSet;
                    SmoothPresenterLog.AppendNote(
                        CatalogStore.DataDir(),
                        $"strip-file end {name} peakPrivateBytes={Math.Max(peakBefore, peakAfter)} "
                        + $"budgetFloatBytes={(long)s.Width * s.Height * 8}");
                    if (strip is null)
                    {
                        TxtSmoothTiles.Text = "WIC strip decode failed: " + name;
                        return (null, true);
                    }

                    return (strip, false);

                case SmoothWorkingDecode.WorkingRoute.UnsupportedOverBudget:
                    TxtSmoothTiles.Text =
                        "Working decode strip not available for over-budget format: " + name;
                    return (null, true);

                case SmoothWorkingDecode.WorkingRoute.WithinBudget:
                default:
                    break;
            }
        }
        else if (!SmoothWorkingDecode.IsStripCapablePath(path))
        {
            // Non-WIC formats: probe via full load path; DecodeCore null = over budget.
        }
        else
        {
            // Strip-capable but size probe failed — try full path; may still error.
        }

        TxtSmoothTiles.Text = "DecodePathAsync " + name + "…";
        var result = await _workingDecode.DecodePathAsync(
            path,
            SmoothSourceLoad.LoaderForPath(path));
        if (result is null)
        {
            // Over-budget non-strip (or decode failure).
            if (!SmoothWorkingDecode.IsStripCapablePath(path))
            {
                TxtSmoothTiles.Text =
                    "Working decode strip not available for over-budget format: " + name;
            }
            else
            {
                TxtSmoothTiles.Text =
                    "Working decode failed or over the 64MP float budget: " + name;
            }

            return (null, true);
        }

        return (result, false);
    }

    private static string? FindSmoothSource(string dataDir)
    {
        if (!Directory.Exists(dataDir))
        {
            return null;
        }

        // Prefer large PNG/JPEG strip fixtures when present under temp PALACE_DATA_DIR.
        foreach (var ext in new[] { "*.png", "*.jpg", "*.jpeg", "*.jxl", "*.avif", "*.exr", "*.hdr", "*.bmp", "*.tif" })
        {
            foreach (var path in Directory.EnumerateFiles(dataDir, ext))
            {
                if (SmoothTileDecodeGate.MayDecodePath(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private Task ApplyWorkingResultAsync(SmoothWorkingDecodeResult result)
    {
        _working = result;
        _storedW = result.Width;
        _storedH = result.Height;
        if (_tiles is null
            || _tiles.FullWidth != result.Width
            || _tiles.FullHeight != result.Height)
        {
            _tiles = new SmoothTileSession(result.Width, result.Height);
            _poolReady = false;
            if (_presenter is not null && _hostStarted
                && _presenter.TryPrepareV1TilePool(_tiles))
            {
                _poolReady = true;
            }
        }

        ApplyOrientationToPresenter();
        // Studio A/B scrapes note=ImageStats from the frame log (strip + normal).
        SmoothPresenterLog.AppendImageStats(CatalogStore.DataDir(), result.Stats);
        TxtSmoothTiles.Text =
            $"Working {result.Width}x{result.Height} gen={result.Generation} orient={_exifOrientation} — building pyramid…";
        if (_poolReady)
        {
            StartBackgroundPyramid();
        }

        return Task.CompletedTask;
    }

    private void ApplyOrientationToPresenter()
    {
        if (_presenter is null)
        {
            return;
        }

        var orient = SmoothViewport.ExifOrient.FromExif(_exifOrientation, _storedW, _storedH);
        _presenter.SetOrientation(orient);
    }

    private void HandleNeedsRecreate()
    {
        if (_presenter is null || _gpuUnavailable)
        {
            return;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (now < _nextRecreateDueQpc)
        {
            TxtSmoothStatus.Text = _presenter.Status;
            return;
        }

        _buildCts?.Cancel();
        _workingDecode?.Cancel();
        if (_buildTask is not null && !_buildTask.IsCompleted)
        {
            TxtSmoothTiles.Text = "device lost — waiting for build cancel…";
            return;
        }

        _buildTask = null;
        _poolReady = false;
        _presenter.PyramidBuildRunning = false;

        var m = PanelMetrics();
        if (m.DipW < 2 || m.DipH < 2)
        {
            return;
        }

        if (_presenter.TryRecreateAfterDeviceLost(m.DipW, m.DipH, m.Sx, m.Sy, _tiles))
        {
            _deviceLostRetryCount = 0;
            _nextRecreateDueQpc = 0;
            _poolReady = true;
            // Pyramid is image-scoped. GPU recreate re-uploads coarse + re-arms;
            // do not rebuild CPU pyramid when it already finished.
            if (_working is not null && _tiles is not null)
            {
                if (SmoothViewportArmPolicy.MustRebuildPyramidAfterGpuRecreate(_tiles.PyramidBuilt))
                {
                    StartBackgroundPyramid();
                }
                else
                {
                    // Finalize+Arm only after successful recreate (PyramidBuilt → no rebuild).
                    _presenter.FinalizeV1CoarseUploads(_tiles);
                    ArmV2Viewport(_tiles, _presenter, _working);
                }
            }

            TxtSmoothTiles.Text = TilesStatusLine() + " · " + _presenter.TileStatus;
            return;
        }

        _deviceLostRetryCount++;
        var after = SmoothDeviceLostRecreatePolicy.AfterFailedAttempt(_deviceLostRetryCount);
        if (!after.KeepRetrying)
        {
            _gpuUnavailable = true;
            TxtSmoothStatus.Text = after.ExhaustedStatus ?? SmoothDeviceLostRecreatePolicy.ExhaustedMessage;
            TxtSmoothTiles.Text = TxtSmoothStatus.Text;
            return;
        }

        var freq = System.Diagnostics.Stopwatch.Frequency;
        _nextRecreateDueQpc = now + (long)(after.NextBackoffMs / 1000.0 * freq);
        TxtSmoothStatus.Text =
            $"device lost — retry {_deviceLostRetryCount}/{SmoothDeviceLostRecreatePolicy.MaxAttempts} " +
            $"in {after.NextBackoffMs}ms";
        TxtSmoothTiles.Text = TilesStatusLine() + " · " + _presenter.TileStatus;
    }

    private string TilesStatusLine() =>
        _tiles?.StatusLine(SmoothDiligentPresenter.GpuEvidenceEnabled) ?? "";

    private void TryStartWhenPanelSized()
    {
        if (_presenter is null || _hostStarted)
        {
            return;
        }

        var (dipW, dipH, sx, sy) = PanelMetrics();
        if (dipW < 2 || dipH < 2)
        {
            TxtSmoothStatus.Text = "waiting for panel size…";
            return;
        }

        if (!_presenter.TryStart(dipW, dipH, sx, sy))
        {
            TxtSmoothStatus.Text = _presenter.Status;
            return;
        }

        _hostStarted = true;
        TxtSmoothStatus.Text = _presenter.Status;

        if (_tiles is not null && _presenter.TryPrepareV1TilePool(_tiles))
        {
            _poolReady = true;
            if (_working is not null)
            {
                TxtSmoothTiles.Text = "pool ready — building pyramid (bg)…";
                StartBackgroundPyramid();
            }
            else
            {
                TxtSmoothTiles.Text = "pool ready — waiting for Working decode…";
            }
        }
        else
        {
            TxtSmoothTiles.Text = _presenter.TileStatus;
        }
    }

    private void StartBackgroundPyramid()
    {
        if (_tiles is null || _presenter is null || !_poolReady || _working is null)
        {
            return;
        }

        if (_buildTask is not null && !_buildTask.IsCompleted)
        {
            return;
        }

        _buildCts?.Cancel();
        _buildCts?.Dispose();
        _buildCts = new CancellationTokenSource();
        var cts = _buildCts;
        var session = _tiles;
        var presenter = _presenter;
        var working = _working;
        var dq = DispatcherQueue;
        presenter.PyramidBuildRunning = true;

        Action<SmoothGpuMipRequest> gpuMip = req =>
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }

            var done = new ManualResetEventSlim(false);
            Exception? error = null;
            if (!dq.TryEnqueue(() =>
                {
                    try
                    {
                        presenter.ExecuteGpuMip(req);
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                }))
            {
                done.Dispose();
                return;
            }

            try
            {
                done.Wait(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            done.Dispose();
            if (error is not null)
            {
                throw error;
            }
        };

        Action afterLevel = () =>
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }

            var done = new ManualResetEventSlim(false);
            if (!dq.TryEnqueue(() =>
                {
                    try
                    {
                        presenter.FlushGpu();
                    }
                    finally
                    {
                        done.Set();
                    }
                }))
            {
                done.Dispose();
                return;
            }

            try
            {
                done.Wait(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            done.Dispose();
        };

        var build = session.BuildFromWorkingAsync(working, cts.Token, gpuMip, afterLevel);
        _buildTask = build;
        _ = build.ContinueWith(
            t =>
            {
                dq.TryEnqueue(() =>
                {
                    presenter.PyramidBuildRunning = false;
                    if (cts.IsCancellationRequested || !ReferenceEquals(_tiles, session))
                    {
                        return;
                    }

                    if (t.IsFaulted)
                    {
                        TxtSmoothTiles.Text =
                            "tile build failed: " + (t.Exception?.GetBaseException().Message ?? "error");
                        return;
                    }

                    if (t.IsCanceled)
                    {
                        return;
                    }

                    presenter.FinalizeV1CoarseUploads(session);
                    ArmV2Viewport(session, presenter, working);
                    TxtSmoothTiles.Text =
                        session.StatusLine(SmoothDiligentPresenter.GpuEvidenceEnabled)
                        + " · " + presenter.TileStatus;
                });
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private void ArmV2Viewport(
        SmoothTileSession session,
        SmoothDiligentPresenter presenter,
        SmoothWorkingDecodeResult working)
    {
        if (!ViewerSettings.SmoothPresenterEnabled)
        {
            return;
        }

        ApplyOrientationToPresenter();
        ApplyFitTransformFromPanel();

        // DisplayTarget from Windows HDR probe. Peak unknown → DisplayForPresent
        // 10000-nit default (gallery). SDR → Sdr203. Raw CCD white logged only.
        var windowsHdr = DisplayHdr.TryWindowsHdrEnabled(null);
        var sdrWhiteRaw = DisplayHdr.TrySdrWhiteLevelNits(null);
        var display = SmoothToneGpu.ResolveDisplayTarget(
            displayIsHdr: windowsHdr,
            displayPeakNits: 0f);
        // Studio scrapes: resolved DisplayTarget + raw CCD / Windows HDR.
        SmoothPresenterLog.AppendNote(
            CatalogStore.DataDir(),
            "DisplayTarget"
            + " IsHdrOutput=" + display.IsHdrOutput
            + " PeakNits=" + display.PeakNits.ToString(CultureInfo.InvariantCulture)
            + " SdrWhiteNits=" + display.SdrWhiteNits.ToString(CultureInfo.InvariantCulture)
            + " PeakNormalized=" + display.PeakNormalized.ToString(CultureInfo.InvariantCulture)
            + " rawSdrWhiteLevel=" + sdrWhiteRaw.ToString(CultureInfo.InvariantCulture)
            + " rawWindowsHdr=" + (windowsHdr?.ToString() ?? "null"));
        var packed = SmoothToneGpu.FromStats(working.Stats, display);
        _ = presenter.TryArmV2Viewport(_zoom, _tx, _ty, packed.ToneCb, packed.LutSamples);
    }

    /// <summary>
    /// Oriented fit + centered pan → <see cref="SmoothDiligentPresenter.SetViewportTransform"/>.
    /// Never re-arms tone/LUT.
    /// </summary>
    private void ApplyFitTransformFromPanel()
    {
        if (_presenter is null)
        {
            return;
        }

        ApplyOrientationToPresenter();
        var orient = SmoothViewport.ExifOrient.FromExif(_exifOrientation, _storedW, _storedH);
        var (dipW, dipH, sx, sy) = PanelMetrics();
        var panelW = (float)(dipW * sx);
        var panelH = (float)(dipH * sy);
        var fit = SmoothViewportArmPolicy.FitCentered(
            panelW, panelH, orient.OrientedWidth, orient.OrientedHeight);
        _fitZoom = fit.FitZoom;
        _zoom = fit.Zoom;
        _targetZoom = fit.Zoom;
        _tx = fit.Tx;
        _ty = fit.Ty;
        _panelDevW = panelW;
        _panelDevH = panelH;
        _presenter.SetViewportTransform(_zoom, _tx, _ty);
        _transformDirty = false;
    }

    /// <summary>
    /// SizeChanged while armed: refit only when at fit; else keep zoom/pan about
    /// the image point under the old panel center (Wimbly blocker on tip 8356b5d).
    /// </summary>
    private void ApplyPanelResizeTransform(float newPanelW, float newPanelH)
    {
        if (_presenter is null)
        {
            return;
        }

        ApplyOrientationToPresenter();
        var orient = SmoothViewport.ExifOrient.FromExif(_exifOrientation, _storedW, _storedH);
        var next = SmoothViewportArmPolicy.AfterPanelResize(
            _panelDevW,
            _panelDevH,
            newPanelW,
            newPanelH,
            _zoom,
            _targetZoom,
            _tx,
            _ty,
            _fitZoom,
            orient.OrientedWidth,
            orient.OrientedHeight);
        _fitZoom = next.FitZoom;
        _zoom = next.Zoom;
        _targetZoom = next.Zoom;
        _tx = next.Tx;
        _ty = next.Ty;
        _panelDevW = newPanelW;
        _panelDevH = newPanelH;
        _presenter.SetViewportTransform(_zoom, _tx, _ty);
        _transformDirty = false;
    }

    private void ScpSmooth_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var keyCode = (int)e.Key;
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (e.Key == VirtualKey.R)
        {
            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(CoreVirtualKeyStates.Down);
            RotateView(clockwise: !shift);
            e.Handled = true;
            return;
        }

        if (SmoothZoomHud.RequestsFit(keyCode))
        {
            ApplyFitZoom();
            e.Handled = true;
            return;
        }

        if (SmoothZoomHud.RequestsActual(keyCode))
        {
            ApplyActualZoom();
            e.Handled = true;
            return;
        }

        if (SmoothZoomHud.TogglesGrid(ctrl, keyCode))
        {
            TogglePixelGrid();
            e.Handled = true;
            return;
        }

        if (SmoothZoomHud.ZoomsIn(keyCode))
        {
            NudgeZoom(inward: true);
            e.Handled = true;
            return;
        }

        if (SmoothZoomHud.ZoomsOut(keyCode))
        {
            NudgeZoom(inward: false);
            e.Handled = true;
        }
    }

    private void ApplyFitZoom()
    {
        if (_presenter is null || !_hostStarted)
        {
            return;
        }

        MarkHudGestureStart();
        ApplyFitTransformFromPanel();
        NotifyHudActivity(gestureEnded: true);
        SyncZoomHudChrome();
    }

    private void ApplyActualZoom()
    {
        if (_presenter is null || !_hostStarted)
        {
            return;
        }

        MarkHudGestureStart();
        var (_, _, sx, sy) = PanelMetrics();
        var panelW = (float)(ScpSmooth.ActualWidth * sx);
        var panelH = (float)(ScpSmooth.ActualHeight * sy);
        var aboutX = panelW * 0.5f;
        var aboutY = panelH * 0.5f;
        var next = SmoothViewport.ZoomAbout(_zoom, _tx, _ty, SmoothZoomHud.ActualZoom, aboutX, aboutY);
        _zoom = next.Zoom;
        _targetZoom = next.Zoom;
        _tx = next.Tx;
        _ty = next.Ty;
        _presenter.SetViewportTransform(_zoom, _tx, _ty);
        _transformDirty = false;
        _panSnapApplied = false;
        NotifyHudActivity(gestureEnded: true);
        SyncZoomHudChrome();
    }

    private void TogglePixelGrid()
    {
        _pixelGridArmed = !_pixelGridArmed;
        _presenter?.SetPixelGridArmed(_pixelGridArmed);
        SyncZoomHudChrome();
        _hudFade.NotifyActivity();
    }

    private void NudgeZoom(bool inward)
    {
        if (_presenter is null || !_hostStarted)
        {
            return;
        }

        MarkHudGestureStart();
        var (_, _, sx, sy) = PanelMetrics();
        _lastPanX = (float)(ScpSmooth.ActualWidth * sx * 0.5);
        _lastPanY = (float)(ScpSmooth.ActualHeight * sy * 0.5);
        var factor = SmoothViewport.WheelZoomFactor(inward ? 1 : -1, fine: false);
        _targetZoom = SmoothViewport.ClampZoom(_targetZoom * factor, _fitZoom);
        SmoothPresenterLog.MarkWheelInput(_targetZoom);
        _panSnapApplied = false;
        NotifyHudActivity(gestureEnded: false);
    }

    private void BtnSmoothFit_Click(object sender, RoutedEventArgs e) => ApplyFitZoom();

    private void BtnSmoothActual_Click(object sender, RoutedEventArgs e) => ApplyActualZoom();

    private void BtnSmoothGrid_Click(object sender, RoutedEventArgs e) => TogglePixelGrid();

    private void BrdZoomHud_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hudPointerOver = true;
        UpdateHudPointerOrFocus();
    }

    private void BrdZoomHud_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hudPointerOver = false;
        UpdateHudPointerOrFocus();
    }

    private void BrdZoomHud_GotFocus(object sender, RoutedEventArgs e)
    {
        _hudFocusWithin = true;
        UpdateHudPointerOrFocus();
    }

    private void BrdZoomHud_LostFocus(object sender, RoutedEventArgs e)
    {
        _hudFocusWithin = false;
        UpdateHudPointerOrFocus();
    }

    private void HudButton_GotFocus(object sender, RoutedEventArgs e)
    {
        _hudFocusWithin = true;
        UpdateHudPointerOrFocus();
    }

    private void HudButton_LostFocus(object sender, RoutedEventArgs e)
    {
        // Focus may move to another HUD button — defer; next GotFocus restores.
        _hudFocusWithin = BtnZoomHudFit.FocusState != FocusState.Unfocused
            || BtnZoomHudActual.FocusState != FocusState.Unfocused
            || BtnZoomHudGrid.FocusState != FocusState.Unfocused;
        UpdateHudPointerOrFocus();
    }

    private void UpdateHudPointerOrFocus() =>
        _hudFade.SetPointerOrFocus(_hudPointerOver || _hudFocusWithin);

    private void MarkHudGestureStart() => _zoomAtGestureStart = _zoom;

    private void NotifyHudActivity(bool gestureEnded)
    {
        _hudFade.NotifyActivity();
        MaybeAnnounceZoom(gestureEnded);
        SyncZoomHudChrome();
    }

    private void TickZoomHud()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        float dt;
        if (_lastHudQpc == 0)
        {
            dt = 1f / 60f;
        }
        else
        {
            dt = SmoothViewport.ClampFrameDt(
                (float)((now - _lastHudQpc) / (double)System.Diagnostics.Stopwatch.Frequency));
        }

        _lastHudQpc = now;

        // When ease is still running, treat as activity so the pill stays full.
        if (!SmoothViewport.IsEaseSettled(_zoom, _targetZoom) || _panning || _pinching)
        {
            _hudFade.NotifyActivity();
        }

        BrdZoomHud.Opacity = _hudFade.Tick(dt);
        SyncZoomHudChrome();
    }

    private void SyncZoomHudChrome()
    {
        var displayZoom = _zoom;
        TxtZoomHudPercent.Text = SmoothZoomHud.FormatPercent(displayZoom);

        var atFit = SmoothZoomHud.IsAtFit(displayZoom, _fitZoom);
        var atActual = SmoothZoomHud.IsAtActual(displayZoom);
        BtnSmoothToolbarFit.IsChecked = atFit;
        BtnZoomHudFit.IsChecked = atFit;
        BtnSmoothToolbarActual.IsChecked = atActual;
        BtnZoomHudActual.IsChecked = atActual;
        BtnSmoothToolbarGrid.IsChecked = _pixelGridArmed;
        BtnZoomHudGrid.IsChecked = _pixelGridArmed;

        var gridTip = SmoothZoomHud.GridTooltip(displayZoom);
        ToolTipService.SetToolTip(BtnSmoothToolbarGrid, gridTip);
        ToolTipService.SetToolTip(BtnZoomHudGrid, gridTip);

        var dim = SmoothZoomHud.IsGridIconDimmed(displayZoom);
        IcnZoomHudGrid.Opacity = dim && _pixelGridArmed ? 0.45 : (dim ? 0.55 : 1.0);
        BtnZoomHudGrid.Opacity = dim ? 0.65 : 1.0;
    }

    private void MaybeAnnounceZoom(bool gestureEnded)
    {
        var prev = float.IsNaN(_lastAnnouncedZoom) ? _zoomAtGestureStart : _lastAnnouncedZoom;
        if (!SmoothZoomHud.ShouldAnnounce(prev, _zoom, _fitZoom, gestureEnded))
        {
            return;
        }

        _lastAnnouncedZoom = _zoom;
        // LiveSetting=Polite on TxtZoomHudPercent: rewrite Name so Narrator hears the value.
        AutomationProperties.SetName(TxtZoomHudPercent, SmoothZoomHud.AnnounceText(_zoom));
    }

    private void RotateView(bool clockwise)
    {
        _exifOrientation = SmoothViewport.ExifOrient.Rotate90(_exifOrientation, clockwise);
        // Before arm / during build: store orientation only. ArmV2Viewport at build
        // completion applies SetOrientation + fit. Never re-arm tone/LUT on rotate.
        if (_presenter is null
            || !SmoothViewportArmPolicy.MayApplyRotateTransform(
                _presenter.IsViewportArmed,
                _presenter.PyramidBuildRunning))
        {
            return;
        }

        ApplyFitTransformFromPanel();
    }

    private void TickTransformEase()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var freq = System.Diagnostics.Stopwatch.Frequency;
        float dtSeconds;
        if (_lastEaseQpc == 0)
        {
            dtSeconds = 1f / 60f;
        }
        else
        {
            dtSeconds = (float)((now - _lastEaseQpc) / (double)freq);
        }

        _lastEaseQpc = now;
        dtSeconds = SmoothViewport.ClampFrameDt(dtSeconds);

        if (SmoothViewport.IsEaseSettled(_zoom, _targetZoom))
        {
            if (_zoom != _targetZoom)
            {
                _zoom = _targetZoom;
                _transformDirty = true;
            }

            TrySnapPanAtRest();
            return;
        }

        _panSnapApplied = false;
        var eased = SmoothViewport.EaseZoom(_zoom, _targetZoom, dtSeconds);
        if (MathF.Abs(eased - _targetZoom) < 1e-3f)
        {
            eased = _targetZoom;
        }

        // Ease toward target about last cursor (device px).
        var about = SmoothViewport.ZoomAbout(
            _zoom, _tx, _ty, eased, _lastPanX, _lastPanY);
        _zoom = about.Zoom;
        _tx = about.Tx;
        _ty = about.Ty;
        _transformDirty = true;
    }

    /// <summary>
    /// Round pan T to whole device px only when input is at rest (ease settled,
    /// no active pan/pinch). Allowed at any zoom including z≥4 at rest.
    /// </summary>
    private void TrySnapPanAtRest()
    {
        if (_panSnapApplied || _panning || _pinching)
        {
            return;
        }

        if (!SmoothViewport.IsEaseSettled(_zoom, _targetZoom))
        {
            return;
        }

        var (stx, sty) = SmoothViewport.SnapPanTranslation(_tx, _ty);
        if (stx != _tx || sty != _ty)
        {
            _tx = stx;
            _ty = sty;
            _transformDirty = true;
        }

        _panSnapApplied = true;
    }

    private void ScpSmooth_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_presenter is null || !_hostStarted)
        {
            return;
        }

        var pt = e.GetCurrentPoint(ScpSmooth);
        var (_, _, sx, sy) = PanelMetrics();
        // Device px so TickTransformEase ZoomAbout matches pan space.
        _lastPanX = (float)(pt.Position.X * sx);
        _lastPanY = (float)(pt.Position.Y * sy);

        var delta = pt.Properties.MouseWheelDelta;
        // WinUI: 120 per notch. Negative = zoom out.
        var notches = delta == 0 ? 0 : delta / 120;
        if (notches == 0 && delta != 0)
        {
            notches = delta > 0 ? 1 : -1;
        }

        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        var factor = SmoothViewport.WheelZoomFactor(notches, fine: ctrl);
        // Only move the target — EaseZoom / ZoomAbout run on the timer (no skip).
        MarkHudGestureStart();
        _targetZoom = SmoothViewport.ClampZoom(_targetZoom * factor, _fitZoom);
        // Present-path latency (PALACE_SMOOTH_FRAME_LOG only): QPC → first Present.
        SmoothPresenterLog.MarkWheelInput(_targetZoom);
        _panSnapApplied = false;
        NotifyHudActivity(gestureEnded: false);
        e.Handled = true;
    }

    private void ScpSmooth_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
    {
        // Touch pinch/pan owns the gesture — drop any Mouse/Pen pointer pan.
        EndPointerPanClean();
        _pinching = true;
        _panSnapApplied = false;
        e.Handled = true;
    }

    private void ScpSmooth_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (_presenter is null || !_hostStarted || e.IsInertial)
        {
            return;
        }

        var (_, _, sx, sy) = PanelMetrics();
        var cx = (float)(e.Position.X * sx);
        var cy = (float)(e.Position.Y * sy);
        var scale = e.Delta.Scale <= 0 ? 1f : (float)e.Delta.Scale;
        var dx = (float)(e.Delta.Translation.X * sx);
        var dy = (float)(e.Delta.Translation.Y * sy);
        // Scale about centroid, then T += Translation×scale. Do not touch _lastPan
        // (pointer pan is Mouse/Pen only — Isiac two-finger fly-off).
        var next = SmoothViewport.ManipulationScaleTranslate(
            _zoom, _tx, _ty, scale, cx, cy, dx, dy, _fitZoom);
        MarkHudGestureStart();
        _zoom = next.Zoom;
        _targetZoom = next.Zoom;
        _tx = next.Tx;
        _ty = next.Ty;
        _transformDirty = true;
        _panSnapApplied = false;
        NotifyHudActivity(gestureEnded: false);
        e.Handled = true;
    }

    private void ScpSmooth_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        _pinching = false;
        TrySnapPanAtRest();
        NotifyHudActivity(gestureEnded: true);
        e.Handled = true;
    }

    private void ScpSmooth_ManipulationInertiaStarting(object sender, ManipulationInertiaStartingRoutedEventArgs e)
    {
        // No inertia — pinch/pan 1:1 ends on finger lift.
        e.Handled = true;
    }

    private void ScpSmooth_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var device = e.Pointer.PointerDeviceType;
        var isMouseOrPen = device is PointerDeviceType.Mouse or PointerDeviceType.Pen;
        var pt = e.GetCurrentPoint(ScpSmooth);
        if (!SmoothViewport.AllowsPointerPanBegin(isMouseOrPen, pt.Properties.IsLeftButtonPressed))
        {
            return;
        }

        // Already panning another pointer — ignore second (no tx/ty).
        if (_panning)
        {
            return;
        }

        _panning = true;
        _panPointerId = e.Pointer.PointerId;
        _panSnapApplied = false;
        var (_, _, sx, sy) = PanelMetrics();
        _lastPanX = (float)(pt.Position.X * sx);
        _lastPanY = (float)(pt.Position.Y * sy);
        ScpSmooth.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ScpSmooth_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_presenter is null
            || !SmoothViewport.IsActivePointerPan(_panning, _panPointerId, e.Pointer.PointerId))
        {
            return;
        }

        var pt = e.GetCurrentPoint(ScpSmooth);
        var (_, _, sx, sy) = PanelMetrics();
        var x = (float)(pt.Position.X * sx);
        var y = (float)(pt.Position.Y * sy);
        _tx += x - _lastPanX;
        _ty += y - _lastPanY;
        _lastPanX = x;
        _lastPanY = y;
        _transformDirty = true;
        NotifyHudActivity(gestureEnded: false);
        e.Handled = true;
    }

    private void ScpSmooth_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        // CaptureLost / Canceled / Released — only our captured Mouse/Pen id.
        if (!SmoothViewport.IsActivePointerPan(_panning, _panPointerId, e.Pointer.PointerId))
        {
            return;
        }

        EndPointerPanClean();
        TrySnapPanAtRest();
        NotifyHudActivity(gestureEnded: true);
        e.Handled = true;
    }

    /// <summary>Clear Mouse/Pen pan state without touching zoom (manip may own next).</summary>
    private void EndPointerPanClean()
    {
        if (!_panning && _panPointerId is null)
        {
            return;
        }

        _panning = false;
        _panPointerId = null;
        ScpSmooth.ReleasePointerCaptures();
    }

    private void ScpSmooth_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_presenter is null)
        {
            return;
        }

        var (dipW, dipH, sx, sy) = PanelMetrics();
        if (dipW < 2 || dipH < 2)
        {
            return;
        }

        if (!_hostStarted)
        {
            TryStartWhenPanelSized();
            return;
        }

        // ResizeBuffers only — never StartBackgroundPyramid (image-scoped).
        _presenter.Resize(dipW, dipH, sx, sy);
        if (_presenter.IsViewportArmed)
        {
            ApplyPanelResizeTransform((float)(dipW * sx), (float)(dipH * sy));
        }

        TxtSmoothStatus.Text = _presenter.Status;
    }

    private void ScpSmooth_CompositionScaleChanged(SwapChainPanel sender, object args)
    {
        if (_presenter is null || !_hostStarted)
        {
            return;
        }

        var (dipW, dipH, sx, sy) = PanelMetrics();
        if (dipW < 2 || dipH < 2)
        {
            return;
        }

        // ResizeBuffers + preserve zoom/pan (or refit if at fit) — never rebuild pyramid.
        _presenter.Resize(dipW, dipH, sx, sy);
        if (_presenter.IsViewportArmed)
        {
            ApplyPanelResizeTransform((float)(dipW * sx), (float)(dipH * sy));
        }

        TxtSmoothStatus.Text = _presenter.Status;
    }

    private (double DipW, double DipH, float Sx, float Sy) PanelMetrics()
    {
        var dipW = ScpSmooth.ActualWidth;
        var dipH = ScpSmooth.ActualHeight;

        var sx = ScpSmooth.CompositionScaleX;
        var sy = ScpSmooth.CompositionScaleY;
        if (sx <= 0)
        {
            sx = (float)(Content?.XamlRoot?.RasterizationScale ?? 1.0);
        }

        if (sy <= 0)
        {
            sy = sx;
        }

        return (dipW, dipH, sx, sy);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _timer?.Stop();
        _timer = null;
        _buildCts?.Cancel();
        _buildCts?.Dispose();
        _buildCts = null;
        _buildTask = null;
        _workingDecode?.Cancel();
        _workingDecode?.Dispose();
        _workingDecode = null;
        _working = null;
        if (_presenter is not null)
        {
            _presenter.PyramidBuildRunning = false;
        }

        _presenter?.Dispose();
        _presenter = null;
        _hostStarted = false;
        _poolReady = false;
    }
}
