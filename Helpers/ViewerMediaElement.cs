using System.Runtime.CompilerServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Palace.Helpers;

/// <summary>
/// WinUI hooks that keep MediaPlayerElement / AppWindow from maximizing or
/// entering full-window when the viewer opens video.
/// </summary>
public static class ViewerMediaElement
{
    private static readonly ConditionalWeakTable<MediaPlayerElement, object> Hooked = new();

    /// <summary>
    /// Hide the transport full-window button and reject IsFullWindow flips.
    /// Safe to call repeatedly.
    /// </summary>
    public static void SuppressFullWindow(MediaPlayerElement? element)
    {
        if (element is null)
        {
            return;
        }

        if (element.TransportControls is MediaTransportControls transport)
        {
            transport.IsFullWindowButtonVisible = false;
        }

        if (element.IsFullWindow)
        {
            element.IsFullWindow = false;
        }

        if (Hooked.TryGetValue(element, out _))
        {
            return;
        }

        Hooked.Add(element, new object());
        element.RegisterPropertyChangedCallback(
            MediaPlayerElement.IsFullWindowProperty,
            OnIsFullWindowChanged);
    }

    private static void OnIsFullWindowChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (sender is MediaPlayerElement { IsFullWindow: true } element
            && !ViewerMediaChrome.AllowsMediaFullWindow)
        {
            element.IsFullWindow = false;
        }
    }

    public static bool IsPresenterMaximized(AppWindow? appWindow) =>
        appWindow?.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };

    public static void RestoreIfVideoMaximized(AppWindow? appWindow, bool wasMaximizedBefore)
    {
        if (appWindow?.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        var nowMaximized = presenter.State == OverlappedPresenterState.Maximized;
        if (ViewerMediaChrome.ShouldRestorePresenter(wasMaximizedBefore, nowMaximized))
        {
            presenter.Restore();
        }
    }
}
