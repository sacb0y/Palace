namespace Palace.Helpers;

/// <summary>
/// Visual pager policy for viewer swipe-nav (docs/specs/swipe-nav.md, Isiac Oct 22).
/// Off WinUI for Palace.Tests. Custom pager — not FlipView.
/// </summary>
public static class SwipeNav
{
    /// <summary>Gap between current and neighbor pages (DIP).</summary>
    public const double NeighborGapPx = 16;

    /// <summary>Commit when |travel| ≥ this fraction of viewer width.</summary>
    public const double CommitFraction = 0.30;

    /// <summary>Commit when |velocity| exceeds this (px/ms) in the drag direction.</summary>
    public const double FlickPxPerMs = 0.5;

    /// <summary>Commit settle animation duration (ms).</summary>
    public const int CommitMs = 220;

    /// <summary>Snap-back spring duration (ms).</summary>
    public const int SnapBackMs = 280;

    /// <summary>Max snap-back overshoot (px).</summary>
    public const double MaxOvershootPx = 4;

    /// <summary>Edge rubber-band gain (first/last item).</summary>
    public const double EdgeResistanceGain = 0.3;

    /// <summary>Playing-video scrub bar hit height (DIP).</summary>
    public const double VideoScrubHitDip = 28;

    /// <summary>cubic-bezier(0.1, 0.9, 0.2, 1) control points.</summary>
    public const double EaseX1 = 0.1;
    public const double EaseY1 = 0.9;
    public const double EaseX2 = 0.2;
    public const double EaseY2 = 1.0;

    public const string EndOfFolderHint = "End of folder";
    public const string StartOfFolderHint = "Start of folder";

    public enum DragKind
    {
        /// <summary>Browse pager — current tracks finger; neighbor may slide in.</summary>
        BrowsePage,
        /// <summary>Review classify — tilt/tint only; never reveal neighbor.</summary>
        ReviewClassify,
        /// <summary>Playing video scrub on the 28 DIP bar.</summary>
        VideoScrub,
        /// <summary>No pager / scrub for this pointer.</summary>
        None
    }

    public enum EdgeHint
    {
        None,
        StartOfFolder,
        EndOfFolder
    }

    /// <summary>
    /// Stride from current page origin to neighbor origin (= width + gap).
    /// </summary>
    public static double PageStride(double viewerWidth) =>
        Math.Max(1, viewerWidth) + NeighborGapPx;

    /// <summary>
    /// First/last edge resistance: offset = d × 0.3 / (1 + |d| / viewerWidth).
    /// Always snaps back on release (no wrap).
    /// </summary>
    public static double EdgeResistanceOffset(double dragPx, double viewerWidth)
    {
        if (Math.Abs(dragPx) < 0.0001)
        {
            return 0;
        }

        var w = Math.Max(1, viewerWidth);
        var abs = Math.Abs(dragPx);
        var soft = abs * EdgeResistanceGain / (1.0 + abs / w);
        return Math.Sign(dragPx) * soft;
    }

    /// <summary>
    /// Visual X for the current page during a browse drag. Applies edge resistance
    /// when there is no neighbor on that side. Review never uses this path for
    /// neighbor reveal — see <see cref="RevealsNeighbor"/>.
    /// </summary>
    public static double BrowseVisualOffset(
        double dragPx,
        double viewerWidth,
        bool canGoPrevious,
        bool canGoNext)
    {
        if (dragPx > 0 && !canGoPrevious)
        {
            return EdgeResistanceOffset(dragPx, viewerWidth);
        }

        if (dragPx < 0 && !canGoNext)
        {
            return EdgeResistanceOffset(dragPx, viewerWidth);
        }

        return dragPx;
    }

    /// <summary>Neighbor is never shown during Review drag.</summary>
    public static bool RevealsNeighbor(bool reviewMode) => !reviewMode;

