using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class VideoBrowseSwipeTests
{
    [Fact]
    public void TryBegin_IgnoresMouseAndSecondFinger()
    {
        var swipe = new VideoBrowseSwipe();
        Assert.False(swipe.TryBegin(
            isTouch: false, pointerId: 1, x: 10, y: 10, timestampMs: 0,
            swipeAllowed: true, transportChromeVisible: false, yFromBottom: 200));
        Assert.True(swipe.TryBegin(
            isTouch: true, pointerId: 1, x: 10, y: 10, timestampMs: 0,
            swipeAllowed: true, transportChromeVisible: false, yFromBottom: 200));
        Assert.True(swipe.IsTracking);
        // Second finger must not overwrite the first swipe.
        Assert.False(swipe.TryBegin(
            isTouch: true, pointerId: 2, x: 40, y: 10, timestampMs: 10,
            swipeAllowed: true, transportChromeVisible: false, yFromBottom: 200));
        Assert.Equal(1u, swipe.PointerId);
    }

    [Fact]
    public void TryBegin_RespectsDeadZoneOnlyWhenChromeVisible()
    {
        var swipe = new VideoBrowseSwipe();
        Assert.False(swipe.TryBegin(
            isTouch: true, pointerId: 1, x: 10, y: 10, timestampMs: 0,
            swipeAllowed: true, transportChromeVisible: true, yFromBottom: 40));
        Assert.True(swipe.TryBegin(
            isTouch: true, pointerId: 1, x: 10, y: 10, timestampMs: 0,
            swipeAllowed: true, transportChromeVisible: false, yFromBottom: 40));
        swipe.Cancel();
        Assert.True(TouchGestures.BlocksGestureOverVideoControls(true, 40));
        Assert.False(TouchGestures.BlocksGestureOverVideoControls(false, 40));
    }

    [Fact]
    public void TryComplete_NavigatesBrowseAfterSlop()
    {
        var swipe = new VideoBrowseSwipe();
        Assert.True(swipe.TryBegin(
            isTouch: true, pointerId: 7, x: 200, y: 80, timestampMs: 1000,
            swipeAllowed: true, transportChromeVisible: false, yFromBottom: 300));
        Assert.True(swipe.TryMove(7, 40, 82, out var crossed));
        Assert.True(crossed);
        Assert.True(swipe.IsArmed);
        Assert.True(swipe.TryComplete(
            7, 20, 84, 1100, viewportWidth: 400, viewportHeight: 300, reviewMode: false,
            out var action, out var committed));
        Assert.True(committed);
        // Finger left (negative dx) → next image.
        Assert.Equal(TouchGestures.SwipeAction.BrowseNext, action);
        Assert.False(swipe.IsTracking);
    }

    [Fact]
    public void TryComplete_ReviewMode_UsesClassifyReviewSwipe()
    {
        var swipe = new VideoBrowseSwipe();
        Assert.True(swipe.TryBegin(
            isTouch: true, pointerId: 3, x: 200, y: 150, timestampMs: 1000,
            swipeAllowed: true, transportChromeVisible: false, yFromBottom: 300));
        // Left swipe past 30% height → Reject.
        Assert.True(swipe.TryMove(3, 40, 150, out _));
        Assert.True(swipe.TryComplete(
            3, 20, 150, 1100, viewportWidth: 400, viewportHeight: 200, reviewMode: true,
            out var reject, out var committedReject));
        Assert.True(committedReject);
        Assert.Equal(TouchGestures.SwipeAction.Reject, reject);

        Assert.True(swipe.TryBegin(
            isTouch: true, pointerId: 4, x: 50, y: 150, timestampMs: 2000,
            swipeAllowed: true, transportChromeVisible: false, yFromBottom: 300));
        // Right swipe → Keep.
        Assert.True(swipe.TryMove(4, 200, 150, out _));
        Assert.True(swipe.TryComplete(
            4, 220, 150, 2100, viewportWidth: 400, viewportHeight: 200, reviewMode: true,
            out var keep, out var committedKeep));
        Assert.True(committedKeep);
        Assert.Equal(TouchGestures.SwipeAction.Keep, keep);

        Assert.True(swipe.TryBegin(
            isTouch: true, pointerId: 5, x: 100, y: 180, timestampMs: 3000,
            swipeAllowed: true, transportChromeVisible: false, yFromBottom: 300));
        // Up swipe → Skip.
        Assert.True(swipe.TryMove(5, 100, 40, out _));
        Assert.True(swipe.TryComplete(
            5, 100, 20, 3100, viewportWidth: 400, viewportHeight: 200, reviewMode: true,
            out var skip, out var committedSkip));
        Assert.True(committedSkip);
        Assert.Equal(TouchGestures.SwipeAction.Skip, skip);
    }

    [Fact]
    public void TryMove_IgnoresForeignPointer()
    {
        var swipe = new VideoBrowseSwipe();
        Assert.True(swipe.TryBegin(
            isTouch: true, pointerId: 3, x: 0, y: 0, timestampMs: 0,
            swipeAllowed: true, transportChromeVisible: false, yFromBottom: 200));
        Assert.False(swipe.TryMove(9, 100, 0, out _));
        Assert.False(swipe.TryComplete(
            9, 100, 0, 50, 400, 300, reviewMode: false, out _, out var committed));
        Assert.False(committed);
        Assert.True(swipe.IsTracking);
    }
}
