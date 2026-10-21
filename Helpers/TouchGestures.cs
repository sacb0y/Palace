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
    public const double DoubleTapActualMinFit = 1.25;
    public const double DoubleTapFallbackFit = 2.5;
    public const double VideoSeekSeconds = 10;
    public const double VideoControlsDeadZoneDip = 72;

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
    /// Mosaic long-press enters multi-select. Overlay long-press toggles the
    /// current item's selection (check badge). Video long-press is select, not 2×.
    /// </summary>
    public static bool MosaicHoldEntersSelect(bool selectMode, bool overlayOpen, bool isFolderHeader) =>
        SelectMode.EnterFromHold(selectMode, overlayOpen, isFolderHeader);

    public static bool ViewerHoldTogglesSelection(bool overlayOpen) => overlayOpen;

    /// <summary>
    /// Context menu is "…" / right-click only — not touch long-press.
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
    /// Pinch min while dragging: 0.5× Fit. On release snap back to Fit when below 1.
    /// </summary>
    public static double ClampPinchDuringGesture(double zoomFitFactor) =>
        Math.Max(PinchMinFitFactor, zoomFitFactor);

    public static double SnapPinchOnRelease(double zoomFitFactor) =>
        zoomFitFactor < 1.0 - 0.0001 ? 1.0 : zoomFitFactor;

    /// <summary>
    /// Hard max = max(8× actual-pixel scale, 4× Fit). Soft rubber to 1.15× past.
    /// <paramref name="fitFactor"/> is zoom relative to Fit (1 = Fit).
    /// <paramref name="actualFactor"/> is zoom relative to 1:1 pixels.
    /// </summary>
    public static double MaxPinchFitFactor(double fitToActualRatio)
    {
        // fitToActualRatio = how many Fit-units equal 1:1 (Actual/Fit).
        // 8× actual in Fit units = 8 * (Actual/Fit) = 8 / fitToActual when
        // fitToActual = Fit/Actual. Prefer: max(8 * actualAsFit, 4).
        var eightActualAsFit = PinchMaxActualFactor * Math.Max(0, fitToActualRatio);
        return Math.Max(eightActualAsFit, PinchMaxFitFactor);
    }

    public static double ClampPinchHard(double fitFactor, double fitToActualRatio)
    {
        var max = MaxPinchFitFactor(fitToActualRatio);
        if (fitFactor < PinchMinFitFactor)
        {
            return PinchMinFitFactor;
        }

        return fitFactor > max ? max : fitFactor;
    }

    public static double ClampPinchSoft(double fitFactor, double fitToActualRatio)
    {
        var hard = MaxPinchFitFactor(fitToActualRatio);
        var softMax = hard * PinchSoftOvershoot;
        if (fitFactor < PinchMinFitFactor)
        {
            return PinchMinFitFactor;
        }

        return fitFactor > softMax ? softMax : fitFactor;
    }

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
    /// Target scroll offsets so <paramref name="tapX"/>/<paramref name="tapY"/>
    /// (viewport) stay under the finger when jumping to a new content size.
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
        var contentX = oldOffsetX + tapX;
        var contentY = oldOffsetY + tapY;
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
