using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class RangeSelectTests
{
    [Fact]
    public void AllowsGesture_SelectModeOnly_NotOverlay_NotHeader()
    {
        Assert.True(RangeSelect.AllowsGesture(true, false, false));
        Assert.False(RangeSelect.AllowsGesture(false, false, false));
        Assert.False(RangeSelect.AllowsGesture(true, true, false));
        Assert.False(RangeSelect.AllowsGesture(true, false, true));
    }

    [Fact]
    public void MouseAndPen_BeginOnPress_TouchRequiresHold()
    {
        Assert.True(RangeSelect.BeginsOnPress(RangeSelectPointer.Mouse));
        Assert.True(RangeSelect.BeginsOnPress(RangeSelectPointer.Pen));
        Assert.False(RangeSelect.BeginsOnPress(RangeSelectPointer.Touch));
        Assert.True(RangeSelect.RequiresHold(RangeSelectPointer.Touch));
        Assert.False(RangeSelect.RequiresHold(RangeSelectPointer.Mouse));
    }

    [Fact]
    public void ContiguousIndexes_SkipsHeaders_InclusiveSpan()
    {
        bool[] headers = [true, false, false, true, false, false];
        Assert.Equal([1, 2, 4], RangeSelect.ContiguousIndexes(headers, 1, 4));
        Assert.Equal([1, 2, 4], RangeSelect.ContiguousIndexes(headers, 4, 1));
        Assert.Equal([4, 5], RangeSelect.ContiguousIndexes(headers, 3, 5));
        Assert.Empty(RangeSelect.ContiguousIndexes(headers, 0, 0));
        Assert.Empty(RangeSelect.ContiguousIndexes(headers, -1, 2));
        Assert.Empty(RangeSelect.ContiguousIndexes([], 0, 0));
    }

    [Fact]
    public void Touch_MovementBeforeHold_Cancels_NotActivate()
    {
        Assert.True(RangeSelect.CancelsHold(RangeSelectPointer.Touch, 100, 20, 0));
        Assert.False(RangeSelect.ActivatesDrag(RangeSelectPointer.Touch, 100, 20, 0));
        Assert.False(RangeSelect.CancelsHold(RangeSelectPointer.Touch, RangeSelect.TouchHoldMs, 20, 0));
        Assert.True(RangeSelect.ActivatesDrag(RangeSelectPointer.Touch, RangeSelect.TouchHoldMs, 20, 0));
        Assert.False(RangeSelect.CancelsHold(RangeSelectPointer.Mouse, 50, 20, 0));
    }

    [Fact]
    public void Mouse_DragPastSlop_Activates_ClickDoesNot()
    {
        Assert.False(RangeSelect.ActivatesDrag(RangeSelectPointer.Mouse, 10, 2, 2));
        Assert.True(RangeSelect.ActivatesDrag(RangeSelectPointer.Mouse, 10, RangeSelect.MoveSlop, 0));
        Assert.False(RangeSelect.ActivatesDrag(RangeSelectPointer.Other, 10, 20, 0));
    }

    [Fact]
    public void Session_MouseDrag_HighlightsThroughLastItem()
    {
        var session = new RangeSelectSession(RangeSelectPointer.Mouse, 1);
        bool[] headers = [true, false, false, true, false];
        Assert.False(session.TryActivate(5, 1, 0));
        Assert.False(session.IsActive);
        Assert.True(session.TryActivate(20, 12, 0));
        Assert.Equal([1, 2, 4], session.Highlight(headers, 4));
        Assert.Equal(4, session.EndIndex);
        Assert.Equal(1, session.AnchorIndex);
    }

    [Fact]
    public void Session_Touch_HoldThenDrag_NotBeforeHold()
    {
        var session = new RangeSelectSession(RangeSelectPointer.Touch, 0);
        bool[] headers = [false, false, false];
        Assert.False(session.TryActivate(80, 16, 0));
        Assert.False(session.TryActivateHold(80));
        Assert.True(session.TryActivateHold(RangeSelect.TouchHoldMs));
        Assert.True(session.IsActive);
        Assert.Equal([0, 1, 2], session.Highlight(headers, 2));
    }

    [Fact]
    public void Session_Touch_DragAfterHoldWithoutHoldingEvent()
    {
        var session = new RangeSelectSession(RangeSelectPointer.Touch, 0);
        Assert.True(session.TryActivate(RangeSelect.TouchHoldMs, 10, 0));
        Assert.Equal([0, 1], session.Highlight([false, false], 1));
    }

    [Fact]
    public void OverlayOpen_DoesNotStealPan()
    {
        Assert.False(RangeSelect.AllowsGesture(selectMode: true, overlayOpen: true, isFolderHeader: false));
        var session = new RangeSelectSession(RangeSelectPointer.Mouse, 0);
        Assert.True(session.TryActivate(1, 20, 0));
    }
}
