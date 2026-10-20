namespace Palace.Helpers;

/// <summary>
/// Appearance density + animations prefs (LocalSettings). Off WinUI for Palace.Tests.
/// Visual density polish lands in Settings PR 7; this PR wires the model + Settings UI.
/// </summary>
public static class AppearanceSettings
{
    public const string DensityKey = "AppearanceDensity";
    public const string AnimationsKey = "AppearanceAnimations";

    public const string DensityComfortable = "Comfortable";
    public const string DensityCompact = "Compact";
    public const string DefaultDensity = DensityComfortable;
    public const bool DefaultAnimations = true;

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

    public static bool ParseAnimations(object? stored) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => DefaultAnimations
    };
}
