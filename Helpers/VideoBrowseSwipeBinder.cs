using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Palace.Helpers;

/// <summary>
/// WinUI hookup for paused-video browse swipe on <see cref="MediaPlayerElement"/>.
/// Touch only via <see cref="VideoBrowseSwipe"/>; mouse/pen reach transport and
/// ContextFlyout. Not linked into Palace.Tests.
/// </summary>
public sealed class VideoBrowseSwipeBinder
{
    private readonly MediaPlayerElement _mpe;
    private readonly Func<bool> _swipeAllowed;
    private readonly Func<bool> _isReviewMode;
    private readonly Action<TouchGestures.SwipeAction> _onNavigate;
    private readonly VideoBrowseSwipe _swipe = new();
    private bool _hooked;
    private bool _captured;

    public VideoBrowseSwipeBinder(
        MediaPlayerElement mpe,
        Func<bool> swipeAllowed,
        Action<TouchGestures.SwipeAction> onNavigate,
        Func<bool>? isReviewMode = null)
    {
        _mpe = mpe;
        _swipeAllowed = swipeAllowed;
        _onNavigate = onNavigate;
        _isReviewMode = isReviewMode ?? (() => false);
    }

    public void Hook()
    {
        if (_hooked)
        {
            return;
        }

        // handledEventsToo: MPE swallows touch; no overlay catcher so mouse/pen
        // still hit-test the element (transport + ContextFlyout).
        _mpe.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(OnPointerPressed),
            handledEventsToo: true);
        _mpe.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(OnPointerMoved),
            handledEventsToo: true);
        _mpe.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnPointerReleased),
            handledEventsToo: true);
        _mpe.AddHandler(
            UIElement.PointerCanceledEvent,
            new PointerEventHandler(OnPointerCanceled),
            handledEventsToo: true);
        _mpe.AddHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(OnPointerCaptureLost),
            handledEventsToo: true);
        _hooked = true;
    }

    /// <summary>Cancel an in-flight swipe when playback starts / swipe disallowed.</summary>
    public void SyncAllowed()
    {
        if (!_swipeAllowed())
        {
            _swipe.Cancel();
            _captured = false;
        }
    }

    /// <summary>
    /// MediaTransportControls stays Visibility=Visible; Show/Hide fades the
    /// template's ControlPanelGrid. Fall back to the control Opacity.
    /// </summary>
    public static bool IsTransportChromeVisible(MediaTransportControls? transport)
    {
        if (transport is null)
        {
            return false;
        }

        if (FindDescendantByName(transport, "ControlPanelGrid") is FrameworkElement panel)
        {
            return panel.Visibility == Visibility.Visible && panel.Opacity > 0.05;
        }

        return transport.Visibility == Visibility.Visible && transport.Opacity > 0.05;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var device = e.Pointer.PointerDeviceType;
        var point = e.GetCurrentPoint(_mpe);
        var yFromBottom = _mpe.ActualHeight - point.Position.Y;
        _swipe.TryBegin(
            isTouch: device == PointerDeviceType.Touch,
            pointerId: e.Pointer.PointerId,
            x: point.Position.X,
            y: point.Position.Y,
            timestampMs: Environment.TickCount64,
            swipeAllowed: _swipeAllowed(),
            transportChromeVisible: IsTransportChromeVisible(_mpe.TransportControls),
            yFromBottom: yFromBottom);

        // Stock MPE shows transport on mouse/pen click. Our AddHandler can steal
        // that path — restore Show for mouse/pen without arming touch swipe.
        if (device is PointerDeviceType.Mouse or PointerDeviceType.Pen)
        {
            _mpe.TransportControls?.Show();
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_mpe);
        if (!_swipe.TryMove(e.Pointer.PointerId, point.Position.X, point.Position.Y, out var crossedSlop))
        {
            return;
        }

        if (crossedSlop && !_captured)
        {
            _captured = _mpe.CapturePointer(e.Pointer);
        }

        if (_swipe.IsArmed)
        {
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var device = e.Pointer.PointerDeviceType;
        var point = e.GetCurrentPoint(_mpe);
        var hadCapture = _captured;
        var completed = _swipe.TryComplete(
            e.Pointer.PointerId,
            point.Position.X,
            point.Position.Y,
            Environment.TickCount64,
            _mpe.ActualWidth,
            _mpe.ActualHeight,
            reviewMode: _isReviewMode(),
            out var action,
            out var committed);
        EndCapture(e.Pointer);
        if (completed && hadCapture && committed && _swipeAllowed())
        {
            _onNavigate(action);
            e.Handled = true;
            return;
        }

        // Touch tap (no swipe capture): same as stock — show transport.
        // Mouse/pen already Show()'d on press; touch browse swipe must not.
        if (device == PointerDeviceType.Touch && !hadCapture)
        {
            _mpe.TransportControls?.Show();
        }
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _swipe.Cancel(e.Pointer.PointerId);
        EndCapture(e.Pointer);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _swipe.PointerId)
        {
            return;
        }

        _swipe.Cancel(e.Pointer.PointerId);
        _captured = false;
    }

    private void EndCapture(Pointer? pointer)
    {
        var captured = _captured;
        _captured = false;
        if (!captured || pointer is null)
        {
            return;
        }

        try
        {
            _mpe.ReleasePointerCapture(pointer);
        }
        catch
        {
            // Capture may already be released.
        }
    }

    private static FrameworkElement? FindDescendantByName(DependencyObject root, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name)
            {
                return fe;
            }

            if (FindDescendantByName(child, name) is { } match)
            {
                return match;
            }
        }

        return null;
    }
}
