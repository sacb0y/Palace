using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public class SdrMapPeakTests
{
    [Fact]
    public void SdrMapPeak_KeepsMaxCllForTypicalHdrStill()
    {
        // HDR PNG: MaxCLL 717 nits, P99 ~400 nits -> 2x P99 is above MaxCLL.
        Assert.Equal(717f, GalleryPresent.SdrMapPeakNits(717f, 400f), 3);
    }

    [Fact]
    public void SdrMapPeak_CapsExtremeHighlightAtTwiceP99()
    {
        // 360 panorama: sun ~313 000 nits, P99 ~150 nits.
        Assert.Equal(300f, GalleryPresent.SdrMapPeakNits(313_000f, 150f), 3);
        var scale = GalleryPresent.PresentMap(false, GalleryPresent.SdrMapPeakNits(313_000f, 150f), 80f).Scale;
        Assert.True(scale > 0.25f, $"scale {scale} would crush the frame");
    }

    [Fact]
    public void SdrMapPeak_FloorsAtSdrWhiteAndPassesThroughUnknown()
    {
        Assert.Equal(GalleryPresent.ScrgbNits, GalleryPresent.SdrMapPeakNits(5000f, 10f), 3);
        Assert.Equal(5000f, GalleryPresent.SdrMapPeakNits(5000f, 0f), 3);
        Assert.Equal(5000f, GalleryPresent.SdrMapPeakNits(5000f, float.NaN), 3);
        Assert.Equal(0f, GalleryPresent.SdrMapPeakNits(0f, 100f), 3);
    }

    [Fact]
    public void P99MaxRgb_IgnoresTheTopPercent()
    {
        // 1000 pixels: 990 at scRGB 1.0 (80 nits), 10 at 1000.0 (80 000 nits).
        var rgba = new float[1000 * 4];
        for (var i = 0; i < 1000; i++)
        {
            var v = i < 990 ? 1f : 1000f;
            rgba[i * 4] = v * 0.5f;
            rgba[i * 4 + 1] = v;
            rgba[i * 4 + 2] = float.NaN;
            rgba[i * 4 + 3] = 1f;
        }

        Assert.Equal(80f, GalleryPresent.P99MaxRgbNits(rgba, 1000, 1), 3);
        Assert.Equal(0f, GalleryPresent.P99MaxRgbNits(rgba, 2000, 1));
        Assert.Equal(0f, GalleryPresent.P99MaxRgbNits([], 0, 0));
    }
}