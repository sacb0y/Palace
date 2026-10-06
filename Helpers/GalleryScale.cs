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
    public const int KeySpace = 32;
    public const int KeyEscape = 27;
    public const double MinPinchZoom = 1.0;
    public const double MaxPinchZoom = 8.0;

    public static bool TogglesImageInfo(bool controlDown, int keyCode) =>
        controlDown && keyCode == KeyLetterD;

    public static bool TogglesDetails(bool controlDown, int keyCode) =>
        controlDown && keyCode == KeyLetterI;

    /// <summary>
    /// Space opens the overlay on the focused or selected mosaic tile.
    /// Do not steal Space while typing. While the overlay is already open,
    /// Space must reach MediaPlayer / focused chrome — do not mark it handled
    /// on the viewer root, and do not toggle-close on Space (Esc closes).
    /// </summary>
    public static bool OpensOverlay(bool overlayOpen, bool isTyping, int keyCode) =>
        !overlayOpen && !isTyping && keyCode == KeySpace;

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
    /// Mouse left-drag uses pointer capture. Touch, pen, and trackpad pan via
    /// ManipulationDelta so a contact does not pan twice.
    /// </summary>
    public static bool UsesPointerCapturePan(bool scrolls, bool isMouse, bool leftButton) =>
        scrolls && isMouse && leftButton;

    /// <summary>
    /// Learn: touchpad does not raise Manipulation events. PointerWheelChanged
    /// (often with Ctrl) is pinch; two-finger pan is wheel without Ctrl.
    /// Touch/pen still use ManipulationDelta. Mouse-without-left is the
    /// simulator, not a real precision touchpad.
    /// </summary>
    public static bool UsesManipulationGesture(
        bool isTouch,
        bool isPen,
        bool isMouse,
        bool leftButton) =>
        isTouch || isPen || (isMouse && !leftButton);

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

    public static bool UsesWheelPinch(bool controlDown, int wheelDelta) =>
        controlDown && wheelDelta != 0;

    public static bool UsesWheelPan(bool scrolls, bool controlDown, int wheelDelta) =>
        scrolls && !controlDown && wheelDelta != 0;

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
    /// Pointer delta subtracts from the current offset and clamps to the
    /// scrollable range (mouse left-drag and touch/pen drag).
    /// </summary>
    public static (double Horizontal, double Vertical) DragPan(
        double horizontalOffset,
        double verticalOffset,
        double pointerDeltaX,
        double pointerDeltaY,
        double scrollableWidth,
        double scrollableHeight) =>
        (
            ClampOffset(horizontalOffset - pointerDeltaX, scrollableWidth),
            ClampOffset(verticalOffset - pointerDeltaY, scrollableHeight));

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
    /// Keep the pinch origin on the same content point after zoom.
    /// </summary>
    public static (double Horizontal, double Vertical) PinchPan(
        double horizontalOffset,
        double verticalOffset,
        double originX,
        double originY,
        double oldZoom,
        double newZoom,
        double scrollableWidth,
        double scrollableHeight)
    {
        var prior = oldZoom > 0 ? oldZoom : 1.0;
        var next = ClampPinchZoom(newZoom);
        var scale = next / prior;
        return (
            ClampOffset((horizontalOffset + originX) * scale - originX, scrollableWidth),
            ClampOffset((verticalOffset + originY) * scale - originY, scrollableHeight));
    }

    /// <summary>
    /// One scroll target for a pinch frame: keep the pinch origin, then apply
    /// the same-frame translation. Do not DragPan from a stale offset after
    /// ChangeView — ScrollViewer has not updated HorizontalOffset yet.
    /// </summary>
    public static (double Horizontal, double Vertical) PinchThenDrag(
        double horizontalOffset,
        double verticalOffset,
        double originX,
        double originY,
        double oldZoom,
        double newZoom,
        double pointerDeltaX,
        double pointerDeltaY,
        double scrollableWidth,
        double scrollableHeight)
    {
        var (h, v) = PinchPan(
            horizontalOffset,
            verticalOffset,
            originX,
            originY,
            oldZoom,
            newZoom,
            scrollableWidth,
            scrollableHeight);
        return DragPan(h, v, pointerDeltaX, pointerDeltaY, scrollableWidth, scrollableHeight);
    }

    /// <summary>
    /// Fill starts centered (cover crop). 1:1 starts at the origin.
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

    private static double ClampOffset(double offset, double scrollable)
    {
        if (scrollable <= 0)
        {
            return 0;
        }

        if (offset < 0)
        {
            return 0;
        }

        return offset > scrollable ? scrollable : offset;
    }
}
