using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class BrandAccentTests
{
    [Fact]
    public void PlaceholderHex_IsSingleSwapConstant()
    {
        Assert.Equal("#E05A8C", BrandAccent.PlaceholderHex);
        Assert.NotNull(BrandAccent.ParseHex(BrandAccent.PlaceholderHex));
    }

    [Fact]
    public void ParseMode_DefaultsToFollowWindows()
    {
        Assert.Equal(BrandAccent.ModeFollowWindows, BrandAccent.ParseMode(null));
        Assert.Equal(BrandAccent.ModeFollowWindows, BrandAccent.ParseMode("nope"));
        Assert.Equal(BrandAccent.ModePalacePink, BrandAccent.ParseMode("PalacePink"));
        Assert.True(BrandAccent.IsPalacePink("palacepink"));
        Assert.False(BrandAccent.IsPalacePink(BrandAccent.ModeFollowWindows));
    }

    [Fact]
    public void BuildRamp_MeetsAaGuardrails()
    {
        var ramp = BrandAccent.BuildRamp();
        Assert.True(BrandAccent.MeetsAaGuardrails(ramp), "Palace pink ramp must meet AA on pane fills");
        Assert.True(BrandAccent.ContrastRatio(ramp.Light2, new BrandAccent.Rgb(0x2C, 0x2C, 0x2C)) >= 4.5);
        Assert.True(BrandAccent.ContrastRatio(ramp.Dark2, new BrandAccent.Rgb(0xF3, 0xF3, 0xF3)) >= 4.5);
    }
}
