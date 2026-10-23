using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Media.Playback;

namespace Palace.Helpers;

/// <summary>
/// WinUI hookup for video browse swipe / playing scrub on
/// <see cref="MediaPlayerElement"/>. Touch only via <see cref="VideoBrowseSwipe"/>.
/// Visual pager for video is deferred (docs/specs/swipe-nav.md); paging stays on
/// this #128 path. Scrub: ProgressSlider when chrome is up, else 28 DIP strip;
/// claim only after slop. Not linked into Palace.Tests.
/// </summary>
public sealed class VideoBrowseSwipeBinder
{
    private const long ScrubThrottleMs = 33; // ~30 Hz

    private readonly MediaPlayerElement _mpe;
    private readonly Func<bool> _videoGesturesAllowed;
    private readonly Func<bool> _isReviewMode;
    private readonly Action<TouchGestures.SwipeAction> _onNavigate;
    private readonly Action? _onLeavePlaying;
    private readonly VideoBrowseSwipe _swipe = new();
    private bool _hooked;
    private bool _captured;
    private SwipeNav.DragKind _kind = SwipeNav.DragKind.None;
    private bool _scrubCandidate;
    private bool _scrubbing;
    private double _scrubStartX;
    private double _scrubStartY;
    private TimeSpan _scrubStartPosition;
    private long _lastScrubSeekMs;
    private FrameworkElement? _progressSlider;

    public VideoBrowseSwipeBinder(
        MediaPlayerElement mpe,
        Func<bool> swipeAllowed,
        Action<TouchGestures.SwipeAction> onNavigate,
        Func<bool>? isReviewMode = null,
        Action? onLeavePlaying = null)
    {
        _mpe = mpe;
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

    public void SyncAllowed()
    {
        if (!_videoGesturesAllowed())
        {
            CancelAll();
        }
    }

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
        var chrome = IsTransportChromeVisible(_mpe.TransportControls);
        var overSlider = chrome && IsOverProgressSlider(point.Position);
        var playing = IsPlaying();
        _kind = SwipeNav.ClassifyVideoDrag(
            isVideo: true,
            isPlaying: playing,
            touchGesturesEnabled: _videoGesturesAllowed(),
            yFromBottom: yFromBottom,
            transportChromeVisible: chrome,
            overProgressSlider: overSlider);

        _scrubCandidate = false;
        _scrubbing = false;

        if (_kind == SwipeNav.DragKind.VideoScrub && device == PointerDeviceType.Touch)
        {
            // B5: arm scrub, but claim only after slop — leave transport hittable.
            _scrubCandidate = true;
            _scrubStartX = point.Position.X;
            _scrubStartY = point.Position.Y;
            _scrubStartPosition = _mpe.MediaPlayer?.PlaybackSession?.Position ?? TimeSpan.Zero;
            return;
        }

        _swipe.TryBegin(
            isTouch: device == PointerDeviceType.Touch,
            pointerId: e.Pointer.PointerId,
            x: point.Position.X,
            y: point.Position.Y,
            timestampMs: Environment.TickCount64,
            swipeAllowed: _kind == SwipeNav.DragKind.BrowsePage,
            transportChromeVisible: chrome,
            yFromBottom: yFromBottom);

        if (device is PointerDeviceType.Mouse or PointerDeviceType.Pen)
        {
            _mpe.TransportControls?.Show();
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_mpe);
        if (_scrubCandidate || _scrubbing)
        {
            var dx = point.Position.X - _scrubStartX;
            var dy = point.Position.Y - _scrubStartY;
            if (!_scrubbing)
            {
                if (!TouchGestures.ExceedsSlop(dx, dy))
                {
                    return;
                }

                _scrubbing = true;
                _scrubCandidate = false;
                _captured = _mpe.CapturePointer(e.Pointer);
            }

            ApplyScrubThrottled(point.Position.X);
            e.Handled = true;
            return;
        }

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
        if (_scrubbing || _scrubCandidate)
        {
            if (_scrubbing)
            {
                ApplyScrubThrottled(e.GetCurrentPoint(_mpe).Position.X, force: true);
                e.Handled = true;
            }

            EndCapture(e.Pointer);
            _scrubbing = false;
            _scrubCandidate = false;
            _kind = SwipeNav.DragKind.None;
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

        if (device == PointerDeviceType.Touch && !hadCapture)
        {
            _mpe.TransportControls?.Show();
        }
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_scrubbing || _scrubCandidate)
        {
            EndCapture(e.Pointer);
            _scrubbing = false;
            _scrubCandidate = false;
            _kind = SwipeNav.DragKind.None;
            return;
        }

        _swipe.Cancel(e.Pointer.PointerId);
        EndCapture(e.Pointer);
        _kind = SwipeNav.DragKind.None;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_scrubbing || _scrubCandidate)
        {
            _scrubbing = false;
            _scrubCandidate = false;
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

    private void ApplyScrubThrottled(double x, bool force = false)
    {
        var now = Environment.TickCount64;
        if (!force && now - _lastScrubSeekMs < ScrubThrottleMs)
        {
            return;
        }

        _lastScrubSeekMs = now;
        ApplyScrub(x);
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

    private bool IsOverProgressSlider(Point position)
    {
        _progressSlider ??= FindDescendantByName(_mpe.TransportControls, "ProgressSlider") as FrameworkElement
            ?? FindDescendantByName(_mpe, "ProgressSlider") as FrameworkElement;
        if (_progressSlider is null
            || _progressSlider.Visibility != Visibility.Visible
            || _progressSlider.ActualWidth <= 0
            || _progressSlider.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            var transform = _progressSlider.TransformToVisual(_mpe);
            var topLeft = transform.TransformPoint(new Point(0, 0));
            var rect = new Rect(topLeft.X, topLeft.Y, _progressSlider.ActualWidth, _progressSlider.ActualHeight);
            // Small vertical pad so the thin slider is hittable.
            rect.Y -= 8;
            rect.Height += 16;
            return rect.Contains(position);
        }
        catch
        {
            return false;
        }
    }

    private bool IsPlaying() =>
        _mpe.MediaPlayer?.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;

    private void CancelAll()
    {
        _swipe.Cancel();
        _scrubbing = false;
        _scrubCandidate = false;
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

    private static FrameworkElement? FindDescendantByName(DependencyObject? root, string name)
    {
        if (root is null)
        {
            return null;
        }

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
