using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Palace.Helpers;

public static class TagColor
{
    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var text = hex.Trim();
        if (text.StartsWith('#') || text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[0] == '#' ? text[1..] : text[2..];
        }

        if (text.Length == 6)
        {
            text = "FF" + text;
        }

        if (text.Length != 8 ||
            !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = Color.FromArgb(
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value);
        return true;
    }

    public static string ToHex(Color color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public static string? Normalize(string? hex) =>
        TryParse(hex, out var color) ? ToHex(color) : null;

    public static Brush ChipFill(string? hex) =>
        TryParse(hex, out var color)
            ? new SolidColorBrush(color)
            : ThemeBrush("CardBackgroundFillColorDefaultBrush");

    public static Brush ChipInk(string? hex) =>
        TryParse(hex, out var color)
            ? new SolidColorBrush(ContrastingInk(color))
            : ThemeBrush("TextFillColorPrimaryBrush");

    public static double ChipOpacity(bool isPartial) => isPartial ? 0.75 : 1.0;

    public static Color ContrastingInk(Color fill)
    {
        var luminance = 0.2126 * Linear(fill.R) + 0.7152 * Linear(fill.G) + 0.0722 * Linear(fill.B);
        return luminance > 0.55
            ? Color.FromArgb(255, 0, 0, 0)
            : Color.FromArgb(255, 255, 255, 255);
    }

    private static double Linear(byte channel)
    {
        var srgb = channel / 255.0;
        return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    private static Brush ThemeBrush(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    }
}
