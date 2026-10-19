using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagColorTests
{
    [Theory]
    [InlineData("#EAB308")] // HDR amber — was white ink (1.9:1), now black
    [InlineData("#F97316")] // Sunset
    [InlineData("#3B82F6")] // Sky
    [InlineData("#E05A8C")] // Mira / brand pink
    [InlineData("#22C55E")]
    [InlineData("#EF4444")]
    public void ContrastingInk_PicksBlackOnMidLuminanceFills(string hex)
    {
        Assert.True(TagInk.TryParse(hex, out var fill));
        var ink = TagInk.Contrasting(fill);
        Assert.Equal(0, ink.R);
        Assert.Equal(0, ink.G);
        Assert.Equal(0, ink.B);
        Assert.True(ink.A >= 0xE6);
    }

    [Theory]
    [InlineData("#6D28D9")] // Space violet — stays white
    [InlineData("#64748B")] // slate preset
    [InlineData("#000000")]
    public void ContrastingInk_PicksWhiteOnDarkFills(string hex)
    {
        Assert.True(TagInk.TryParse(hex, out var fill));
        var ink = TagInk.Contrasting(fill);
        Assert.Equal(255, ink.R);
        Assert.Equal(255, ink.G);
        Assert.Equal(255, ink.B);
    }

    [Fact]
    public void Presets_AllReachAaWithChosenInk()
    {
        foreach (var hex in TagInk.Presets)
        {
            Assert.True(TagInk.TryParse(hex, out var fill));
            var ink = TagInk.Contrasting(fill);
            var ratio = TagInk.ContrastRatio(fill, ink);
            Assert.True(ratio >= 4.5, $"{hex} with ink {TagInk.ToHex(ink)} is {ratio:F2}:1");
        }
    }

    [Fact]
    public void EqualContrastCrossover_PicksEitherInkNearPointOneSevenNine()
    {
        // White and black have equal contrast at L ≈ 0.179.
        var gray = new TagInk.Rgba(255, 118, 118, 118);
        var ink = TagInk.Contrasting(gray);
        Assert.True(ink.R == 0 || ink.R == 255);
    }
}
