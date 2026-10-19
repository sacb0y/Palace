using System.Globalization;

namespace Palace.Helpers;

/// <summary>
/// Optional Palace brand accent ramp. One placeholder hex for a one-line swap later.
/// Off WinUI — App applies <see cref="Ramp"/> as SystemAccentColor* + Accent*Brush
/// inside <c>XamlControlsResources.MergedDictionaries</c> (not Application ThemeDictionaries alone).
/// </summary>
public static class BrandAccent
{
    /// <summary>Placeholder brand pink — replace this single constant when the official hex lands.</summary>
    public const string PlaceholderHex = "#E05A8C";

    public const string SettingsKey = "BrandAccentMode";
    public const string ModeFollowWindows = "FollowWindows";
    public const string ModePalacePink = "PalacePink";

    public const string FollowWindowsLabel = "Follow Windows";
    public const string PalacePinkLabel = "Palace pink";
    public const string RestartCaption = "Takes effect after restart.";

    public readonly record struct Rgb(byte R, byte G, byte B);

    public readonly record struct Ramp(
        Rgb Base,
        Rgb Light1,
        Rgb Light2,
        Rgb Light3,
        Rgb Dark1,
        Rgb Dark2,
        Rgb Dark3);

    /// <summary>
    /// WinUI AccentFill / AccentText / TextOnAccent colors for one theme.
    /// Secondary/Tertiary fills share <see cref="Fill"/> with brush Opacity 0.9 / 0.8.
    /// </summary>
    public readonly record struct ThemeAccentColors(
        Rgb Fill,
        Rgb AccentText,
        Rgb AccentTextSecondary,
        Rgb TextOnAccent);

    public static bool IsPalacePink(object? stored) =>
        stored is string text
        && text.Trim().Equals(ModePalacePink, StringComparison.OrdinalIgnoreCase);

    public static string ParseMode(object? stored) =>
        IsPalacePink(stored) ? ModePalacePink : ModeFollowWindows;

    public static Ramp BuildRamp(string? hex = null)
    {
        var seed = ParseHex(hex) ?? ParseHex(PlaceholderHex)!.Value;
        // AccentFillColorDefault (Base) must carry white TextOnAccent at ≥4.5:1 —
        // raw #E05A8C is too light, so darken the fill while keeping the seed hue
        // for Light* steps (AccentText on dark panes).
        var baseRgb = Mix(seed, Black, 0.22);
        return new Ramp(
            Base: baseRgb,
            Light1: Mix(seed, White, 0.18),
            Light2: Mix(seed, White, 0.42),
            Light3: Mix(seed, White, 0.62),
            Dark1: Mix(seed, Black, 0.28),
            Dark2: Mix(seed, Black, 0.48),
            Dark3: Mix(seed, Black, 0.65));
    }

    /// <summary>Dark (Default) theme: fills/text from Light*, ink on accent is near-black.</summary>
    public static ThemeAccentColors ForDarkTheme(Ramp ramp) =>
        new(Fill: ramp.Light2, AccentText: ramp.Light2, AccentTextSecondary: ramp.Light3, TextOnAccent: Black);

    /// <summary>Light theme: fills/text from Dark*, ink on accent is white.</summary>
    public static ThemeAccentColors ForLightTheme(Ramp ramp) =>
        new(Fill: ramp.Dark1, AccentText: ramp.Dark2, AccentTextSecondary: ramp.Dark3, TextOnAccent: White);

    /// <summary>Theme dictionary keys App writes (Default + Dark for WASDK 2.x).</summary>
    public static readonly string[] ThemeDictionaryKeys = ["Default", "Dark", "Light"];

    /// <summary>Brush keys App recreates so ThemeResource Accent*Brush rebinds after Color overrides.</summary>
    public static readonly string[] AccentBrushKeys =
    [
        "AccentFillColorDefaultBrush",
        "AccentFillColorSecondaryBrush",
        "AccentFillColorTertiaryBrush",
        "AccentTextFillColorPrimaryBrush",
        "AccentTextFillColorSecondaryBrush",
        "AccentTextFillColorTertiaryBrush",
        "TextOnAccentFillColorPrimaryBrush",
        "TextOnAccentFillColorSecondaryBrush",
        "TextOnAccentFillColorDisabledBrush",
        "NavigationViewSelectionIndicatorForeground",
    ];

    /// <summary>SystemAccentColor palette keys (base + Light/Dark 1–3) WinUI AccentFill resolves through.</summary>
    public static readonly string[] SystemAccentColorKeys =
    [
        "SystemAccentColor",
        "SystemAccentColorLight1",
        "SystemAccentColorLight2",
        "SystemAccentColorLight3",
        "SystemAccentColorDark1",
        "SystemAccentColorDark2",
        "SystemAccentColorDark3",
    ];

    /// <summary>Contrast ratio of two sRGB colors (WCAG).</summary>
    public static double ContrastRatio(Rgb a, Rgb b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static bool MeetsAaText(Rgb foreground, Rgb background) =>
        ContrastRatio(foreground, background) >= 4.5;

    /// <summary>
    /// Verify the placeholder ramp: accent text on dark/light pane fills, and
    /// text-on-accent on the base fill.
    /// </summary>
    public static bool MeetsAaGuardrails(Ramp ramp)
    {
        var darkPane = new Rgb(0x2C, 0x2C, 0x2C);
        var lightPane = new Rgb(0xF3, 0xF3, 0xF3);
        // Dark theme: AccentTextFillColorPrimary ≈ Light2 on pane.
        // Light theme: AccentTextFillColorPrimary ≈ Dark2 on pane.
        // TextOnAccentFillColorPrimary ≈ white on AccentFillColorDefault (Base).
        return MeetsAaText(ramp.Light2, darkPane)
            && MeetsAaText(ramp.Dark2, lightPane)
            && MeetsAaText(White, ramp.Base);
    }

    public static Rgb? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var text = hex.Trim();
        if (text.StartsWith('#') || text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[0] == '#' ? text[1..] : text[2..];
        }

        if (text.Length == 8)
        {
            text = text[2..];
        }

        if (text.Length != 6
            || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return new Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    private static Rgb White => new(255, 255, 255);
    private static Rgb Black => new(0, 0, 0);

    private static Rgb Mix(Rgb from, Rgb to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    private static double RelativeLuminance(Rgb c)
    {
        static double Lin(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }
}
