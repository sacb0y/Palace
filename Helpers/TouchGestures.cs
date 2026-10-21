namespace Palace.Helpers;

/// <summary>
/// Viewer / mosaic touch gesture policy (Gooey touch-gestures.md + Isiac 2026-10-21).
/// Touch only — callers gate on <c>PointerDeviceType.Touch</c>. Off WinUI for tests.
/// </summary>
public static class TouchGestures
{
    public const int LongPressMs = 500;
    public const double MoveSlopPx = 8;
    public const double AxisLockPx = 10;
    public const double AxisLockRatio = 1.5;
    public const double EdgeHandoffPx = 24;
    public const double SwipeCommitFraction = 0.35;
    /// <summary>Review keep/reject commit uses height (Isiac 2026-10-21).</summary>
    public const double ReviewCommitFraction = 0.30;
    public const double SwipeFlickPxPerMs = 0.5;
    public const double InertiaDecelPxPerMs2 = 0.0025;
    public const double RubberBandFactor = 0.35;
    public const double RubberBandCapFraction = 0.25;
    public const int RubberBandSpringMs = 300;
    public const int DoubleTapEaseMs = 250;
    public const int ZoomPillFadeMs = 800;
    public const int ChromeFadeMs = 150;
    public const double PinchMinFitFactor = 0.5;
    public const double PinchMaxActualFactor = 8.0;
    public const double PinchMaxFitFactor = 4.0;
    public const double PinchSoftOvershoot = 1.15;
    /// <summary>
    /// Layout long-edge cap (DIPs). Past this, DXGI ResizeBuffers fails and HDR
    /// present collapses to a blank still — Sacb0y pinch-out black on 4K.
    /// </summary>
    public const double MaxStillPanelLongEdgeDip = 8192;
    public const double DoubleTapActualMinFit = 1.25;
    public const double DoubleTapFallbackFit = 2.5;
    public const double VideoSeekSeconds = 10;
    /// <summary>
    /// Bottom inset for the paused-video browse-swipe catcher so MediaTransportControls
    /// (seek bar + command row) stay hittable. 72 was too short — finger-drag on the
    /// paused seek bar hit the catcher instead of the slider. Keep XAML/code margins in sync.
    /// </summary>
    public const double VideoControlsDeadZoneDip = 128;

    public enum SwipeAction
    {
        None,
        BrowsePrevious,
        BrowseNext,
        Keep,
        Reject,
        Skip,
        SpringBack
    }

    public enum DoubleTapZoomStep
    {
        Fit,
        Zoomed
    }

    public enum VideoDoubleTapZone
    {
        LeftSeekBack,
        CenterZoom,
        RightSeekForward
    }

    /// <summary>Touch-only gate for gesture handlers.</summary>
    public static bool IsTouchOnly(bool isTouch) => isTouch;

    public static bool LongPressReady(int elapsedMs) => elapsedMs >= LongPressMs;

    public static bool ExceedsSlop(double dx, double dy) =>
        (dx * dx) + (dy * dy) >= MoveSlopPx * MoveSlopPx;

    public static bool CancelsLongPress(int elapsedMs, double dx, double dy) =>
        !LongPressReady(elapsedMs) && ExceedsSlop(dx, dy);

    /// <summary>
    /// Mosaic long-press enters multi-select. Viewer long-press opens the
    /// context menu (not select badge). Video long-press in the mosaic is select.
    /// </summary>
    public static bool MosaicHoldEntersSelect(bool selectMode, bool overlayOpen, bool isFolderHeader) =>
        SelectMode.EnterFromHold(selectMode, overlayOpen, isFolderHeader);

    /// <summary>
    /// Superseded 2026-10-21: viewer long-press opens ContextFlyout, not a
    /// select badge. Kept returning false so old call sites stay safe.
    /// </summary>
    public static bool ViewerHoldTogglesSelection(bool overlayOpen) => false;

    /// <summary>
    /// Single-image viewer long-press opens the shared ContextFlyout (same as
    /// … / right-click). No-op in Review mode (#129).
    /// </summary>
    public static bool ViewerHoldOpensContext(bool overlayOpen, bool reviewMode) =>
        overlayOpen && !reviewMode;

    /// <summary>
    /// Mosaic: touch long-press is select — suppress ItemContainer ContextFlyout.
    /// Viewer uses <see cref="ViewerHoldOpensContext"/> instead.
    /// </summary>
    public static bool OpensContextFromTouchHold() => false;

