using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;
using Palace.Services;

namespace Palace;

/// <summary>
/// Experimental Diligent SwapChainPanel host for Smooth Viewer V1.
/// Opened from Settings when the smooth presenter toggle is on.
/// </summary>
public sealed partial class SmoothPresenterWindow : Window
{
    private SmoothDiligentPresenter? _presenter;
    private SmoothTileSession? _tiles;
    private DispatcherQueueTimer? _timer;
    private CancellationTokenSource? _buildCts;
    private bool _activated;
    private bool _hostStarted;
    private bool _poolReady;
    private bool _buildStarted;

    public SmoothPresenterWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
        Closed += OnClosed;
        Activated += OnActivated;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
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
        var dataDir = CatalogStore.DataDir();
        // Session shell only — pyramid waits for host + VRAM + pool (GPU mip marshal).
        _tiles = new SmoothTileSession(12000, 8000);
        TxtSmoothTiles.Text = "waiting for presenter…";

        _presenter = new SmoothDiligentPresenter(ScpSmooth, dataDir);
        TryStartWhenPanelSized();

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

            if (_presenter.Lifecycle.NeedsRecreate)
            {
                var m = PanelMetrics();
                if (m.DipW >= 2 && m.DipH >= 2)
                {
                    _poolReady = false;
                    _buildStarted = false;
                    if (_presenter.TryRecreateAfterDeviceLost(m.DipW, m.DipH, m.Sx, m.Sy, _tiles))
                    {
                        _poolReady = true;
                        StartBackgroundPyramid();
                    }

                    TxtSmoothTiles.Text = (_tiles?.StatusLine() ?? "") + " · " + _presenter.TileStatus;
                }
            }
            else
            {
                _ = _presenter.Tick();
            }

            TxtSmoothStatus.Text = _presenter.Status;
        };
        _timer.Start();
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
            TxtSmoothTiles.Text = "pool ready — building pyramid (bg)…";
            StartBackgroundPyramid();
        }
        else
        {
            TxtSmoothTiles.Text = _presenter.TileStatus;
        }
    }

    private void StartBackgroundPyramid()
    {
        if (_buildStarted || _tiles is null || _presenter is null || !_poolReady)
        {
            return;
        }

        _buildStarted = true;
        _buildCts?.Cancel();
        _buildCts?.Dispose();
        _buildCts = new CancellationTokenSource();
        var cts = _buildCts;
        var session = _tiles;
        var presenter = _presenter;
        var dq = DispatcherQueue;

        // Marshal GPU upload + TileMipBox onto the presenter/UI thread; bg waits.
        session.GpuMip = req =>
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }

            using var done = new ManualResetEventSlim(false);
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
                return;
            }

            done.Wait(cts.Token);
            if (error is not null)
            {
                throw error;
            }
        };

        session.AfterLevel = () =>
        {
            using var done = new ManualResetEventSlim(false);
            _ = dq.TryEnqueue(() =>
            {
                presenter.FlushGpu();
                done.Set();
            });
            done.Wait(cts.Token);
        };

        _ = session.BuildPyramidAsync(cts.Token).ContinueWith(
            t =>
            {
                dq.TryEnqueue(() =>
                {
                    if (cts.IsCancellationRequested || !ReferenceEquals(_tiles, session))
                    {
                        return;
                    }

                    if (t.IsFaulted)
                    {
                        TxtSmoothTiles.Text = "tile build failed: " + (t.Exception?.GetBaseException().Message ?? "error");
                        return;
                    }

                    presenter.FinalizeV1CoarseUploads(session);
                    TxtSmoothTiles.Text = session.StatusLine() + " · " + presenter.TileStatus;
                });
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
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
        if (_tiles is not null)
        {
            _tiles.GpuMip = null;
            _tiles.AfterLevel = null;
        }

        _presenter?.Dispose();
        _presenter = null;
        _hostStarted = false;
        _poolReady = false;
        _buildStarted = false;
    }
}