    /// <summary>
    /// Which neighbor is on-screen during a browse drag (none in Review or at rest).
    /// Positive offset (finger right) reveals previous; negative reveals next.
    /// </summary>
    public static int NeighborSlot(double visualOffsetPx, bool reviewMode)
    {
        if (reviewMode || Math.Abs(visualOffsetPx) < 0.0001)
        {
            return 0;
        }

        return visualOffsetPx > 0 ? -1 : 1;
    }

    /// <summary>
    /// Neighbor page X = visualOffset + slot × stride. Current stays at visualOffset.
    /// </summary>
    public static double NeighborTranslation(
        double visualOffsetPx,
        int slot,
        double viewerWidth)
    {
        if (slot == 0)
        {
            return visualOffsetPx;
        }

        return visualOffsetPx + slot * PageStride(viewerWidth);
    }

    public static EdgeHint EdgeHintFor(
        double dragPx,
        bool canGoPrevious,
        bool canGoNext)
    {
        if (dragPx > 0 && !canGoPrevious)
        {
            return EdgeHint.StartOfFolder;
        }

        if (dragPx < 0 && !canGoNext)
        {
            return EdgeHint.EndOfFolder;
        }

        return EdgeHint.None;
    }

    public static string? EdgeHintText(EdgeHint hint) => hint switch
    {
        EdgeHint.StartOfFolder => StartOfFolderHint,
        EdgeHint.EndOfFolder => EndOfFolderHint,
        _ => null
    };

    /// <summary>
    /// Soft accent edge-glow opacity 0…1 while rubber-banding at ends.
    /// </summary>
    public static double EdgeGlowOpacity(double visualOffsetPx, double viewerWidth)
    {
        var w = Math.Max(1, viewerWidth);
        var t = Math.Abs(visualOffsetPx) / (w * EdgeResistanceGain);
        if (t <= 0)
        {
            return 0;
        }

        return t >= 1 ? 1 : t;
    }

    /// <summary>
    /// Travel that counts toward the commit threshold. Zoomed handoff: only the
    /// distance past the pan-edge, not the earlier pan.
    /// </summary>
    public static double CommitTravel(double totalDragPx, double panBeforeHandoffPx)
    {
        var handoff = totalDragPx - panBeforeHandoffPx;
        // Same sign as total drag; zero if the handoff has not started.
        if (Math.Sign(handoff) != Math.Sign(totalDragPx) && Math.Abs(totalDragPx) > 0.0001)
        {
            return 0;
        }

        return handoff;
    }

    /// <summary>
    /// Commit when |travel| ≥ 30% width or flick &gt; 0.5 px/ms in the drag
    /// direction. A flick back against the drag cancels even past 30%.
    /// </summary>
    public static bool ShouldCommitBrowse(
        double travelPx,
        double velocityXPxPerMs,
        double viewerWidth)
    {
        if (Math.Abs(travelPx) < 0.0001 && Math.Abs(velocityXPxPerMs) <= FlickPxPerMs)
        {
            return false;
        }

        // Flick against the drag cancels.
        if (Math.Abs(velocityXPxPerMs) > FlickPxPerMs
            && Math.Sign(velocityXPxPerMs) != Math.Sign(travelPx)
            && Math.Abs(travelPx) > 0.0001)
        {
            return false;
        }

        var width = Math.Max(1, viewerWidth);
        if (Math.Abs(travelPx) >= CommitFraction * width)
        {
            return true;
        }

        // Flick in the drag direction (or pure flick with tiny travel).
        if (Math.Abs(velocityXPxPerMs) > FlickPxPerMs)
        {
            if (Math.Abs(travelPx) < 0.0001)
            {
                return true;
            }

            return Math.Sign(velocityXPxPerMs) == Math.Sign(travelPx);
        }

        return false;
    }

