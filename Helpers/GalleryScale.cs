namespace Palace.Helpers;

public enum ImageScaling
{
    Fit = 0,
    Actual = 1,
    Fill = 2
}

/// <summary>
/// Overlay / gallery still scaling (SKIV 1:1 / Fit / Fill). Stays off WinUI.
/// </summary>
public static class GalleryScale
{
    public const int KeyNumber0 = 48;
    public const int KeyNumber1 = 49;
    public const int KeyNumber2 = 50;
    public const int KeyNumber3 = 51;
    public const int KeyNumberPad0 = 96;
    public const int KeyNumberPad1 = 97;
    public const int KeyNumberPad2 = 98;
    public const int KeyNumberPad3 = 99;
    public const int KeyLetterD = 68;
    public const int KeyLetterI = 73;
    public const int KeyLetterH = 72;
    public const int KeySpace = 32;
    public const int KeyEscape = 27;
    public const int KeyEnter = 13;
    public const int KeyF6 = 117;
    public const double MinPinchZoom = 1.0;
    public const double MaxPinchZoom = 8.0;

    /// <summary>Ctrl+D aliases the Info / Details card (same as bare <c>I</c>).</summary>
    public static bool TogglesImageInfo(bool controlDown, int keyCode) =>
        controlDown && keyCode == KeyLetterD;

    /// <summary>
    /// Details / Info card: bare <c>I</c> or <c>Alt+Enter</c> (no Ctrl). Ctrl+D is
    /// the chord alias via <see cref="TogglesImageInfo"/>.
    /// </summary>
    public static bool TogglesDetails(bool controlDown, bool altDown, int keyCode)
    {
        if (controlDown)
        {
            return false;
        }

        if (keyCode == KeyLetterI && !altDown)
        {
            return true;
        }

        return altDown && keyCode == KeyEnter;
    }

    /// <summary>Backward-compatible overload — treats Alt as up.</summary>
    public static bool TogglesDetails(bool controlDown, int keyCode) =>
        TogglesDetails(controlDown, altDown: false, keyCode);

    /// <summary>Ctrl+H: luminance heatmap view (SKIV's visualization cycle key).</summary>
    public static bool TogglesHeatmap(bool controlDown, int keyCode) =>
        controlDown && keyCode == KeyLetterH;

    /// <summary>Esc closes the Details panel first while it is open.</summary>
    public static bool ClosesDetailsFirst(bool detailsOpen, int keyCode) =>
        detailsOpen && keyCode == KeyEscape;

    /// <summary>F6 cycles image / chrome controls / Details card.</summary>
    public static bool CyclesViewerFocus(int keyCode) =>
        keyCode == KeyF6;

    /// <summary>
    /// Space opens the overlay on the focused or selected mosaic tile.
    /// Do not steal Space while typing. While the overlay is already open,
    /// Space must reach MediaPlayer / focused chrome — do not mark it handled
    /// on the viewer root, and do not toggle-close on Space (Esc closes).
    /// </summary>
    public static bool OpensOverlay(bool overlayOpen, bool isTyping, int keyCode) =>
        !overlayOpen && !isTyping && keyCode == KeySpace;

    /// <summary>
    /// Enter opens the overlay on the focused or selected mosaic tile (same
    /// path as ItemsView ItemInvoked / UIA Invoke when selection mode is on).
    /// </summary>
    public static bool OpensOverlayFromEnter(bool overlayOpen, bool isTyping, int keyCode) =>
        !overlayOpen && !isTyping && keyCode == KeyEnter;

    public static bool ClosesOverlay(bool overlayOpen, int keyCode) =>
        overlayOpen && keyCode == KeyEscape;

    /// <summary>
    /// Overlay / GalleryWindow PreviewKeyDown must not mark Space handled
    /// so MediaPlayer play/pause and focused chrome receive it.
    /// </summary>
    public static bool PassesViewerSpace(bool overlayOpen, int keyCode) =>
        overlayOpen && keyCode == KeySpace;

    public static ImageScaling Cycle(ImageScaling current) =>
        current switch
        {
            ImageScaling.Fit => ImageScaling.Fill,
            ImageScaling.Fill => ImageScaling.Actual,
            _ => ImageScaling.Fit
        };

