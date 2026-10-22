using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Playback;

namespace Palace.Helpers;

/// <summary>
/// WinUI hookup for video browse swipe / playing scrub on
/// <see cref="MediaPlayerElement"/>. Touch only via <see cref="VideoBrowseSwipe"/>.
/// Playing: 28 DIP scrub bar scrubs; elsewhere pages (docs/specs/swipe-nav.md).
/// Not linked into Palace.Tests.
/// </summary>
public sealed class VideoBrowseSwipeBinder
{
    private readonly MediaPlayerElement _mpe;
    private readonly Func<bool> _videoGesturesAllowed;
    private readonly Func<bool> _isReviewMode;
    private readonly Action<TouchGestures.SwipeAction> _onNavigate;
    private readonly Action? _onLeavePlaying;
    private readonly VideoBrowseSwipe _swipe = new();
    private bool _hooked;
    private bool _captured;
    private SwipeNav.DragKind _kind = SwipeNav.DragKind.None;
    private bool _scrubbing;
    private double _scrubStartX;
    private TimeSpan _scrubStartPosition;

    public VideoBrowseSwipeBinder(
        MediaPlayerElement mpe,
        Func<bool> swipeAllowed,
        Action<TouchGestures.SwipeAction> onNavigate,
        Func<bool>? isReviewMode = null,
        Action? onLeavePlaying = null)
    {
        _mpe = mpe;
        // swipeAllowed kept as "video + touch gestures on" (playing OK).
        _videoGesturesAllowed = swipeAllowed;
        _onNavigate = onNavigate;
        _isReviewMode = isReviewMode ?? (() => false);
        _onLeavePlaying = onLeavePlaying;
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

    /// <summary>Cancel an in-flight gesture when video / touch gestures turn off.</summary>
    public void SyncAllowed()
    {
        if (!_videoGesturesAllowed())
        {
            CancelAll();
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
        var playing = IsPlaying();
        _kind = SwipeNav.ClassifyVideoDrag(
            isVideo: true,
            isPlaying: playing,
            touchGesturesEnabled: _videoGesturesAllowed(),
            yFromBottom: yFromBottom,
            transportChromeVisible: IsTransportChromeVisible(_mpe.TransportControls));

        if (_kind == SwipeNav.DragKind.VideoScrub && device == PointerDeviceType.Touch)
        {
            _scrubbing = true;
            _scrubStartX = point.Position.X;
            _scrubStartPosition = _mpe.MediaPlayer?.PlaybackSession?.Position ?? TimeSpan.Zero;
            _captured = _mpe.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        _scrubbing = false;
        _swipe.TryBegin(
            isTouch: device == PointerDeviceType.Touch,
            pointerId: e.Pointer.PointerId,
            x: point.Position.X,
            y: point.Position.Y,
            timestampMs: Environment.TickCount64,
            swipeAllowed: _kind == SwipeNav.DragKind.BrowsePage,
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
        if (_scrubbing)
        {
            ApplyScrub(e.GetCurrentPoint(_mpe).Position.X);
            e.Handled = true;
            return;
        }

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
        if (_scrubbing)
        {
            ApplyScrub(e.GetCurrentPoint(_mpe).Position.X);
            EndCapture(e.Pointer);
            _scrubbing = false;
            _kind = SwipeNav.DragKind.None;
            e.Handled = true;
            return;
        }

        var point = e.GetCurrentPoint(_mpe);
        var hadCapture = _captured;
        var wasPlaying = IsPlaying();
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
        _kind = SwipeNav.DragKind.None;
        if (completed && hadCapture && committed && _videoGesturesAllowed())
        {
            if (SwipeNav.ShouldPauseVideoOnLeave(isVideo: true, wasPlaying: wasPlaying)
                && action is TouchGestures.SwipeAction.BrowsePrevious
                    or TouchGestures.SwipeAction.BrowseNext)
            {
                _mpe.MediaPlayer?.Pause();
                _onLeavePlaying?.Invoke();
            }

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
        if (_scrubbing)
        {
            EndCapture(e.Pointer);
            _scrubbing = false;
            _kind = SwipeNav.DragKind.None;
            return;
        }

        _swipe.Cancel(e.Pointer.PointerId);
        EndCapture(e.Pointer);
        _kind = SwipeNav.DragKind.None;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_scrubbing)
        {
            _scrubbing = false;
            _captured = false;
            _kind = SwipeNav.DragKind.None;
            return;
        }

        if (e.Pointer.PointerId != _swipe.PointerId)
        {
            return;
        }

        _swipe.Cancel(e.Pointer.PointerId);
        _captured = false;
        _kind = SwipeNav.DragKind.None;
    }

    private void ApplyScrub(double x)
    {
        if (_mpe.MediaPlayer?.PlaybackSession is not { } session)
        {
            return;
        }

        var duration = session.NaturalDuration;
        if (duration <= TimeSpan.Zero || _mpe.ActualWidth <= 1)
        {
            return;
        }

        var dx = x - _scrubStartX;
        var delta = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * (dx / _mpe.ActualWidth));
        var next = _scrubStartPosition + delta;
        if (next < TimeSpan.Zero)
        {
            next = TimeSpan.Zero;
        }
        else if (next > duration)
        {
            next = duration;
        }

        session.Position = next;
    }

    private bool IsPlaying() =>
        _mpe.MediaPlayer?.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;

    private void CancelAll()
    {
        _swipe.Cancel();
        _scrubbing = false;
        _captured = false;
        _kind = SwipeNav.DragKind.None;
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
