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
        scaling == ImageScaling.Actual;
}