    public static ImageScaling? FromDigit(int digit) =>
        digit switch
        {
            1 => ImageScaling.Actual,
            0 or 2 => ImageScaling.Fit,
            3 => ImageScaling.Fill,
            _ => null
        };

    public static ImageScaling? FromKeyCode(int keyCode)
    {
        if (keyCode is >= KeyNumber0 and <= KeyNumber3)
        {
            return FromDigit(keyCode - KeyNumber0);
        }

        if (keyCode is >= KeyNumberPad0 and <= KeyNumberPad3)
        {
            return FromDigit(keyCode - KeyNumberPad0);
        }

        return null;
    }

    public static string Label(ImageScaling scaling) =>
        scaling switch
        {
            ImageScaling.Actual => "1:1",
            ImageScaling.Fill => "Fill",
            _ => "Fit"
        };

    public static string StretchName(ImageScaling scaling) =>
        scaling switch
        {
            ImageScaling.Actual => "Uniform",
            ImageScaling.Fill => "UniformToFill",
            _ => "Uniform"
        };

    public static bool Scrolls(ImageScaling scaling, double pinchZoom = 1.0) =>
        scaling is ImageScaling.Actual or ImageScaling.Fill
        || pinchZoom > MinPinchZoom + 0.0001;

    /// <summary>
    /// Layout size so one image pixel is one device pixel. At 150% DPI a
    /// 200×100 image is ~133×67 DIPs, not 200×100 DIPs (which stretches).
    /// </summary>
    public static (double Width, double Height) ActualDipSize(
        int pixelWidth,
        int pixelHeight,
        double rasterScale)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return (double.NaN, double.NaN);
        }

        var scale = rasterScale > 0 ? rasterScale : 1.0;
        return (pixelWidth / scale, pixelHeight / scale);
    }

    /// <summary>
    /// Cover the viewport (crop overflow). One side matches the view; the
    /// other is larger so Fill can pan. Same aspect as the oriented image.
    /// </summary>
    public static (double Width, double Height) FillCoverDipSize(
        int pixelWidth,
        int pixelHeight,
        double viewportWidth,
        double viewportHeight)
    {
        if (viewportWidth <= 1 || viewportHeight <= 1)
        {
            return (double.NaN, double.NaN);
        }

        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return (viewportWidth, viewportHeight);
        }

        var imageAspect = (double)pixelWidth / pixelHeight;
        var viewAspect = viewportWidth / viewportHeight;
        if (imageAspect > viewAspect)
        {
            return (viewportHeight * imageAspect, viewportHeight);
        }

        return (viewportWidth, viewportWidth / imageAspect);
    }

    /// <summary>
    /// Letterbox the oriented image into the viewport (Uniform Fit). Panel
    /// matches the <b>image content rect</b>, not the full viewport — so HDR
    /// SwapChainPanel black letterbox is not part of pinch/pan bounds.
    /// </summary>
    public static (double Width, double Height) FitContainDipSize(
        int pixelWidth,
        int pixelHeight,
        double viewportWidth,
        double viewportHeight)
    {
        if (viewportWidth <= 1 || viewportHeight <= 1)
        {
            return (double.NaN, double.NaN);
        }

        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return (viewportWidth, viewportHeight);
        }

        var imageAspect = (double)pixelWidth / pixelHeight;
        var viewAspect = viewportWidth / viewportHeight;
        if (imageAspect > viewAspect)
        {
            // Wider than view → width matches; height letterboxed.
            return (viewportWidth, viewportWidth / imageAspect);
        }

        return (viewportHeight * imageAspect, viewportHeight);
    }

    /// <summary>
    /// Mouse left-drag uses pointer capture. Touch, pen, and trackpad pan via
    /// ManipulationDelta so a contact does not pan twice.
    /// </summary>
    public static bool UsesPointerCapturePan(bool scrolls, bool isMouse, bool leftButton) =>
        scrolls && isMouse && leftButton;

    /// <summary>
    /// Learn: touchpad does not raise Manipulation events. PointerWheelChanged
    /// (often with Ctrl) is pinch; two-finger pan is wheel without Ctrl.
    /// Touch/pen still use ManipulationDelta. Mouse-without-left is the
    /// simulator, not a real precision touchpad. Honors
    /// <see cref="ViewerSettings.TouchGesturesEnabled"/>.
    /// </summary>
    public static bool UsesManipulationGesture(
        bool isTouch,
        bool isPen,
        bool isMouse,
        bool leftButton) =>
        UsesManipulationGesture(
            isTouch,
            isPen,
            isMouse,
            leftButton,
            ViewerSettings.TouchGesturesEnabled);

    /// <summary>
    /// When <paramref name="touchGesturesEnabled"/> is false, touch / pen /
    /// trackpad Manipulation is off; mouse left-drag pan stays on via pointer
    /// capture.
    /// </summary>
    public static bool UsesManipulationGesture(
        bool isTouch,
        bool isPen,
        bool isMouse,
        bool leftButton,
        bool touchGesturesEnabled)
    {
        if (!ViewerSettings.AllowsTouchGestures(touchGesturesEnabled))
        {
            return false;
        }

        return isTouch || isPen || (isMouse && !leftButton);
    }

    /// <summary>
    /// Learn: call CancelDirectManipulations on the element inside a
    /// ScrollViewer so pointer/manipulation events are not marked handled
    /// after the first DirectManipulation pan/zoom. Ignore inertial frames.
    /// </summary>
    public static bool HandlesManipulationDelta(
        bool isInertial,
        bool isTouch,
        bool isPen,
        bool isMouse,
        bool leftButton) =>
        !isInertial && UsesManipulationGesture(isTouch, isPen, isMouse, leftButton);

    /// <summary>
    /// Auto bars turn on DirectManipulation. Disabled also blocks ChangeView
    /// pan. Hidden keeps programmatic pan without showing bars.
    /// </summary>
    public static bool ShowsScrollBars() => false;

    /// <summary>
    /// Recenter / drop pinch when the still, 1:1/Fit/Fill mode, or viewport
    /// changes so a prior pan does not stick.
    /// </summary>
    public static bool ShouldResetView(
        bool stillChanged,
        bool scalingChanged,
        bool viewportChanged) =>
        stillChanged || scalingChanged || viewportChanged;

    /// <summary>
    /// Sticky scale after open/navigate (user's kept zoom). Keep-zoom on: carry
    /// Actual/Fill. Keep-zoom off: <paramref name="defaultZoom"/>. Never forces
    /// Fit for HDR — use <see cref="DisplayScaling"/> for on-screen Fit.
    /// </summary>
    public static ImageScaling StickyScalingAfterStillOpen(
        ImageScaling current,
        bool keepZoomBetweenImages,
        ImageScaling defaultZoom) =>
        keepZoomBetweenImages
            ? current
            : ViewerSettings.NormalizeZoom(defaultZoom);

    /// <summary>
    /// On-screen scale for the current still. HDR presentable stills show Fit
    /// without overwriting the sticky kept zoom.
    /// </summary>
    public static ImageScaling DisplayScaling(ImageScaling sticky, bool hdrPresentable) =>
        hdrPresentable ? ImageScaling.Fit : sticky;

    /// <summary>
    /// Fit at pinch 1 must clear translation (content equals viewport). Actual/Fill
    /// keep pan offset after release — otherwise 1:1 pans snap top-left and Fill
    /// loses its centered cover crop.
    /// </summary>
    public static bool ClearsScrollOnPinchRelease(ImageScaling scaling, double pinchZoom) =>
        scaling == ImageScaling.Fit && pinchZoom <= MinPinchZoom + 0.0001;

    /// <summary>
    /// Open landed at Fit with no leftover pinch or scroll offset (HDR path).
    /// </summary>
    public static bool IsOpenAtFit(
        ImageScaling scaling,
        double pinchZoom,
        double horizontalOffset = 0,
        double verticalOffset = 0) =>
        scaling == ImageScaling.Fit
        && pinchZoom <= MinPinchZoom + 0.0001
        && Math.Abs(horizontalOffset) < 0.5
        && Math.Abs(verticalOffset) < 0.5;

    public static bool UsesWheelPinch(bool controlDown, int wheelDelta) =>
        controlDown && wheelDelta != 0;

    public static bool UsesWheelPan(bool scrolls, bool controlDown, int wheelDelta) =>
        scrolls && !controlDown && wheelDelta != 0;

    /// <summary>
    /// Hidden ScrollViewer marks PointerWheelChanged handled before a parent
    /// Grid hears it. Listen with handledEventsToo when bars are Hidden.
    /// </summary>
    public static bool NeedsHandledWheelListener(bool hidesScrollBars) =>
        hidesScrollBars;

    /// <summary>
    /// One wheel notch (120) is a 1.1× pinch step. Negative delta pinches out.
    /// </summary>
    public static double WheelPinchFactor(int mouseWheelDelta)
    {
        if (mouseWheelDelta == 0)
        {
            return 1.0;
        }

        return Math.Pow(1.1, mouseWheelDelta / 120.0);
    }

    /// <summary>
    /// Wheel delta as a DragPan pointer delta (subtracted from offset).
    /// </summary>
    public static double WheelToPanDelta(int mouseWheelDelta) =>
        mouseWheelDelta / 4.0;

    /// <summary>
    /// Mouse left-drag and touch/pen contact pan when the still can scroll.
    /// Trackpad two-finger pan is ManipulationDelta / wheel, not this.
    /// </summary>
    public static bool UsesDragPan(
        bool scrolls,
        bool isMouse,
        bool leftButton,
        bool isTouch,
        bool isPen)
    {
        if (!scrolls)
        {
            return false;
        }

        if (isMouse)
        {
            return leftButton;
        }

        return isTouch || isPen;
    }

    /// <summary>
    /// Pan with per-axis content/viewport clamp (larger: both edges hard;
    /// smaller: centered). Mouse, touch, and wheel pan when zoomed.
    /// </summary>
    public static (double Horizontal, double Vertical) DragPanContent(
        double horizontalOffset,
        double verticalOffset,
        double pointerDeltaX,
        double pointerDeltaY,
        double contentWidth,
        double contentHeight,
        double viewportWidth,
        double viewportHeight) =>
        TouchGestures.ClampPan(
            horizontalOffset - pointerDeltaX,
            verticalOffset - pointerDeltaY,
            contentWidth,
            contentHeight,
            viewportWidth,
            viewportHeight);

    public static double ClampPinchZoom(double zoom) =>
        Math.Clamp(zoom, MinPinchZoom, MaxPinchZoom);

    /// <summary>
    /// Multiplicative pinch. <paramref name="scaleDelta"/> is the frame
    /// scale (1 = unchanged). Clamped to <see cref="MinPinchZoom"/>–
    /// <see cref="MaxPinchZoom"/>.
    /// </summary>
    public static double PinchZoom(double currentZoom, double scaleDelta)
    {
        var factor = scaleDelta > 0 ? scaleDelta : 1.0;
        return ClampPinchZoom(currentZoom * factor);
    }

    public static (double Width, double Height) ApplyPinchZoom(
        double width,
        double height,
        double zoom)
    {
        if (double.IsNaN(width) || double.IsNaN(height))
        {
            return (width, height);
        }

        var z = ClampPinchZoom(zoom);
        return (width * z, height * z);
    }

    /// <summary>
    /// Fill starts centered (cover crop). Fit / Actual: offset 0 — when
    /// content is smaller than the viewport, layout uses Center alignment
    /// (see still surface); when larger, Left/Top + pan clamp.
    /// </summary>
    public static (double Horizontal, double Vertical) InitialScrollOffset(
        ImageScaling scaling,
        double contentWidth,
        double contentHeight,
        double viewportWidth,
        double viewportHeight) =>
        scaling == ImageScaling.Fill
            ? CoverCenterOffset(contentWidth, contentHeight, viewportWidth, viewportHeight)
            : (0, 0);

    public static (double Horizontal, double Vertical) CoverCenterOffset(
        double contentWidth,
        double contentHeight,
        double viewportWidth,
        double viewportHeight)
    {
        if (contentWidth <= 0 || contentHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return (0, 0);
        }

        return (
            Math.Max(0, (contentWidth - viewportWidth) / 2.0),
            Math.Max(0, (contentHeight - viewportHeight) / 2.0));
    }
}