    /// <summary>
    /// Browse action from committed travel. Finger left (negative) → next.
    /// At edges with resistance-only travel, returns SpringBack.
    /// </summary>
    public static TouchGestures.SwipeAction ClassifyBrowseCommit(
        double travelPx,
        double velocityXPxPerMs,
        double viewerWidth,
        bool canGoPrevious,
        bool canGoNext,
        double travelPy = 0)
    {
        if (!TouchGestures.IsHorizontalAxis(travelPx, travelPy)
            && Math.Abs(velocityXPxPerMs) <= FlickPxPerMs)
        {
            return TouchGestures.SwipeAction.None;
        }

        if (!ShouldCommitBrowse(travelPx, velocityXPxPerMs, viewerWidth))
        {
            return TouchGestures.SwipeAction.SpringBack;
        }

        var goingNext = travelPx < 0
            || (Math.Abs(travelPx) < 0.0001 && velocityXPxPerMs < 0);
        if (goingNext)
        {
            return canGoNext
                ? TouchGestures.SwipeAction.BrowseNext
                : TouchGestures.SwipeAction.SpringBack;
        }

        return canGoPrevious
            ? TouchGestures.SwipeAction.BrowsePrevious
            : TouchGestures.SwipeAction.SpringBack;
    }

    /// <summary>
    /// Settle duration. When Windows Animation effects are off, swaps are
    /// instant (0) but the drag still tracks during the gesture.
    /// </summary>
    public static int SettleDurationMs(bool animationsEnabled, bool committing) =>
        animationsEnabled
            ? (committing ? CommitMs : SnapBackMs)
            : 0;

    /// <summary>
    /// Target translation for settle: ±stride on commit, 0 on snap-back.
    /// </summary>
    public static double SettleTargetX(
        TouchGestures.SwipeAction action,
        double viewerWidth) => action switch
    {
        TouchGestures.SwipeAction.BrowseNext => -PageStride(viewerWidth),
        TouchGestures.SwipeAction.BrowsePrevious => PageStride(viewerWidth),
        _ => 0
    };

    /// <summary>
    /// After navigate-at-commit, jump the new still to where the neighbor was
    /// (releaseX + slot×stride) before settling to 0 — avoids sliding the new
    /// image in from the wrong side.
    /// </summary>
    public static double PostCommitStartX(double releaseX, int slot, double viewerWidth) =>
        releaseX + slot * PageStride(viewerWidth);

    /// <summary>Browse commit slot: +1 next, −1 previous, else 0.</summary>
    public static int CommitSlot(TouchGestures.SwipeAction action) => action switch
    {
        TouchGestures.SwipeAction.BrowseNext => 1,
        TouchGestures.SwipeAction.BrowsePrevious => -1,
        _ => 0
    };

    /// <summary>Stuck awaiting-content snap-back (ms).</summary>
    public const int AwaitingContentTimeoutMs = 500;

    /// <summary>
    /// Review card tilt (degrees) from horizontal drag. Caps soft so it never
    /// flips; neighbor is never revealed (<see cref="RevealsNeighbor"/>).
    /// </summary>
    public static double ReviewTiltDegrees(double dragPx, double viewerWidth)
    {
        var w = Math.Max(1, viewerWidth);
        var t = dragPx / w;
        // ~12° at full width, soft clamp.
        var deg = t * 12.0;
        if (deg > 18)
        {
            return 18;
        }

        if (deg < -18)
        {
            return -18;
        }

        return deg;
    }

    /// <summary>
    /// Review tint strength 0…1. Negative drag (reject) and positive (keep)
    /// share magnitude; callers pick red vs green from sign.
    /// </summary>
    public static double ReviewTintOpacity(double dragPx, double viewerWidth)
    {
        var w = Math.Max(1, viewerWidth);
        var t = Math.Abs(dragPx) / (CommitFraction * w);
        if (t <= 0)
        {
            return 0;
        }

        return t >= 1 ? 0.45 : t * 0.45;
    }

    /// <summary>True when Review drag leans reject (finger left).</summary>
    public static bool ReviewTintIsReject(double dragPx) => dragPx < 0;

