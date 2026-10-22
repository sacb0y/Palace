namespace Palace.Helpers;

/// <summary>
/// Paused/stopped video browse-swipe session. Touch only — callers attach via
/// <c>AddHandler(..., handledEventsToo: true)</c> on <c>MediaPlayerElement</c>
/// so mouse/pen still reach transport, seek, and ContextFlyout. In Review mode
/// completes via <see cref="TouchGestures.ClassifyReviewSwipe"/> (L=reject,
/// R=keep, up=skip). Off WinUI.
/// </summary>
public sealed class VideoBrowseSwipe
{
    private double _startX;
    private double _startY;
    private long _startMs;
    private uint _pointerId;
    private bool _tracking;
    private bool _armed;

    public bool IsTracking => _tracking;

    public bool IsArmed => _armed;

    public uint PointerId => _pointerId;

    /// <summary>
    /// Begin a touch swipe. Ignores mouse/pen, blocked transport chrome, and a
    /// second finger while already tracking.
    /// </summary>
    public bool TryBegin(
        bool isTouch,
        uint pointerId,
        double x,
        double y,
        long timestampMs,
        bool swipeAllowed,
        bool transportChromeVisible,
        double yFromBottom)
    {
        if (!isTouch || !swipeAllowed)
        {
            return false;
        }

        if (TouchGestures.BlocksGestureOverVideoControls(transportChromeVisible, yFromBottom))
        {
            return false;
        }

        // Second finger: keep the first swipe; do not overwrite.
        if (_tracking)
        {
            return false;
        }

        _tracking = true;
        _armed = false;
        _pointerId = pointerId;
        _startX = x;
        _startY = y;
        _startMs = timestampMs;
        return true;
    }

    /// <summary>
    /// Returns true once past slop (caller may CapturePointer). Foreign pointers ignored.
    /// </summary>
    public bool TryMove(uint pointerId, double x, double y, out bool crossedSlop)
    {
        crossedSlop = false;
        if (!_tracking || pointerId != _pointerId)
        {
            return false;
        }

        if (_armed)
        {
            return true;
        }

        var dx = x - _startX;
        var dy = y - _startY;
        if (!TouchGestures.ExceedsSlop(dx, dy))
        {
            return false;
        }

        _armed = true;
        crossedSlop = true;
        return true;
    }

    /// <summary>
    /// Completes the swipe. Browse: prev/next. Review: reject/keep/skip via
    /// <see cref="TouchGestures.ClassifyReviewSwipe"/>.
    /// </summary>
    public bool TryComplete(
        uint pointerId,
        double x,
        double y,
        long timestampMs,
        double viewportWidth,
        double viewportHeight,
        bool reviewMode,
        out TouchGestures.SwipeAction action,
        out bool committed)
    {
        action = TouchGestures.SwipeAction.None;
        committed = false;
        if (!_tracking || pointerId != _pointerId)
        {
            return false;
        }

        var armed = _armed;
        var dx = x - _startX;
        var dy = y - _startY;
        var elapsedMs = Math.Max(1L, timestampMs - _startMs);
        var vx = dx / elapsedMs;
        var vy = dy / elapsedMs;
        Reset();
        if (!armed)
        {
            return true;
        }

        if (reviewMode)
        {
            action = TouchGestures.ClassifyReviewSwipe(
                dx, dy, viewportWidth, viewportHeight, vx, vy);
            committed = action is TouchGestures.SwipeAction.Reject
                or TouchGestures.SwipeAction.Keep
                or TouchGestures.SwipeAction.Skip;
            return true;
        }

        action = TouchGestures.ClassifyBrowseSwipe(dx, dy, viewportWidth, vx);
        committed = action is TouchGestures.SwipeAction.BrowsePrevious
            or TouchGestures.SwipeAction.BrowseNext;
        return true;
    }

    public void Cancel(uint? pointerId = null)
    {
        if (pointerId is { } id && _tracking && id != _pointerId)
        {
            return;
        }

        Reset();
    }

    private void Reset()
    {
        _tracking = false;
        _armed = false;
        _pointerId = 0;
        _startX = 0;
        _startY = 0;
        _startMs = 0;
    }
}