    public static bool OpensContextFromRightClick(bool isFolderHeader) =>
        GalleryMedia.ShouldShowAssetContextFlyout(isFolderHeader);

    /// <summary>
    /// Axis lock after <see cref="AxisLockPx"/>: horizontal when |dx| &gt; 1.5|dy|.
    /// </summary>
    public static bool IsHorizontalAxis(double dx, double dy)
    {
        var adx = Math.Abs(dx);
        var ady = Math.Abs(dy);
        if (adx < AxisLockPx && ady < AxisLockPx)
        {
            return false;
        }

        return adx > AxisLockRatio * ady;
    }

    public static bool IsVerticalAxis(double dx, double dy)
    {
        var adx = Math.Abs(dx);
        var ady = Math.Abs(dy);
        if (adx < AxisLockPx && ady < AxisLockPx)
        {
            return false;
        }

        return ady > AxisLockRatio * adx;
    }

    /// <summary>
    /// Zoomed one finger always pans. Swipe handoff only if pinned at an edge
    /// when the touch started and drag goes more than 24px outward.
    /// </summary>
    public static bool AllowsSwipeHandoff(
        bool zoomed,
        bool pinnedAtStartEdge,
        double outwardDeltaPx) =>
        zoomed && pinnedAtStartEdge && outwardDeltaPx > EdgeHandoffPx;

    public static bool AllowsSwipe(bool zoomed, int fingerCount, bool afterPinchAwaitingLift) =>
        !zoomed && fingerCount == 1 && !afterPinchAwaitingLift;

    /// <summary>
    /// Isiac 2026-10-21 supersedes CORRECTION: left = reject, right = keep,
    /// down is not reject. Up = skip (Review). Not zoomed.
    /// </summary>
    public static SwipeAction ClassifyReviewSwipe(
        double dx,
        double dy,
        double viewportWidth,
        double viewportHeight,
        double velocityXPxPerMs,
        double velocityYPxPerMs)
    {
        // Axis lock after 10px, or by dominant flick velocity when distance is small.
        var horizontal = IsHorizontalAxis(dx, dy)
            || (Math.Abs(velocityXPxPerMs) > SwipeFlickPxPerMs
                && Math.Abs(velocityXPxPerMs) > AxisLockRatio * Math.Abs(velocityYPxPerMs));
        var vertical = IsVerticalAxis(dx, dy)
            || (Math.Abs(velocityYPxPerMs) > SwipeFlickPxPerMs
                && Math.Abs(velocityYPxPerMs) > AxisLockRatio * Math.Abs(velocityXPxPerMs));

        if (!horizontal && !vertical)
        {
            return SwipeAction.None;
        }

        if (horizontal && (!vertical || Math.Abs(dx) >= Math.Abs(dy)
            || Math.Abs(velocityXPxPerMs) >= Math.Abs(velocityYPxPerMs)))
        {
            var commit = Math.Abs(dx) >= ReviewCommitFraction * Math.Max(1, viewportHeight)
                || Math.Abs(velocityXPxPerMs) > SwipeFlickPxPerMs;
            if (!commit)
            {
                return SwipeAction.SpringBack;
            }

            // Direction: prefer displacement; fall back to velocity for short flicks.
            var left = dx < 0 || (Math.Abs(dx) < 0.0001 && velocityXPxPerMs < 0);
            return left ? SwipeAction.Reject : SwipeAction.Keep;
        }

        // Vertical: up = skip; down does nothing (not reject).
        var up = dy < 0 || (Math.Abs(dy) < 0.0001 && velocityYPxPerMs < 0);
        if (up)
        {
            var commit = Math.Abs(dy) >= ReviewCommitFraction * Math.Max(1, viewportHeight)
                || Math.Abs(velocityYPxPerMs) > SwipeFlickPxPerMs;
            return commit ? SwipeAction.Skip : SwipeAction.SpringBack;
        }

        return SwipeAction.None;
    }

    /// <summary>
    /// Non-review browse swipe when not zoomed: left/right = next/prev.
    /// Prefer <see cref="ClassifyReviewSwipe"/> for keep/reject Review.
    /// </summary>
    public static SwipeAction ClassifyBrowseSwipe(
        double dx,
        double dy,
        double viewportWidth,
        double velocityXPxPerMs)
    {
        if (!IsHorizontalAxis(dx, dy))
        {
            return SwipeAction.None;
        }

        var commit = Math.Abs(dx) >= SwipeCommitFraction * Math.Max(1, viewportWidth)
            || Math.Abs(velocityXPxPerMs) > SwipeFlickPxPerMs;
        if (!commit)
        {
            return SwipeAction.SpringBack;
        }

        // Content follows finger: swipe left → next.
        return dx < 0 ? SwipeAction.BrowseNext : SwipeAction.BrowsePrevious;
    }