    /// <summary>
    /// Playing-video pointer routing. When transport chrome is visible, scrub
    /// only on the real ProgressSlider (pass <paramref name="overProgressSlider"/>);
    /// the 28 DIP strip applies only when chrome is hidden. Never steal the
    /// #128 transport dead zone.
    /// </summary>
    public static DragKind ClassifyVideoDrag(
        bool isVideo,
        bool isPlaying,
        bool touchGesturesEnabled,
        double yFromBottom,
        bool transportChromeVisible,
        bool overProgressSlider = false)
    {
        if (!isVideo || !touchGesturesEnabled)
        {
            return DragKind.None;
        }

        if (!isPlaying)
        {
            // Paused: yield to transport dead zone when chrome is up (#128).
            if (TouchGestures.BlocksGestureOverVideoControls(transportChromeVisible, yFromBottom))
            {
                return DragKind.None;
            }

            return DragKind.BrowsePage;
        }

        if (transportChromeVisible)
        {
            // Scrub only on the ProgressSlider — not the whole bottom band.
            if (overProgressSlider)
            {
                return DragKind.VideoScrub;
            }

            if (TouchGestures.BlocksGestureOverVideoControls(true, yFromBottom))
            {
                return DragKind.None;
            }

            return DragKind.BrowsePage;
        }

        // Chrome hidden: 28 DIP scrub strip from the bottom.
        if (yFromBottom <= VideoScrubHitDip)
        {
            return DragKind.VideoScrub;
        }

        return DragKind.BrowsePage;
    }

    /// <summary>
    /// Playing video may page (elsewhere) or scrub (slider / hidden-chrome strip).
    /// </summary>
    public static bool AllowsVideoPageSwipe(
        bool isVideo,
        bool isPlaying,
        bool touchGesturesEnabled,
        double yFromBottom,
        bool transportChromeVisible,
        bool overProgressSlider = false) =>
        ClassifyVideoDrag(
            isVideo, isPlaying, touchGesturesEnabled, yFromBottom, transportChromeVisible,
            overProgressSlider)
        == DragKind.BrowsePage;

    public static bool AllowsVideoScrub(
        bool isVideo,
        bool isPlaying,
        bool touchGesturesEnabled,
        double yFromBottom,
        bool transportChromeVisible,
        bool overProgressSlider = false) =>
        ClassifyVideoDrag(
            isVideo, isPlaying, touchGesturesEnabled, yFromBottom, transportChromeVisible,
            overProgressSlider)
        == DragKind.VideoScrub;

    /// <summary>
    /// Leaving a video (page commit) pauses and keeps position — callers save
    /// PlaybackSession.Position then Pause.
    /// </summary>
    public static bool ShouldPauseVideoOnLeave(bool isVideo, bool wasPlaying) =>
        isVideo && wasPlaying;

    /// <summary>
    /// Neighbor for a video item is its poster (never a live decode during drag).
    /// </summary>
    public static bool NeighborUsesPoster(bool isVideo) => isVideo;

    /// <summary>
    /// Local zoom/pan reset on browse commit only when
    /// <see cref="ViewerSettings.KeepZoomBetweenImages"/> is off (#128).
    /// Never write <c>GalleryViewModel.Scaling</c> from the pager.
    /// </summary>
    public static bool ResetsLocalZoomOnCommit(
        TouchGestures.SwipeAction action,
        bool keepZoomBetweenImages) =>
        !keepZoomBetweenImages
        && action is TouchGestures.SwipeAction.BrowseNext
            or TouchGestures.SwipeAction.BrowsePrevious;

    /// <summary>
    /// Snap-back overshoot keyframe (px). Cap at <see cref="MaxOvershootPx"/>.
    /// </summary>
    public static double SnapOvershootPx(double releaseVelocityPxPerMs)
    {
        var raw = Math.Abs(releaseVelocityPxPerMs) * 40; // soft spring feel
        if (raw <= 0)
        {
            return 0;
        }

        return raw > MaxOvershootPx ? MaxOvershootPx : raw;
    }
}
