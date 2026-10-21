using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TouchGesturesTests
{
    [Fact]
    public void LongPress_ThresholdsMatchSpec()
    {
        Assert.Equal(500, TouchGestures.LongPressMs);
        Assert.Equal(8, TouchGestures.MoveSlopPx);
        Assert.True(TouchGestures.LongPressReady(500));
        Assert.False(TouchGestures.LongPressReady(499));
        Assert.True(TouchGestures.CancelsLongPress(100, 9, 0));
        Assert.False(TouchGestures.CancelsLongPress(100, 3, 4));
        Assert.False(TouchGestures.OpensContextFromTouchHold());
        Assert.True(TouchGestures.MosaicHoldEntersSelect(false, false, false));
        Assert.False(TouchGestures.MosaicHoldEntersSelect(true, false, false));
        Assert.True(TouchGestures.ViewerHoldTogglesSelection(true));
        Assert.False(TouchGestures.ViewerHoldTogglesSelection(false));
    }

    [Fact]
    public void ClampPanAxis_LargerContent_CannotPassEitherEdge()
    {
        // Content 200, viewport 100 → offset 0…100.
        Assert.Equal(0, TouchGestures.ClampScrollOffset(-20, 200, 100), 3);
        Assert.Equal(100, TouchGestures.ClampScrollOffset(140, 200, 100), 3);
        Assert.Equal(40, TouchGestures.ClampScrollOffset(40, 200, 100), 3);
        // Symmetric: same magnitude past each edge clamps equally.
        Assert.Equal(0, TouchGestures.ClampScrollOffset(-50, 200, 100), 3);
        Assert.Equal(100, TouchGestures.ClampScrollOffset(150, 200, 100), 3);
    }

    [Fact]
    public void ClampPanAxis_SmallerContent_StaysCentered()
    {
        // Content shorter than viewport → offset 0 (layout centers).
        Assert.Equal(0, TouchGestures.ClampScrollOffset(30, 50, 100), 3);
        Assert.Equal(0, TouchGestures.ClampScrollOffset(-10, 50, 100), 3);
        // Origin form: centered at (100-50)/2 = 25.
        Assert.Equal(25, TouchGestures.ClampPanAxis(0, 50, 100), 3);
        Assert.Equal(25, TouchGestures.ClampPanAxis(40, 50, 100), 3);
        Assert.Equal(25, TouchGestures.ClampPanAxis(25, 50, 100), 3);
    }

    [Fact]
    public void ClampPan_IndependentAxes_TallImage()
    {
        // Wide viewport, tall content: X centered (content narrower), Y scrollable.
        var (h, v) = TouchGestures.ClampPan(
            offsetX: 50,
            offsetY: -20,
            contentWidth: 80,
            contentHeight: 400,
            viewportWidth: 200,
            viewportHeight: 100);
        Assert.Equal(0, h, 3);
        Assert.Equal(0, v, 3);

        var mid = TouchGestures.ClampPan(0, 150, 80, 400, 200, 100);
        Assert.Equal(0, mid.Horizontal, 3);
        Assert.Equal(150, mid.Vertical, 3);

        var bottom = TouchGestures.ClampPan(0, 500, 80, 400, 200, 100);
        Assert.Equal(0, bottom.Horizontal, 3);
        Assert.Equal(300, bottom.Vertical, 3);
    }

    [Fact]
    public void RubberBand_CapsAtQuarterViewport()
    {
        Assert.Equal(0, TouchGestures.RubberBand(0, 100), 3);
        Assert.Equal(3.5, TouchGestures.RubberBand(10, 100), 3); // 10 * 0.35
        Assert.Equal(25, TouchGestures.RubberBand(1000, 100), 3); // cap 25%
    }

    [Fact]
    public void ReviewSwipe_LeftReject_RightKeep_DownIgnored()
    {
        // Commit via distance (30% of height = 30 on 100).
        Assert.Equal(
            TouchGestures.SwipeAction.Reject,
            TouchGestures.ClassifyReviewSwipe(-40, 0, 200, 100, 0, 0));
        Assert.Equal(
            TouchGestures.SwipeAction.Keep,
            TouchGestures.ClassifyReviewSwipe(40, 0, 200, 100, 0, 0));
        Assert.Equal(
            TouchGestures.SwipeAction.SpringBack,
            TouchGestures.ClassifyReviewSwipe(-10, 0, 200, 100, 0, 0));
        // Down is not reject.
        Assert.Equal(
            TouchGestures.SwipeAction.None,
            TouchGestures.ClassifyReviewSwipe(0, 50, 200, 100, 0, 0));
        // Up = skip when committed.
        Assert.Equal(
            TouchGestures.SwipeAction.Skip,
            TouchGestures.ClassifyReviewSwipe(0, -40, 200, 100, 0, 0));
        // Flick commits.
        Assert.Equal(
            TouchGestures.SwipeAction.Reject,
            TouchGestures.ClassifyReviewSwipe(-5, 0, 200, 100, -0.6, 0));
    }

    [Fact]
    public void DoubleTap_FitThenZoomThenFit_AndVideoThirds()
    {
        Assert.Equal(TouchGestures.DoubleTapZoomStep.Zoomed, TouchGestures.NextDoubleTapStep(true));
        Assert.Equal(TouchGestures.DoubleTapZoomStep.Fit, TouchGestures.NextDoubleTapStep(false));
        // 1:1 only 1.1× Fit → use 2.5× Fit.
        Assert.Equal(2.5, TouchGestures.DoubleTapZoomFitFactor(1.1), 3);
        Assert.Equal(2.5, TouchGestures.DoubleTapZoomFitFactor(1.25), 3);
        Assert.Equal(3.0, TouchGestures.DoubleTapZoomFitFactor(3.0), 3);
        Assert.Equal(
            TouchGestures.VideoDoubleTapZone.LeftSeekBack,
            TouchGestures.ClassifyVideoDoubleTap(10, 300));
        Assert.Equal(
            TouchGestures.VideoDoubleTapZone.CenterZoom,
            TouchGestures.ClassifyVideoDoubleTap(150, 300));
        Assert.Equal(
            TouchGestures.VideoDoubleTapZone.RightSeekForward,
            TouchGestures.ClassifyVideoDoubleTap(290, 300));
        Assert.True(TouchGestures.BlocksGestureOverVideoControls(true, 40));
        Assert.False(TouchGestures.BlocksGestureOverVideoControls(true, 80));
        Assert.False(TouchGestures.BlocksGestureOverVideoControls(false, 10));
    }

    [Fact]
    public void PinchLimits_AndArbitration()
    {
        Assert.Equal(0.5, TouchGestures.ClampPinchDuringGesture(0.3), 3);
        Assert.Equal(1.0, TouchGestures.SnapPinchOnRelease(0.7), 3);
        Assert.Equal(1.2, TouchGestures.SnapPinchOnRelease(1.2), 3);
        // Spec would allow max(8*2, 4)=16, but layout zoom caps at MaxPinchZoom (8).
        Assert.Equal(8, TouchGestures.MaxPinchFitFactor(2), 3);
        Assert.Equal(4, TouchGestures.MaxPinchFitFactor(0.25), 3);
        Assert.Equal(8, TouchGestures.ClampPinchHard(20, 2), 3);
        Assert.Equal(8 * 1.15, TouchGestures.ClampPinchSoft(30, 2), 3);
        // 4K Fit base × 8 would exceed 8192 DIPs → panel-size cap.
        Assert.Equal(2.0, TouchGestures.MaxPinchForBaseSize(4096), 3);
        Assert.Equal(2.0, TouchGestures.ClampPinchHard(8, 2, baseLongEdgeDip: 4096), 3);
        var (cw, ch) = TouchGestures.CapStillPanelSize(16000, 9000);
        Assert.True(Math.Max(cw, ch) <= TouchGestures.MaxStillPanelLongEdgeDip + 0.001);
        Assert.False(TouchGestures.AllowsSwipe(zoomed: true, fingerCount: 1, afterPinchAwaitingLift: false));
        Assert.False(TouchGestures.AllowsSwipe(zoomed: false, fingerCount: 2, afterPinchAwaitingLift: false));
        Assert.True(TouchGestures.AllowsSwipe(zoomed: false, fingerCount: 1, afterPinchAwaitingLift: false));
        Assert.True(TouchGestures.AllowsSwipeHandoff(true, true, 25));
        Assert.False(TouchGestures.AllowsSwipeHandoff(true, true, 10));
        Assert.Equal("_rejected", TouchGestures.RejectedFolderName);
        Assert.EndsWith("_rejected", TouchGestures.RejectedFolderPath("/photos"));
    }

    [Fact]
    public void BrowseSwipe_NeverRejects_ReviewRequiresMode()
    {
        Assert.False(TouchGestures.AllowsReviewFileActions(reviewMode: false));
        Assert.True(TouchGestures.AllowsReviewFileActions(reviewMode: true));

        // Normal viewer: left/right = next/prev — never keep/reject/skip.
        Assert.Equal(
            TouchGestures.SwipeAction.BrowseNext,
            TouchGestures.ClassifySwipe(false, -80, 0, 200, 100, 0, 0));
        Assert.Equal(
            TouchGestures.SwipeAction.BrowsePrevious,
            TouchGestures.ClassifySwipe(false, 80, 0, 200, 100, 0, 0));
        Assert.Equal(
            TouchGestures.SwipeAction.None,
            TouchGestures.ClassifySwipe(false, 0, -80, 200, 100, 0, 0));

        // Review mode: left=reject, right=keep, up=skip.
        Assert.Equal(
            TouchGestures.SwipeAction.Reject,
            TouchGestures.ClassifySwipe(true, -40, 0, 200, 100, 0, 0));
        Assert.Equal(
            TouchGestures.SwipeAction.Keep,
            TouchGestures.ClassifySwipe(true, 40, 0, 200, 100, 0, 0));
        Assert.Equal(
            TouchGestures.SwipeAction.Skip,
            TouchGestures.ClassifySwipe(true, 0, -40, 200, 100, 0, 0));

        Assert.Equal(
            TouchGestures.SwipeAction.BrowseNext,
            TouchGestures.ClassifyBrowseSwipe(-80, 0, 200, 0));
    }

    [Fact]
    public void ZoomAroundPoint_KeepsTapUnderFinger()
    {
        var (h, v) = TouchGestures.ZoomAroundPoint(
            tapX: 50,
            tapY: 50,
            oldContentWidth: 100,
            oldContentHeight: 100,
            oldOffsetX: 0,
            oldOffsetY: 0,
            newContentWidth: 200,
            newContentHeight: 200,
            viewportWidth: 100,
            viewportHeight: 100);
        // Content point (50,50) → (100,100); offset so tap stays: 50,50.
        Assert.Equal(50, h, 3);
        Assert.Equal(50, v, 3);
    }

    [Fact]
    public void ZoomAroundPoint_ClampsSoImageCannotLeaveViewport()
    {
        // Extreme zoom with a corner tap must still keep content covering the
        // viewport — never translate so far that Fit/HDR shows empty black.
        var (h, v) = TouchGestures.ZoomAroundPoint(
            tapX: 10,
            tapY: 10,
            oldContentWidth: 100,
            oldContentHeight: 100,
            oldOffsetX: 0,
            oldOffsetY: 0,
            newContentWidth: 800,
            newContentHeight: 800,
            viewportWidth: 100,
            viewportHeight: 100);
        Assert.InRange(h, 0, 700);
        Assert.InRange(v, 0, 700);
        // Midpoint of a 4K Fit→pinch must stay on-screen (viewport coords).
        var mid = TouchGestures.ZoomAroundPoint(
            tapX: 960,
            tapY: 540,
            oldContentWidth: 1920,
            oldContentHeight: 1080,
            oldOffsetX: 0,
            oldOffsetY: 0,
            newContentWidth: 7680,
            newContentHeight: 4320,
            viewportWidth: 1920,
            viewportHeight: 1080);
        Assert.InRange(mid.Horizontal, 0, 7680 - 1920);
        Assert.InRange(mid.Vertical, 0, 4320 - 1080);
        // Content point under midpoint remains under midpoint.
        Assert.Equal(960 * (7680.0 / 1920.0) - 960, mid.Horizontal, 3);
        Assert.Equal(540 * (4320.0 / 1080.0) - 540, mid.Vertical, 3);
    }

    [Fact]
    public void ZoomAroundPoint_FitSizedContent_ForcesZeroOffset()
    {
        // Zooming back to Fit (content == viewport) must reset translation.
        var (h, v) = TouchGestures.ZoomAroundPoint(
            tapX: 50,
            tapY: 50,
            oldContentWidth: 400,
            oldContentHeight: 400,
            oldOffsetX: 120,
            oldOffsetY: 80,
            newContentWidth: 100,
            newContentHeight: 100,
            viewportWidth: 100,
            viewportHeight: 100);
        Assert.Equal(0, h, 3);
        Assert.Equal(0, v, 3);
    }

    [Fact]
    public void ZoomAroundPoint_CenteredLetterbox_KeepsMidpoint()
    {
        // Tall Fit content centered in a wide viewport (HDR black margins outside panel).
        const double viewW = 400;
        const double viewH = 200;
        const double fitW = 100;
        const double fitH = 200;
        // Content left at x=150; mid content at viewport x=200.
        Assert.Equal(0, TouchGestures.ContentCoordFromViewport(150, 0, fitW, viewW), 3);
        Assert.Equal(50, TouchGestures.ContentCoordFromViewport(200, 0, fitW, viewW), 3);
        var (h, v) = TouchGestures.ZoomAroundPoint(
            tapX: 200,
            tapY: 100,
            oldContentWidth: fitW,
            oldContentHeight: fitH,
            oldOffsetX: 0,
            oldOffsetY: 0,
            newContentWidth: 200,
            newContentHeight: 400,
            viewportWidth: viewW,
            viewportHeight: viewH);
        // New width still < viewport → centered (offset 0). Y scrolls; mid stays.
        Assert.Equal(0, h, 3);
        Assert.Equal(100, v, 3); // contentY 100 × 2 − 100
    }

    [Fact]
    public void IsTouchOnly_GatesGestures()
    {
        Assert.True(TouchGestures.IsTouchOnly(true));
        Assert.False(TouchGestures.IsTouchOnly(false));
        Assert.True(TouchGestures.IsHorizontalAxis(20, 5));
        Assert.True(TouchGestures.IsVerticalAxis(5, 20));
        Assert.False(TouchGestures.IsHorizontalAxis(5, 5));
    }
}
