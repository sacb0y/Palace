using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SwipeNavTests
{
    [Fact]
    public void Thresholds_MatchApprovedSpec()
    {
        Assert.Equal(16, SwipeNav.NeighborGapPx);
        Assert.Equal(0.30, SwipeNav.CommitFraction);
        Assert.Equal(0.5, SwipeNav.FlickPxPerMs);
        Assert.Equal(220, SwipeNav.CommitMs);
        Assert.Equal(280, SwipeNav.SnapBackMs);
        Assert.Equal(4, SwipeNav.MaxOvershootPx);
        Assert.Equal(0.3, SwipeNav.EdgeResistanceGain);
        Assert.Equal(28, SwipeNav.VideoScrubHitDip);
        Assert.Equal(0.1, SwipeNav.EaseX1);
        Assert.Equal(0.9, SwipeNav.EaseY1);
        Assert.Equal(0.2, SwipeNav.EaseX2);
        Assert.Equal(1.0, SwipeNav.EaseY2);
        Assert.Equal(SwipeNav.CommitFraction, TouchGestures.SwipeCommitFraction);
        Assert.Equal(SwipeNav.FlickPxPerMs, TouchGestures.SwipeFlickPxPerMs);
        Assert.Equal(216, SwipeNav.PageStride(200), 3); // 200 + 16
    }

    [Fact]
    public void EdgeResistance_FormulaAndHints()
    {
        // offset = d * 0.3 / (1 + |d| / W); W=200, d=100 → 100*0.3/(1+0.5)=20
        Assert.Equal(20, SwipeNav.EdgeResistanceOffset(100, 200), 3);
        Assert.Equal(-20, SwipeNav.EdgeResistanceOffset(-100, 200), 3);
        Assert.Equal(0, SwipeNav.EdgeResistanceOffset(0, 200), 3);

        // Soft: grows slower than linear as |d| increases.
        var soft50 = Math.Abs(SwipeNav.EdgeResistanceOffset(50, 200));
        var soft200 = Math.Abs(SwipeNav.EdgeResistanceOffset(200, 200));
        Assert.True(soft200 < 200 * 0.3); // damped below raw 0.3×
        Assert.True(soft200 / 200 < soft50 / 50); // growing damping

        Assert.Equal(
            SwipeNav.EdgeHint.StartOfFolder,
            SwipeNav.EdgeHintFor(40, canGoPrevious: false, canGoNext: true));
        Assert.Equal(
            SwipeNav.EdgeHint.EndOfFolder,
            SwipeNav.EdgeHintFor(-40, canGoPrevious: true, canGoNext: false));
        Assert.Equal(
            SwipeNav.EdgeHint.None,
            SwipeNav.EdgeHintFor(-40, canGoPrevious: true, canGoNext: true));
        Assert.Equal(SwipeNav.StartOfFolderHint, SwipeNav.EdgeHintText(SwipeNav.EdgeHint.StartOfFolder));
        Assert.Equal(SwipeNav.EndOfFolderHint, SwipeNav.EdgeHintText(SwipeNav.EdgeHint.EndOfFolder));

        // Browse visual offset rubber-bands only at ends.
        Assert.Equal(20, SwipeNav.BrowseVisualOffset(100, 200, canGoPrevious: false, canGoNext: true), 3);
        Assert.Equal(100, SwipeNav.BrowseVisualOffset(100, 200, canGoPrevious: true, canGoNext: true), 3);
        Assert.Equal(-20, SwipeNav.BrowseVisualOffset(-100, 200, canGoPrevious: true, canGoNext: false), 3);
    }

    [Fact]
    public void Commit_ThirtyPercentOrFlick_FlickBackCancels()
    {
        const double w = 200;
        // 30% of 200 = 60.
        Assert.False(SwipeNav.ShouldCommitBrowse(travelPx: -50, velocityXPxPerMs: 0, viewerWidth: w));
        Assert.True(SwipeNav.ShouldCommitBrowse(travelPx: -60, velocityXPxPerMs: 0, viewerWidth: w));
        Assert.True(SwipeNav.ShouldCommitBrowse(travelPx: -10, velocityXPxPerMs: -0.6, viewerWidth: w));
        // Past 30% but flick back against drag → cancel.
        Assert.False(SwipeNav.ShouldCommitBrowse(travelPx: -80, velocityXPxPerMs: 0.6, viewerWidth: w));
        Assert.False(SwipeNav.ShouldCommitBrowse(travelPx: 80, velocityXPxPerMs: -0.6, viewerWidth: w));

        Assert.Equal(
            TouchGestures.SwipeAction.BrowseNext,
            SwipeNav.ClassifyBrowseCommit(-80, 0, w, canGoPrevious: true, canGoNext: true));
        Assert.Equal(
            TouchGestures.SwipeAction.BrowsePrevious,
            SwipeNav.ClassifyBrowseCommit(80, 0, w, canGoPrevious: true, canGoNext: true));
        Assert.Equal(
            TouchGestures.SwipeAction.SpringBack,
            SwipeNav.ClassifyBrowseCommit(-80, 0.6, w, canGoPrevious: true, canGoNext: true));
        // Edge: committed travel with no neighbor → spring back.
        Assert.Equal(
            TouchGestures.SwipeAction.SpringBack,
            SwipeNav.ClassifyBrowseCommit(-80, 0, w, canGoPrevious: true, canGoNext: false));
        Assert.Equal(
            TouchGestures.SwipeAction.SpringBack,
            SwipeNav.ClassifyBrowseCommit(20, 0, w, canGoPrevious: true, canGoNext: true));
    }

    [Fact]
    public void Review_NeverRevealsNeighbor_TiltAndTint()
    {
        Assert.False(SwipeNav.RevealsNeighbor(reviewMode: true));
        Assert.True(SwipeNav.RevealsNeighbor(reviewMode: false));
        Assert.Equal(0, SwipeNav.NeighborSlot(-80, reviewMode: true));
        Assert.Equal(1, SwipeNav.NeighborSlot(-80, reviewMode: false));
        Assert.Equal(-1, SwipeNav.NeighborSlot(80, reviewMode: false));

        // Neighbor sits one stride away from current visual offset.
        Assert.Equal(-80 + 216, SwipeNav.NeighborTranslation(-80, slot: 1, viewerWidth: 200), 3);
        Assert.Equal(80 - 216, SwipeNav.NeighborTranslation(80, slot: -1, viewerWidth: 200), 3);

        Assert.True(SwipeNav.ReviewTiltDegrees(-100, 200) < 0);
        Assert.True(SwipeNav.ReviewTiltDegrees(100, 200) > 0);
        Assert.InRange(SwipeNav.ReviewTiltDegrees(1000, 200), -18, 18);
        Assert.True(SwipeNav.ReviewTintIsReject(-40));
        Assert.False(SwipeNav.ReviewTintIsReject(40));
        Assert.InRange(SwipeNav.ReviewTintOpacity(-60, 200), 0.4, 0.45); // at 30% threshold
        Assert.Equal(0, SwipeNav.ReviewTintOpacity(0, 200), 3);
    }

    [Fact]
    public void Video_ScrubBarVsPage_AndPauseOnLeave()
    {
        // Not playing → page (unless transport dead zone).
        Assert.Equal(
            SwipeNav.DragKind.BrowsePage,
            SwipeNav.ClassifyVideoDrag(
                isVideo: true, isPlaying: false, touchGesturesEnabled: true,
                yFromBottom: 200, transportChromeVisible: false));
        Assert.Equal(
            SwipeNav.DragKind.None,
            SwipeNav.ClassifyVideoDrag(
                isVideo: true, isPlaying: false, touchGesturesEnabled: true,
                yFromBottom: 40, transportChromeVisible: true));

        // Playing + chrome hidden: 28 DIP scrub strip; elsewhere pages.
        Assert.Equal(
            SwipeNav.DragKind.VideoScrub,
            SwipeNav.ClassifyVideoDrag(
                isVideo: true, isPlaying: true, touchGesturesEnabled: true,
                yFromBottom: 20, transportChromeVisible: false));
        Assert.Equal(
            SwipeNav.DragKind.BrowsePage,
            SwipeNav.ClassifyVideoDrag(
                isVideo: true, isPlaying: true, touchGesturesEnabled: true,
                yFromBottom: 100, transportChromeVisible: false));
        // Playing + chrome visible: scrub only on ProgressSlider (B5 / #128).
        Assert.Equal(
            SwipeNav.DragKind.None,
            SwipeNav.ClassifyVideoDrag(
                isVideo: true, isPlaying: true, touchGesturesEnabled: true,
                yFromBottom: 20, transportChromeVisible: true, overProgressSlider: false));
        Assert.Equal(
            SwipeNav.DragKind.VideoScrub,
            SwipeNav.ClassifyVideoDrag(
                isVideo: true, isPlaying: true, touchGesturesEnabled: true,
                yFromBottom: 20, transportChromeVisible: true, overProgressSlider: true));
        Assert.True(SwipeNav.AllowsVideoPageSwipe(
            true, true, true, yFromBottom: 100, transportChromeVisible: false));
        Assert.True(SwipeNav.AllowsVideoScrub(
            true, true, true, yFromBottom: 10, transportChromeVisible: false));
        Assert.False(SwipeNav.AllowsVideoScrub(
            true, false, true, yFromBottom: 10, transportChromeVisible: false));

        Assert.True(SwipeNav.ShouldPauseVideoOnLeave(isVideo: true, wasPlaying: true));
        Assert.False(SwipeNav.ShouldPauseVideoOnLeave(isVideo: true, wasPlaying: false));
        Assert.True(SwipeNav.NeighborUsesPoster(isVideo: true));
        Assert.False(SwipeNav.NeighborUsesPoster(isVideo: false));
    }

    [Fact]
    public void Settle_AnimationsOffIsInstant_OvershootCapped()
    {
        Assert.Equal(220, SwipeNav.SettleDurationMs(animationsEnabled: true, committing: true));
        Assert.Equal(280, SwipeNav.SettleDurationMs(animationsEnabled: true, committing: false));
        Assert.Equal(0, SwipeNav.SettleDurationMs(animationsEnabled: false, committing: true));
        Assert.Equal(0, SwipeNav.SettleDurationMs(animationsEnabled: false, committing: false));

        Assert.Equal(-216, SwipeNav.SettleTargetX(TouchGestures.SwipeAction.BrowseNext, 200), 3);
        Assert.Equal(216, SwipeNav.SettleTargetX(TouchGestures.SwipeAction.BrowsePrevious, 200), 3);
        Assert.Equal(0, SwipeNav.SettleTargetX(TouchGestures.SwipeAction.SpringBack, 200), 3);

        // N3: after navigate, jump to neighbor slot then settle to 0.
        Assert.Equal(1, SwipeNav.CommitSlot(TouchGestures.SwipeAction.BrowseNext));
        Assert.Equal(-1, SwipeNav.CommitSlot(TouchGestures.SwipeAction.BrowsePrevious));
        Assert.Equal(0, SwipeNav.CommitSlot(TouchGestures.SwipeAction.SpringBack));
        Assert.Equal(136, SwipeNav.PostCommitStartX(releaseX: -80, slot: 1, viewerWidth: 200), 3);
        Assert.Equal(-136, SwipeNav.PostCommitStartX(releaseX: 80, slot: -1, viewerWidth: 200), 3);
        Assert.Equal(500, SwipeNav.AwaitingContentTimeoutMs);

        Assert.Equal(4, SwipeNav.SnapOvershootPx(1.0), 3); // capped
        Assert.True(SwipeNav.SnapOvershootPx(0.05) <= SwipeNav.MaxOvershootPx);
        // B3: reset local zoom only when KeepZoomBetweenImages is off.
        Assert.True(SwipeNav.ResetsLocalZoomOnCommit(
            TouchGestures.SwipeAction.BrowseNext, keepZoomBetweenImages: false));
        Assert.False(SwipeNav.ResetsLocalZoomOnCommit(
            TouchGestures.SwipeAction.BrowseNext, keepZoomBetweenImages: true));
        Assert.False(SwipeNav.ResetsLocalZoomOnCommit(
            TouchGestures.SwipeAction.SpringBack, keepZoomBetweenImages: false));
    }

    [Fact]
    public void ClassifyBrowseCommit_UsesRealDyForAxisLock()
    {
        // Dominant vertical travel must not classify as browse (S9).
        Assert.Equal(
            TouchGestures.SwipeAction.None,
            SwipeNav.ClassifyBrowseCommit(
                travelPx: -20,
                velocityXPxPerMs: 0,
                viewerWidth: 200,
                canGoPrevious: true,
                canGoNext: true,
                travelPy: -80));
        Assert.Equal(
            TouchGestures.SwipeAction.BrowseNext,
            SwipeNav.ClassifyBrowseCommit(
                travelPx: -80,
                velocityXPxPerMs: 0,
                viewerWidth: 200,
                canGoPrevious: true,
                canGoNext: true,
                travelPy: -10));
    }

    [Fact]
    public void ZoomHandoff_CountsOnlyTravelPastEdge()
    {
        // Pan −40 then handoff −70 → commit travel −70 (not −110).
        Assert.Equal(-70, SwipeNav.CommitTravel(totalDragPx: -110, panBeforeHandoffPx: -40), 3);
        Assert.Equal(0, SwipeNav.CommitTravel(totalDragPx: -30, panBeforeHandoffPx: -40), 3);
        Assert.True(SwipeNav.ShouldCommitBrowse(
            SwipeNav.CommitTravel(-110, -40), velocityXPxPerMs: 0, viewerWidth: 200));
        Assert.False(SwipeNav.ShouldCommitBrowse(
            SwipeNav.CommitTravel(-50, -40), velocityXPxPerMs: 0, viewerWidth: 200));
    }

    [Fact]
    public void ClassifyBrowseSwipe_UsesSwipeNavThreshold()
    {
        // Legacy entry point must honor 30% (was 35% before swipe-nav).
        Assert.Equal(
            TouchGestures.SwipeAction.SpringBack,
            TouchGestures.ClassifyBrowseSwipe(-50, 0, 200, 0)); // 25%
        Assert.Equal(
            TouchGestures.SwipeAction.BrowseNext,
            TouchGestures.ClassifyBrowseSwipe(-60, 0, 200, 0)); // 30%
        Assert.Equal(
            TouchGestures.SwipeAction.SpringBack,
            TouchGestures.ClassifyBrowseSwipe(-80, 0, 200, 0.6)); // flick back
    }
}
