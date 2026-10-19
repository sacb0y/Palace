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

    [Fact]
    public void ThemeAccentColors_TextOnAccentBeatsAccentTextOnFill_InLight()
    {
        var ramp = BrandAccent.BuildRamp();
        var light = BrandAccent.ForLightTheme(ramp);
        // Light tree bug: AccentText (Dark2) on Fill (Dark1) is dark-on-mid.
        Assert.True(
            BrandAccent.ContrastRatio(light.AccentText, light.Fill) < 4.5,
            "AccentText on Fill must stay the failing Light combo we avoid in UI");
        Assert.True(
            BrandAccent.MeetsAaText(light.TextOnAccent, light.Fill),
            "TextOnAccent on Default fill must pass AA for Light selected rows");

        var dark = BrandAccent.ForDarkTheme(ramp);
        Assert.True(BrandAccent.MeetsAaText(dark.TextOnAccent, dark.Fill));
        Assert.True(BrandAccent.MeetsAaText(dark.AccentText, new BrandAccent.Rgb(0x2C, 0x2C, 0x2C)));
    }

    [Fact]
    public void AccentBrushKeys_CoverFillTextAndOnAccent()
    {
        Assert.Contains("AccentFillColorDefaultBrush", BrandAccent.AccentBrushKeys);
        Assert.Contains("AccentTextFillColorPrimaryBrush", BrandAccent.AccentBrushKeys);
        Assert.Contains("TextOnAccentFillColorPrimaryBrush", BrandAccent.AccentBrushKeys);
        Assert.Equal(9, BrandAccent.AccentBrushKeys.Length);
    }
}