    public const string RejectedFolderName = "_rejected";

    public static string RejectedFolderPath(string sourceRoot) =>
        Path.Combine(sourceRoot, RejectedFolderName);

    /// <summary>
    /// Soft rubber-band past a hard limit: limit + d×0.35, capped at 25% of viewport.
    /// </summary>
    public static double RubberBand(double overshoot, double viewportSize)
    {
        if (overshoot <= 0 || viewportSize <= 0)
        {
            return 0;
        }

        var soft = overshoot * RubberBandFactor;
        var cap = viewportSize * RubberBandCapFraction;
        return soft > cap ? cap : soft;
    }

    /// <summary>
    /// Per-axis pan clamp. Content larger than the viewport cannot pass either
    /// edge; smaller content stays centered (fixes up-but-not-down).
    /// Returns the clamped content origin (top-left) in viewport space.
    /// </summary>
    public static double ClampPanAxis(
        double contentOrigin,
        double contentSize,
        double viewportSize,
        bool allowRubberBand = false)
    {
        if (viewportSize <= 0)
        {
            return 0;
        }

        if (contentSize <= viewportSize)
        {
            // Smaller: centered. Do not allow free pan past center.
            var centered = (viewportSize - contentSize) / 2.0;
            if (!allowRubberBand)
            {
                return centered;
            }

            var delta = contentOrigin - centered;
            if (Math.Abs(delta) < 0.0001)
            {
                return centered;
            }

            var signed = Math.Sign(delta);
            return centered + signed * RubberBand(Math.Abs(delta), viewportSize);
        }

        // Larger: content edges cannot pass viewport edges.
        // origin in [-overflow, 0] when content draws from origin.
        var minOrigin = viewportSize - contentSize; // negative
        var maxOrigin = 0.0;
        if (contentOrigin >= minOrigin && contentOrigin <= maxOrigin)
        {
            return contentOrigin;
        }

        if (!allowRubberBand)
        {
            return contentOrigin < minOrigin ? minOrigin : maxOrigin;
        }

        if (contentOrigin > maxOrigin)
        {
            return maxOrigin + RubberBand(contentOrigin - maxOrigin, viewportSize);
        }

        return minOrigin - RubberBand(minOrigin - contentOrigin, viewportSize);
    }

    /// <summary>
    /// ScrollViewer-style offset clamp (0 … max(0, content − viewport)), with
    /// the same per-axis centering when content is smaller.
    /// </summary>
    public static double ClampScrollOffset(
        double offset,
        double contentSize,
        double viewportSize,
        bool allowRubberBand = false)
    {
        if (viewportSize <= 0)
        {
            return 0;
        }

        if (contentSize <= viewportSize)
        {
            // Offset stays 0; centering is layout, not scroll. Rubber-band may
            // briefly go negative / past zero for spring feedback.
            if (!allowRubberBand)
            {
                return 0;
            }

            if (Math.Abs(offset) < 0.0001)
            {
                return 0;
            }

            var signed = Math.Sign(offset);
            return signed * RubberBand(Math.Abs(offset), viewportSize);
        }

        var max = contentSize - viewportSize;
        if (offset >= 0 && offset <= max)
        {
            return offset;
        }

        if (!allowRubberBand)
        {
            if (offset < 0)
            {
                return 0;
            }

            return offset > max ? max : offset;
        }

        if (offset < 0)
        {
            return -RubberBand(-offset, viewportSize);
        }

        return max + RubberBand(offset - max, viewportSize);
    }

    public static (double Horizontal, double Vertical) ClampPan(
        double offsetX,
        double offsetY,
        double contentWidth,
        double contentHeight,
        double viewportWidth,
        double viewportHeight,
        bool allowRubberBand = false) =>
        (
            ClampScrollOffset(offsetX, contentWidth, viewportWidth, allowRubberBand),
            ClampScrollOffset(offsetY, contentHeight, viewportHeight, allowRubberBand));

