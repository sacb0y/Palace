namespace Palace.Helpers;

/// <summary>
/// Full-window chrome must let the glass wallpaper show through. Off WinUI.
/// Pane and cards stay ThemeResource keys — never a SolidBackgroundFillColorBase sheet.
/// </summary>
public static class ShellChrome
{
    public const string ThroughFill = "Transparent";
    public const string PaneFill = "AcrylicInAppFillColorDefaultBrush";
    public const string CardFill = "CardBackgroundFillColorDefaultBrush";
    public const string LayerFill = "LayerFillColorDefaultBrush";
    public const string NavContentBackgroundKey = "NavigationViewContentBackground";
    public const string NavContentBorderKey = "NavigationViewContentGridBorderBrush";
    public const string ExpandedPaneBackgroundKey = "NavigationViewExpandedPaneBackground";
    public const string DefaultPaneBackgroundKey = "NavigationViewDefaultPaneBackground";
    public const string OpaqueBaseFill = "SolidBackgroundFillColorBaseBrush";
    public const string PageBackgroundTheme = "ApplicationPageBackgroundThemeBrush";

    public static bool IsThroughFill(string? brush)
    {
        if (string.IsNullOrWhiteSpace(brush))
        {
            return true;
        }

        return brush.Equals("Transparent", StringComparison.OrdinalIgnoreCase)
            || brush.Equals("SystemControlTransparentBrush", StringComparison.Ordinal)
            || brush.Equals("LayerOnMicaBaseAltFillColorTransparentBrush", StringComparison.Ordinal);
    }

    public static bool IsOpaqueFullWindowFill(string? brush)
    {
        if (string.IsNullOrWhiteSpace(brush))
        {
            return false;
        }

        return brush.Equals(OpaqueBaseFill, StringComparison.Ordinal)
            || brush.Equals("SolidBackgroundFillColorBase", StringComparison.Ordinal)
            || brush.Equals(PageBackgroundTheme, StringComparison.Ordinal);
    }

    public static bool IsThemeChromeFill(string? brush) =>
        brush is PaneFill or CardFill or LayerFill;

    /// <summary>
    /// Page, NavigationView, and content presenter must be through-fills.
    /// AcrylicInAppFill as a full-window sheet (or stacked on page + nav) hides wallpaper.
    /// </summary>
    public static bool HidesWallpaper(string? pageFill, string? navFill, string? navContentFill)
    {
        foreach (var fill in new[] { pageFill, navFill, navContentFill })
        {
            if (IsOpaqueFullWindowFill(fill) || !IsThroughFill(fill))
            {
                return true;
            }
        }

        return false;
    }

    public static bool ShowsWallpaper(
        string? pageFill,
        string? navFill,
        string? navContentFill,
        string? paneFill) =>
        !HidesWallpaper(pageFill, navFill, navContentFill)
        && IsThemeChromeFill(paneFill);
}
