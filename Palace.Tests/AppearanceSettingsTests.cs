using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class AppearanceSettingsTests
{
    [Fact]
    public void ParseDensity_NormalizesKnownAndFallsBack()
    {
        Assert.Equal(AppearanceSettings.DensityComfortable, AppearanceSettings.ParseDensity(null));
        Assert.Equal(AppearanceSettings.DensityCompact, AppearanceSettings.ParseDensity("compact"));
        Assert.Equal(AppearanceSettings.DensityComfortable, AppearanceSettings.ParseDensity("nope"));
        Assert.Equal(AppearanceSettings.DensityComfortable, AppearanceSettings.NormalizeDensity("Comfortable"));
    }

    [Fact]
    public void ParseAnimations_ReadsBoolOrDefault()
    {
        Assert.True(AppearanceSettings.ParseAnimations(null));
        Assert.False(AppearanceSettings.ParseAnimations(false));
        Assert.True(AppearanceSettings.ParseAnimations("true"));
        Assert.False(AppearanceSettings.ParseAnimations("False"));
    }

    [Fact]
    public void Density_CompactTightensCardChrome()
    {
        Assert.False(AppearanceSettings.IsCompact(AppearanceSettings.DensityComfortable));
        Assert.True(AppearanceSettings.IsCompact("compact"));
        Assert.Equal(56, AppearanceSettings.CardMinHeight(AppearanceSettings.DensityComfortable));
        Assert.Equal(40, AppearanceSettings.CardMinHeight(AppearanceSettings.DensityCompact));
        Assert.Equal(4, AppearanceSettings.CardSpacingBottom(AppearanceSettings.DensityComfortable));
        Assert.Equal(2, AppearanceSettings.CardSpacingBottom(AppearanceSettings.DensityCompact));
        Assert.Equal(16, AppearanceSettings.CardPaddingX(AppearanceSettings.DensityComfortable));
        Assert.Equal(12, AppearanceSettings.CardPaddingY(AppearanceSettings.DensityComfortable));
        Assert.Equal(12, AppearanceSettings.CardPaddingX(AppearanceSettings.DensityCompact));
        Assert.Equal(4, AppearanceSettings.CardPaddingY(AppearanceSettings.DensityCompact));
        // Compact vertical padding must be clearly shorter than Comfortable.
        Assert.True(
            AppearanceSettings.CardPaddingY(AppearanceSettings.DensityCompact)
            < AppearanceSettings.CardPaddingY(AppearanceSettings.DensityComfortable) / 2);
    }
}
