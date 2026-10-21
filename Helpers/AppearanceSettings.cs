namespace Palace.Helpers;

/// <summary>
/// Appearance density + animations prefs (LocalSettings). Off WinUI for Palace.Tests.
/// Compact tightens Settings card min-height and bottom spacing (0.3.0 polish).
/// </summary>
public static class AppearanceSettings
{
    public const string DensityKey = "AppearanceDensity";
    public const string AnimationsKey = "AppearanceAnimations";

    public const string DensityComfortable = "Comfortable";
    public const string DensityCompact = "Compact";
    public const string DefaultDensity = DensityComfortable;
    public const bool DefaultAnimations = true;

    public const double ComfortableCardMinHeight = 40;
    public const double CompactCardMinHeight = 32;
    public const double ComfortableCardSpacingBottom = 4;
    public const double CompactCardSpacingBottom = 2;

    public static readonly string[] DensityOptions =
    [
        DensityComfortable,
        DensityCompact
    ];

    public static string NormalizeDensity(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return DefaultDensity;
        }

        var text = stored.Trim();
        return DensityOptions.Any(o => o.Equals(text, StringComparison.OrdinalIgnoreCase))
            ? DensityOptions.First(o => o.Equals(text, StringComparison.OrdinalIgnoreCase))
            : DefaultDensity;
    }

    public static string ParseDensity(object? stored) => stored switch
    {
        string text => NormalizeDensity(text),
        _ => DefaultDensity
    };

    public static bool IsCompact(string? density) =>
        NormalizeDensity(density) == DensityCompact;

    public static double CardMinHeight(string? density) =>
        IsCompact(density) ? CompactCardMinHeight : ComfortableCardMinHeight;

    public static double CardSpacingBottom(string? density) =>
        IsCompact(density) ? CompactCardSpacingBottom : ComfortableCardSpacingBottom;

    public static bool ParseAnimations(object? stored) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => DefaultAnimations
    };
}
