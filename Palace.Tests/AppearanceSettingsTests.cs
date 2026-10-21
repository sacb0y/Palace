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
}
