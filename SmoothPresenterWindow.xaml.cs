using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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
    private DispatcherQueueTimer? _timer;
    private bool _started;

    public SmoothPresenterWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
        Closed += OnClosed;
        Activated += OnActivated;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_started || args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        _started = true;
        StartPresenter();
    }

    private void StartPresenter()
    {
        var dataDir = CatalogStore.DataDir();
        _presenter = new SmoothDiligentPresenter(ScpSmooth, dataDir);
        var (dipW, dipH, sx, sy) = PanelMetrics();
        if (!_presenter.TryStart(dipW, dipH, sx, sy))
        {
            TxtSmoothStatus.Text = _presenter.Status;
            return;
        }

        TxtSmoothStatus.Text = _presenter.Status;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) =>
        {
            if (_presenter is null)
            {
                return;
            }

            if (_presenter.Lifecycle.NeedsRecreate)
            {
                var m = PanelMetrics();
                _ = _presenter.TryRecreateAfterDeviceLost(m.DipW, m.DipH, m.Sx, m.Sy);
            }
            else
            {
                _ = _presenter.Tick();
            }

            TxtSmoothStatus.Text = _presenter.Status;
        };
        _timer.Start();
    }

    private void ScpSmooth_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_presenter is null)
        {
            return;
        }

        var (dipW, dipH, sx, sy) = PanelMetrics();
        _presenter.Resize(dipW, dipH, sx, sy);
        TxtSmoothStatus.Text = _presenter.Status;
    }

    private void ScpSmooth_CompositionScaleChanged(SwapChainPanel sender, object args)
    {
        if (_presenter is null)
        {
            return;
        }

        var (dipW, dipH, sx, sy) = PanelMetrics();
        _presenter.Resize(dipW, dipH, sx, sy);
        TxtSmoothStatus.Text = _presenter.Status;
    }

    private (double DipW, double DipH, float Sx, float Sy) PanelMetrics()
    {
        var dipW = ScpSmooth.ActualWidth;
        var dipH = ScpSmooth.ActualHeight;
        if (dipW < 2)
        {
            dipW = 640;
        }

        if (dipH < 2)
        {
            dipH = 480;
        }

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
    }
}
