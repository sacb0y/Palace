using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;
using Palace.Services;

namespace Palace;

/// <summary>
/// Experimental Diligent SwapChainPanel host for Smooth Viewer V0.
/// Opened from Settings when the smooth presenter toggle is on.
/// </summary>
public sealed partial class SmoothPresenterWindow : Window
{
    private SmoothDiligentPresenter? _presenter;
    private SmoothTileSession? _tiles;
    private DispatcherQueueTimer? _timer;
    private bool _activated;
    private bool _hostStarted;

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
        // V1 debug: synthetic ~12k×8k Working image (Fenrir evidence sizes).
        _tiles = new SmoothTileSession(12000, 8000);
        _ = _tiles.BuildSyntheticLevel0AndCoarse();
        TxtSmoothTiles.Text = _tiles.StatusLine();

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
                    _ = _presenter.TryRecreateAfterDeviceLost(m.DipW, m.DipH, m.Sx, m.Sy);
                }
            }
            else
            {
                _ = _presenter.Tick();
            }

            TxtSmoothStatus.Text = _presenter.Status;
            if (_tiles is not null)
            {
                TxtSmoothTiles.Text = _tiles.StatusLine();
            }
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

        TryInitTilePool();
        _hostStarted = true;
        TxtSmoothStatus.Text = _presenter.Status;
    }

    private void TryInitTilePool()
    {
        if (_presenter is null || _tiles is null)
        {
            return;
        }

        _ = _presenter.TryInitV1TilePool(_tiles);
        TxtSmoothTiles.Text = _tiles.StatusLine() + " · " + _presenter.TileStatus;
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
            // Minimize / collapse: skip resize.
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
        // No 640×480 bootstrap — defer start until ActualWidth/Height >= 2.
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
        _presenter?.Dispose();
        _presenter = null;
        _hostStarted = false;
    }
}
