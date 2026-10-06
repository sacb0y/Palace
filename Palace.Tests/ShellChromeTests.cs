using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ShellChromeTests
{
    [Fact]
    public void ThroughFill_IsTransparentOrEmpty()
    {
        Assert.True(ShellChrome.IsThroughFill(null));
        Assert.True(ShellChrome.IsThroughFill(""));
        Assert.True(ShellChrome.IsThroughFill("Transparent"));
        Assert.True(ShellChrome.IsThroughFill("transparent"));
        Assert.True(ShellChrome.IsThroughFill("SystemControlTransparentBrush"));
        Assert.True(ShellChrome.IsThroughFill("LayerOnMicaBaseAltFillColorTransparentBrush"));
        Assert.False(ShellChrome.IsThroughFill(ShellChrome.PaneFill));
        Assert.False(ShellChrome.IsThroughFill(ShellChrome.OpaqueBaseFill));
        Assert.Equal("Transparent", ShellChrome.ThroughFill);
    }

    [Fact]
    public void OpaqueFullWindowFill_IsSolidBaseOrPageTheme()
    {
        Assert.True(ShellChrome.IsOpaqueFullWindowFill(ShellChrome.OpaqueBaseFill));
        Assert.True(ShellChrome.IsOpaqueFullWindowFill("SolidBackgroundFillColorBase"));
        Assert.True(ShellChrome.IsOpaqueFullWindowFill(ShellChrome.PageBackgroundTheme));
        Assert.False(ShellChrome.IsOpaqueFullWindowFill(null));
        Assert.False(ShellChrome.IsOpaqueFullWindowFill(ShellChrome.ThroughFill));
        Assert.False(ShellChrome.IsOpaqueFullWindowFill(ShellChrome.PaneFill));
        Assert.False(ShellChrome.IsOpaqueFullWindowFill(ShellChrome.CardFill));
    }

    [Fact]
    public void ThemeChromeFill_IsAcrylicLayerOrCard()
    {
        Assert.True(ShellChrome.IsThemeChromeFill(ShellChrome.PaneFill));
        Assert.True(ShellChrome.IsThemeChromeFill(ShellChrome.CardFill));
        Assert.True(ShellChrome.IsThemeChromeFill(ShellChrome.LayerFill));
        Assert.False(ShellChrome.IsThemeChromeFill(ShellChrome.ThroughFill));
        Assert.False(ShellChrome.IsThemeChromeFill(ShellChrome.OpaqueBaseFill));
        Assert.Equal("AcrylicInAppFillColorDefaultBrush", ShellChrome.PaneFill);
        Assert.Equal("NavigationViewContentBackground", ShellChrome.NavContentBackgroundKey);
    }

    [Fact]
    public void HidesWallpaper_WhenAnyFullWindowLayerCovers()
    {
        Assert.True(ShellChrome.HidesWallpaper(
            ShellChrome.PaneFill,
            ShellChrome.ThroughFill,
            ShellChrome.ThroughFill));
        Assert.True(ShellChrome.HidesWallpaper(
            ShellChrome.ThroughFill,
            ShellChrome.PaneFill,
            ShellChrome.ThroughFill));
        Assert.True(ShellChrome.HidesWallpaper(
            ShellChrome.ThroughFill,
            ShellChrome.ThroughFill,
            ShellChrome.OpaqueBaseFill));
        Assert.True(ShellChrome.HidesWallpaper(
            ShellChrome.PageBackgroundTheme,
            ShellChrome.ThroughFill,
            ShellChrome.ThroughFill));
        Assert.True(ShellChrome.HidesWallpaper(
            ShellChrome.PaneFill,
            ShellChrome.PaneFill,
            ShellChrome.PaneFill));
        Assert.False(ShellChrome.HidesWallpaper(
            ShellChrome.ThroughFill,
            ShellChrome.ThroughFill,
            ShellChrome.ThroughFill));
        Assert.False(ShellChrome.HidesWallpaper(null, null, null));
    }

    [Fact]
    public void ShowsWallpaper_WhenChromeIsThroughAndPaneIsThemeAcrylic()
    {
        Assert.True(ShellChrome.ShowsWallpaper(
            ShellChrome.ThroughFill,
            ShellChrome.ThroughFill,
            ShellChrome.ThroughFill,
            ShellChrome.PaneFill));
        Assert.False(ShellChrome.ShowsWallpaper(
            ShellChrome.ThroughFill,
            ShellChrome.ThroughFill,
            ShellChrome.ThroughFill,
            ShellChrome.OpaqueBaseFill));
        Assert.False(ShellChrome.ShowsWallpaper(
            ShellChrome.PaneFill,
            ShellChrome.ThroughFill,
            ShellChrome.ThroughFill,
            ShellChrome.PaneFill));
    }
}
