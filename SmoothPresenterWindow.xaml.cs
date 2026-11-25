using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Palace.Helpers;
using Palace.Imaging;
using Palace.Services;
using Windows.Foundation;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using VirtualKey = Windows.System.VirtualKey;

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

    private float _zoom = 1f;
    private float _tx;
    private float _ty;
    private float _targetZoom = 1f;
    private float _fitZoom = 1f;
    private bool _panning;
    private Point _lastPan;
    private bool _transformDirty;
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
        var dataDir = Environment.GetEnvironmentVariable("PALACE_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataDir))
        {
            dataDir = CatalogStore.DataDir();
        }

        _workingDecode = new SmoothWorkingDecode();
        TxtSmoothTiles.Text = "decoding Working…";

        _presenter = new SmoothDiligentPresenter(ScpSmooth, dataDir);
        TryStartWhenPanelSized();
        _ = StartWorkingDecodeAsync(dataDir);

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

    private async Task StartWorkingDecodeAsync(string dataDir)
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
            _sourcePath = FindSmoothSource(dataDir);
            if (_sourcePath is not null)
            {
                TxtSmoothTiles.Text = "DecodePathAsync " + Path.GetFileName(_sourcePath) + "…";
                result = await _workingDecode.DecodePathAsync(
                    _sourcePath,
                    SmoothSourceLoad.LoaderForPath(_sourcePath));
                try
                {
                    var loaded = SmoothSourceLoad.LoadLocalFile(_sourcePath);
                    _exifOrientation = Math.Clamp(loaded.Orientation, 1, 8);
                }
                catch
                {
                    _exifOrientation = 1;
                }
            }

            if (result is null)
            {
                // Synthetic ≥64MP under PALACE_DATA_DIR — strip/tile RGBA16F L0 (no box downscale).
                const int w = 10000;
                const int h = 7000; // 70 MP
                Directory.CreateDirectory(dataDir);
                var marker = Path.Combine(dataDir, "smooth-v2-synth-64mp.marker");
                await File.WriteAllTextAsync(
                    marker,
                    $"{w}x{h} strip RGBA16F L0 — no full float Working");
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
        if (_presenter is null)
        {
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
            _poolReady = true;
            StartBackgroundPyramid();
        }

        TxtSmoothTiles.Text = (_tiles?.StatusLine() ?? "") + " · " + _presenter.TileStatus;
    }

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
                    TxtSmoothTiles.Text = session.StatusLine() + " · " + presenter.TileStatus;
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
        var orient = SmoothViewport.ExifOrient.FromExif(_exifOrientation, _storedW, _storedH);
        var (dipW, dipH, sx, sy) = PanelMetrics();
        var panelW = (float)(dipW * sx);
        var panelH = (float)(dipH * sy);
        // Oriented w/h for fit (tiles stay storage-sized).
        _fitZoom = SmoothViewport.FitZoom(panelW, panelH, orient.OrientedWidth, orient.OrientedHeight);
        _zoom = SmoothViewport.ClampZoom(_fitZoom, _fitZoom);
        _targetZoom = _zoom;
        _tx = (panelW - (orient.OrientedWidth * _zoom)) * 0.5f;
        _ty = (panelH - (orient.OrientedHeight * _zoom)) * 0.5f;

        // Should-fix: DisplayTarget from monitor HDR when known (not hardcoded Sdr203).
        var display = SmoothToneGpu.ResolveDisplayTarget(
            displayIsHdr: null,
            displayPeakNits: 0f);
        var packed = SmoothToneGpu.FromStats(working.Stats, display);
        _ = presenter.TryArmV2Viewport(_zoom, _tx, _ty, packed.ToneCb, packed.LutSamples);
    }

    private void ScpSmooth_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.R)
        {
            return;
        }

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        _exifOrientation = SmoothViewport.ExifOrient.Rotate90(_exifOrientation, clockwise: !shift);
        ApplyOrientationToPresenter();
        if (_tiles is not null && _presenter is not null && _working is not null)
        {
            ArmV2Viewport(_tiles, _presenter, _working);
        }

        e.Handled = true;
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

        // Ease toward target about last cursor (device px); Point is double.
        var about = SmoothViewport.ZoomAbout(
            _zoom, _tx, _ty, eased, (float)_lastPan.X, (float)_lastPan.Y);
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
        var (dipW, dipH, sx, sy) = PanelMetrics();
        var cx = (float)(pt.Position.X * sx);
        var cy = (float)(pt.Position.Y * sy);
        _lastPan = new Point(cx, cy);

        var delta = pt.Properties.MouseWheelDelta;
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        // Ctrl+wheel = finer zoom steps; plain wheel = larger.
        var factor = ctrl
            ? (delta > 0 ? 1.05f : 1f / 1.05f)
            : (delta > 0 ? 1.12f : 1f / 1.12f);
        _targetZoom = SmoothViewport.ClampZoom(_targetZoom * factor, _fitZoom);
        var about = SmoothViewport.ZoomAbout(_zoom, _tx, _ty, _targetZoom, cx, cy);
        // Immediate ZoomAbout for responsiveness; EaseZoom catches up on timer.
        _zoom = about.Zoom;
        _tx = about.Tx;
        _ty = about.Ty;
        _transformDirty = true;
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
        _lastPan = pt.Position;
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
        var dx = (float)((pt.Position.X - _lastPan.X) * sx);
        var dy = (float)((pt.Position.Y - _lastPan.Y) * sy);
        _tx += dx;
        _ty += dy;
        _lastPan = pt.Position;
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
        // Snap zoom at rest.
        _zoom = _targetZoom;
        _transformDirty = true;
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

        _presenter.Resize(dipW, dipH, sx, sy);
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

        _presenter.Resize(dipW, dipH, sx, sy);
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
