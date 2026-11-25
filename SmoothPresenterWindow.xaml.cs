using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
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
/// Experimental Diligent SwapChainPanel host for Smooth Viewer V2.
/// Primary path: Working decode (not procedural). Opened from Settings when
/// the smooth presenter toggle is on (default off).
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
    /// <summary>Last zoom-about / pan point in device pixels (not DIP).</summary>
    private float _lastPanX;
    private float _lastPanY;
    private bool _transformDirty;
    /// <summary>Last panel size used for fit/pan (device px) — SizeChanged preserve.</summary>
    private float _panelDevW = 1f;
    private float _panelDevH = 1f;
    private int _exifOrientation = 1;
    private int _storedW = 1;
    private int _storedH = 1;
    private string? _sourcePath;

    public SmoothPresenterWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
        Closed += OnClosed;
        Activated += OnActivated;
        // SwapChainPanel is not focusable, so its KeyDown never fires; window-scoped
        // accelerators make R / Shift+R rotate reachable.
        if (Content is UIElement root)
        {
            var cw = new KeyboardAccelerator { Key = VirtualKey.R };
            cw.Invoked += (_, a) => { RotateView(clockwise: true); a.Handled = true; };
            var ccw = new KeyboardAccelerator { Key = VirtualKey.R, Modifiers = VirtualKeyModifiers.Shift };
            ccw.Invoked += (_, a) => { RotateView(clockwise: false); a.Handled = true; };
            root.KeyboardAccelerators.Add(cw);
            root.KeyboardAccelerators.Add(ccw);
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

            // Prefer a real file under PALACE_DATA_DIR when present. JxlDecode /
            // HdrAvifDecode / HdrWicDecode already return upright pixels (EXIF/irot
            // applied by the decoder), so the loader's Orientation is 1 and the view
            // orientation starts at identity; R / Shift+R rotate at draw time.
            // Decode once, on the background task — no second LoadLocalFile here.
            _sourcePath = sourceDir is null ? null : FindSmoothSource(sourceDir);
            if (_sourcePath is not null)
            {
                TxtSmoothTiles.Text = "DecodePathAsync " + Path.GetFileName(_sourcePath) + "…";
                result = await _workingDecode.DecodePathAsync(
                    _sourcePath,
                    SmoothSourceLoad.LoaderForPath(_sourcePath));
                if (result is null)
                {
                    // Do not silently swap in synthetic data (e.g. >64MP real file).
                    TxtSmoothTiles.Text =
                        "Working decode failed or over the 64MP float budget: " + Path.GetFileName(_sourcePath);
                    return;
                }
            }

            if (result is null)
            {
                // No source file: in-memory synthetic ≥64MP, strip/tile RGBA16F L0 (no box downscale).
                const int w = 10000;
                const int h = 7000; // 70 MP
                TxtSmoothTiles.Text = $"strip decode synthetic {w}x{h} (≥64MP)…";
                result = await _workingDecode.DecodeStripToRgba16FAsync(
                    w,
                    h,
                    (x, y) =>
                    {
                        var u = x / (float)Math.Max(1, w - 1);
                        var v = y / (float)Math.Max(1, h - 1);
                        return (0.15f + 0.85f * u, 0.1f + 0.5f * v, 0.2f + 0.4f * (1f - u) * v, 1f);
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

    private static string? FindSmoothSource(string dataDir)
    {
        if (!Directory.Exists(dataDir))
        {
            return null;
        }

        foreach (var ext in new[] { "*.jxl", "*.avif", "*.exr", "*.hdr", "*.png", "*.jpg" })
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

        // Should-fix: DisplayTarget from monitor HDR when known (not hardcoded Sdr203).
        // Same Windows HDR probe as HdrSwapchainPresenter (CCD, any path). Peak
        // unknown → DisplayForPresent's 10000-nit default, like the gallery path.
        var sdrWhite = DisplayHdr.TrySdrWhiteLevelNits(null);
        var display = SmoothToneGpu.ResolveDisplayTarget(
            displayIsHdr: DisplayHdr.TryWindowsHdrEnabled(null),
            displayPeakNits: 0f,
            sdrWhiteNits: sdrWhite > 0f ? sdrWhite : ImagingConstants.RefWhiteNits);
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
        if (e.Key != VirtualKey.R)
        {
            return;
        }

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        RotateView(clockwise: !shift);
        e.Handled = true;
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
        if (MathF.Abs(_targetZoom - _zoom) < 1e-4f)
        {
            if (_zoom != _targetZoom)
            {
                // Snap at rest.
                _zoom = _targetZoom;
                _transformDirty = true;
            }

            return;
        }

        var eased = SmoothViewport.EaseZoom(_zoom, _targetZoom, dtSeconds: 1f / 60f);
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
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        // Ctrl+wheel = finer zoom steps; plain wheel = larger.
        var factor = ctrl
            ? (delta > 0 ? 1.05f : 1f / 1.05f)
            : (delta > 0 ? 1.12f : 1f / 1.12f);
        // Only move the target — EaseZoom / ZoomAbout run on the timer (no skip).
        _targetZoom = SmoothViewport.ClampZoom(_targetZoom * factor, _fitZoom);
        e.Handled = true;
    }

    private void ScpSmooth_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(ScpSmooth);
        if (!pt.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _panning = true;
        var (_, _, sx, sy) = PanelMetrics();
        _lastPanX = (float)(pt.Position.X * sx);
        _lastPanY = (float)(pt.Position.Y * sy);
        ScpSmooth.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ScpSmooth_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_panning || _presenter is null)
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
        e.Handled = true;
    }

    private void ScpSmooth_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_panning)
        {
            return;
        }

        _panning = false;
        ScpSmooth.ReleasePointerCapture(e.Pointer);
        // Do not snap _zoom here — TickTransformEase snaps at rest after ease.
        e.Handled = true;
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