    /// <summary>
    /// Tall zoomed pan extent: take the max of every size signal. Arranged
    /// <c>ActualHeight</c> / ScrollViewer <c>ExtentHeight</c> can report short
    /// of the assigned panel Height (Disabled scroll measure / lag) — never
    /// let those shrink the clamp below the layout size we set for 1:1 / pinch.
    /// </summary>
    public static double ResolvePanContentExtent(
        double intended,
        double actual,
        double extent,
        double tracked)
    {
        var best = 0.0;
        ConsiderPanSize(ref best, intended);
        ConsiderPanSize(ref best, actual);
        ConsiderPanSize(ref best, extent);
        ConsiderPanSize(ref best, tracked);
        return best;
    }

    /// <summary>
    /// Prefer ScrollViewer.ViewportHeight when it is the smaller positive
    /// size — a larger control ActualHeight shortens max scroll
    /// (content − viewport) and stops tall pan before the image bottom.
    /// </summary>
    public static double ResolvePanViewport(double controlActual, double scrollViewport)
    {
        if (scrollViewport > 1)
        {
            if (controlActual > 1)
            {
                return Math.Min(controlActual, scrollViewport);
            }

            return scrollViewport;
        }

        return controlActual > 0 ? controlActual : 0;
    }

    /// <summary>Max ScrollViewer-style offset for a content/viewport pair.</summary>
    public static double MaxScrollOffset(double contentSize, double viewportSize) =>
        contentSize > viewportSize ? contentSize - viewportSize : 0;

    private static void ConsiderPanSize(ref double best, double value)
    {
        if (!double.IsNaN(value) && !double.IsInfinity(value) && value > best)
        {
            best = value;
        }
    }

    /// <summary>
    /// Pinch min while dragging: 0.5× Fit. On release snap back to Fit when below 1.
    /// </summary>
    public static double ClampPinchDuringGesture(double zoomFitFactor) =>
        Math.Max(PinchMinFitFactor, zoomFitFactor);

    public static double SnapPinchOnRelease(double zoomFitFactor) =>
        zoomFitFactor < 1.0 - 0.0001 ? 1.0 : zoomFitFactor;

    /// <summary>
    /// Spec max = max(8× actual-as-Fit, 4× Fit), but layout zoom never exceeds
    /// <see cref="GalleryScale.MaxPinchZoom"/> (8) — larger multipliers on Fit
    /// base size a 4K still past DXGI limits and paint black.
    /// </summary>
    public static double MaxPinchFitFactor(double fitToActualRatio)
    {
        var eightActualAsFit = PinchMaxActualFactor * Math.Max(0, fitToActualRatio);
        var spec = Math.Max(eightActualAsFit, PinchMaxFitFactor);
        return Math.Min(spec, GalleryScale.MaxPinchZoom);
    }

    /// <summary>
    /// Further cap so baseLongEdge × zoom ≤ <see cref="MaxStillPanelLongEdgeDip"/>.
    /// </summary>
    public static double MaxPinchForBaseSize(double baseLongEdgeDip)
    {
        var layoutMax = GalleryScale.MaxPinchZoom;
        if (baseLongEdgeDip <= 0)
        {
            return layoutMax;
        }

        var panelMax = MaxStillPanelLongEdgeDip / baseLongEdgeDip;
        return Math.Min(layoutMax, Math.Max(1.0, panelMax));
    }

    public static double ClampPinchHard(
        double fitFactor,
        double fitToActualRatio,
        double baseLongEdgeDip = 0)
    {
        var max = MaxPinchFitFactor(fitToActualRatio);
        if (baseLongEdgeDip > 0)
        {
            max = Math.Min(max, MaxPinchForBaseSize(baseLongEdgeDip));
        }

        if (fitFactor < PinchMinFitFactor)
        {
            return PinchMinFitFactor;
        }

        return fitFactor > max ? max : fitFactor;
    }

    public static double ClampPinchSoft(
        double fitFactor,
        double fitToActualRatio,
        double baseLongEdgeDip = 0)
    {
        // Soft ceiling from the hard max (not from an uncapped fitFactor).
        var hardMax = MaxPinchFitFactor(fitToActualRatio);
        if (baseLongEdgeDip > 0)
        {
            hardMax = Math.Min(hardMax, MaxPinchForBaseSize(baseLongEdgeDip));
        }

        var softMax = hardMax * PinchSoftOvershoot;
        if (fitFactor < PinchMinFitFactor)
        {
            return PinchMinFitFactor;
        }

        return fitFactor > softMax ? softMax : fitFactor;
    }

    public static (double Width, double Height) CapStillPanelSize(double width, double height)
    {
        if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0)
        {
            return (width, height);
        }

        var longEdge = Math.Max(width, height);
        if (longEdge <= MaxStillPanelLongEdgeDip)
        {
            return (width, height);
        }

