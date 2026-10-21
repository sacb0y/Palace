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
        Assert.Equal(0, TouchGestures.VideoBrowseSwipeBottomInset(transportChromeVisible: false));
        Assert.Equal(
            TouchGestures.VideoControlsDeadZoneDip,
            TouchGestures.VideoBrowseSwipeBottomInset(transportChromeVisible: true));
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
            7, 20, 84, 1100, viewportWidth: 400,
            out var action, out var navigated));
        Assert.True(navigated);
        // Finger left (negative dx) → next image.
        Assert.Equal(TouchGestures.SwipeAction.BrowseNext, action);
        Assert.False(swipe.IsTracking);
    }

    [Fact]
    public void TryMove_IgnoresForeignPointer()
    {
        var swipe = new VideoBrowseSwipe();
        Assert.True(swipe.TryBegin(
            isTouch: true, pointerId: 3, x: 0, y: 0, timestampMs: 0,
            swipeAllowed: true, transportChromeVisible: false, yFromBottom: 200));
        Assert.False(swipe.TryMove(9, 100, 0, out _));
        Assert.False(swipe.TryComplete(9, 100, 0, 50, 400, out _, out var navigated));
        Assert.False(navigated);
        Assert.True(swipe.IsTracking);
    }
}
