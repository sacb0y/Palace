using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Palace.Helpers;

public static class TagColor
{
    public static IReadOnlyList<string> Presets => TagInk.Presets;

    public static Color BlackInk { get; } = Color.FromArgb(TagInk.Black.A, TagInk.Black.R, TagInk.Black.G, TagInk.Black.B);

    public static Color WhiteInk { get; } = Color.FromArgb(TagInk.White.A, TagInk.White.R, TagInk.White.G, TagInk.White.B);

    public static bool TryParse(string? hex, out Color color)
    {
        if (!TagInk.TryParse(hex, out var rgba))
        {
            color = default;
            return false;
        }

        color = Color.FromArgb(rgba.A, rgba.R, rgba.G, rgba.B);
        return true;
    }

    public static string ToHex(Color color) =>
        TagInk.ToHex(new TagInk.Rgba(color.A, color.R, color.G, color.B));

    public static string? Normalize(string? hex) =>
        TryParse(hex, out var color) ? ToHex(color) : null;

    public static bool HasColor(string? hex) => TagInk.TryParse(hex, out _);

    /// <summary>
    /// Solid fill for a colored tag. Uncolored chips use <c>PalaceTagChipFillBrush</c>
    /// from App ThemeDictionaries so Light/Dark follow the page theme (not app-theme white-on-white).
    /// </summary>
    public static Brush ChipFill(string? hex) =>
        TryParse(hex, out var color)
            ? new SolidColorBrush(color)
            : ThemeBrush("PalaceTagChipFillBrush", "ControlFillColorDefaultBrush");

    /// <summary>
    /// Contrasting ink for a colored tag, or theme primary text when uncolored.
    /// </summary>
    public static Brush ChipInk(string? hex) =>
        TryParse(hex, out var color)
            ? new SolidColorBrush(ContrastingInk(color))
            : ThemeBrush("PalaceTagChipInkBrush", "TextFillColorPrimaryBrush");

    public static double ChipOpacity(bool isPartial) =>
        AssignedTagChrome.Opacity(isPartial, Models.TagSource.Manual);

    public static double ChipOpacity(bool isPartial, Models.TagSource source) =>
        AssignedTagChrome.Opacity(isPartial, source);

    /// <summary>
    /// Black or white ink by maximum WCAG contrast (equal at L ≈ 0.179). Soft black matches the Fluent text ramp.
    /// </summary>
    public static Color ContrastingInk(Color fill)
    {
        var ink = TagInk.Contrasting(new TagInk.Rgba(fill.A, fill.R, fill.G, fill.B));
        return Color.FromArgb(ink.A, ink.R, ink.G, ink.B);
    }

    public static double RelativeLuminance(Color fill) =>
        TagInk.RelativeLuminance(new TagInk.Rgba(fill.A, fill.R, fill.G, fill.B));

    private static Brush ThemeBrush(string preferredKey, string fallbackKey)
    {
        if (Application.Current?.Resources.TryGetValue(preferredKey, out var preferred) == true
            && preferred is Brush preferredBrush)
        {
            return preferredBrush;
        }

        if (Application.Current?.Resources.TryGetValue(fallbackKey, out var fallback) == true
            && fallback is Brush fallbackBrush)
        {
            return fallbackBrush;
        }

        return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    }
}
