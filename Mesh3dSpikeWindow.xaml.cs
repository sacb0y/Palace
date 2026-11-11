using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;
using Palace.Services;

namespace Palace;

/// <summary>
/// Dev-only Diligent viewer host. Opened from Settings → About when
/// <see cref="Mesh3dSpike.IsEnabled"/>; never part of the product nav.
/// </summary>
public sealed partial class Mesh3dSpikeWindow : Window
{
    private static Mesh3dSpikeWindow? _open;
    private Mesh3dDiligentPresenter? _presenter;
    private readonly DispatcherTimer _tick;

    public Mesh3dSpikeWindow()
    {
        InitializeComponent();
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _tick.Tick += (_, _) =>
        {
            _presenter?.Tick();
            RefreshStatus();
        };
        Closed += (_, _) =>
        {
            _tick.Stop();
            _presenter?.Dispose();
            _presenter = null;
            if (ReferenceEquals(_open, this))
            {
                _open = null;
            }
        };
        Activated += (_, _) => EnsureStarted();
    }

    public static void Show()
    {
        if (!Mesh3dSpike.ShowsDevSurface)
        {
            return;
        }

        if (_open is { } existing)
        {
            existing.Activate();
            return;
        }

        _open = new Mesh3dSpikeWindow();
        _open.Activate();
    }

    private void EnsureStarted()
    {
        if (_presenter is not null)
        {
            return;
        }

        _presenter = new Mesh3dDiligentPresenter(ScpMesh3d);
        var scale = (float)(Content?.XamlRoot?.RasterizationScale ?? 1.0);
        var w = ScpMesh3d.ActualWidth > 2 ? ScpMesh3d.ActualWidth : 960;
        var h = ScpMesh3d.ActualHeight > 2 ? ScpMesh3d.ActualHeight : 540;
        _ = _presenter.TryStart(null, w, h, scale);
        RefreshStatus();
        if (Mesh3dNative.IsAvailable)
        {
            _tick.Start();
        }
    }

    private void ScpMesh3d_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_presenter is null || e.NewSize.Width < 2 || e.NewSize.Height < 2)
        {
            return;
        }

        var scale = (float)(Content?.XamlRoot?.RasterizationScale ?? 1.0);
        _presenter.Resize(e.NewSize.Width, e.NewSize.Height, scale);
        RefreshStatus();
    }

    private void ScpMesh3d_CompositionScaleChanged(SwapChainPanel sender, object args)
    {
        if (_presenter is null)
        {
            return;
        }

        var scale = (float)(Content?.XamlRoot?.RasterizationScale ?? 1.0);
        var w = sender.ActualWidth > 2 ? sender.ActualWidth : 960;
        var h = sender.ActualHeight > 2 ? sender.ActualHeight : 540;
        _presenter.Resize(w, h, scale);
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (_presenter is null)
        {
            TxtMesh3dStatus.Text = "Presenter not started.";
            TxtMesh3dProbe.Text = "";
            return;
        }

        TxtMesh3dStatus.Text =
            $"Flag on · arch {Mesh3dSpike.ArchitectureCaption} · {_presenter.Status}";
        TxtMesh3dProbe.Text = _presenter.LastProbe.StatusLine;
    }
}