        var scale = MaxStillPanelLongEdgeDip / longEdge;
        return (width * scale, height * scale);
    }

    /// <summary>
    /// Review file actions (reject → _rejected) require Review mode.
    /// Normal viewer must never move files.
    /// </summary>
    public static bool AllowsReviewFileActions(bool reviewMode) => reviewMode;

    public static SwipeAction ClassifySwipe(
        bool reviewMode,
        double dx,
        double dy,
        double viewportWidth,
        double viewportHeight,
        double velocityXPxPerMs,
        double velocityYPxPerMs) =>
        reviewMode
            ? ClassifyReviewSwipe(dx, dy, viewportWidth, viewportHeight, velocityXPxPerMs, velocityYPxPerMs)
            : ClassifyBrowseSwipe(dx, dy, viewportWidth, velocityXPxPerMs);

    /// <summary>
    /// Double-tap: Fit → 1:1 at tap (or 2.5× Fit if 1:1 ≤ 1.25× Fit) → Fit.
    /// Any non-Fit zoom returns to Fit.
    /// </summary>
    public static DoubleTapZoomStep NextDoubleTapStep(bool currentlyFit) =>
        currentlyFit ? DoubleTapZoomStep.Zoomed : DoubleTapZoomStep.Fit;

    public static double DoubleTapZoomFitFactor(double actualAsFitFactor)
    {
        if (actualAsFitFactor <= DoubleTapActualMinFit)
        {
            return DoubleTapFallbackFit;
        }

        return actualAsFitFactor;
    }

    public static VideoDoubleTapZone ClassifyVideoDoubleTap(double x, double width)
    {
        if (width <= 0)
        {
            return VideoDoubleTapZone.CenterZoom;
        }

        var third = width / 3.0;
        if (x < third)
        {
            return VideoDoubleTapZone.LeftSeekBack;
        }

        if (x > width - third)
        {
            return VideoDoubleTapZone.RightSeekForward;
        }

        return VideoDoubleTapZone.CenterZoom;
    }

    public static bool BlocksGestureOverVideoControls(bool controlsVisible, double yFromBottom) =>
        controlsVisible && yFromBottom <= VideoControlsDeadZoneDip;

    /// <summary>
    /// Browse swipe on video when paused/stopped (incl. before first play).
    /// Playing leaves gestures to transport; callers also exclude the bottom
    /// dead zone so seek / transport stay hittable.
    /// </summary>
    public static bool AllowsVideoBrowseSwipe(
        bool isVideo,
        bool isPlaying,
        bool touchGesturesEnabled) =>
        isVideo && !isPlaying && touchGesturesEnabled;

    /// <summary>
    /// Content X/Y under a viewport tap. When content is smaller than the
    /// viewport it is Center-aligned (offset ignored); when larger, Left/Top
    /// with scroll offset.
    /// </summary>
    public static double ContentCoordFromViewport(
        double tap,
        double offset,
        double contentSize,
        double viewportSize)
    {
        if (contentSize <= viewportSize + 0.0001)
        {
            return tap - (viewportSize - contentSize) / 2.0;
        }

        return offset + tap;
    }

    /// <summary>
    /// Target scroll offsets so <paramref name="tapX"/>/<paramref name="tapY"/>
    /// (viewport) stay under the finger when jumping to a new content size.
    /// Honors Center layout when content ≤ viewport (Fit letterbox).
    /// </summary>
    public static (double Horizontal, double Vertical) ZoomAroundPoint(
        double tapX,
        double tapY,
        double oldContentWidth,
        double oldContentHeight,
        double oldOffsetX,
        double oldOffsetY,
        double newContentWidth,
        double newContentHeight,
        double viewportWidth,
        double viewportHeight)
    {
        var contentX = ContentCoordFromViewport(
            tapX, oldOffsetX, oldContentWidth, viewportWidth);
        var contentY = ContentCoordFromViewport(
            tapY, oldOffsetY, oldContentHeight, viewportHeight);
        var scaleX = oldContentWidth > 0 ? newContentWidth / oldContentWidth : 1.0;
        var scaleY = oldContentHeight > 0 ? newContentHeight / oldContentHeight : 1.0;
        var nextX = contentX * scaleX - tapX;
        var nextY = contentY * scaleY - tapY;
        return ClampPan(
            nextX,
            nextY,
            newContentWidth,
            newContentHeight,
            viewportWidth,
            viewportHeight);
    }
}
