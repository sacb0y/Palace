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
            ImageScaling.Actual => "None",
            ImageScaling.Fill => "UniformToFill",
            _ => "Uniform"
        };

    public static bool Scrolls(ImageScaling scaling) =>
        scaling is ImageScaling.Actual or ImageScaling.Fill;

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
    /// Left-click drag pans like touch: pointer delta subtracts from the
    /// current offset and clamps to the scrollable range.
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
