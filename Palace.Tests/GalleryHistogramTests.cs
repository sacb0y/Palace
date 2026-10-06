using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class GalleryHistogramTests
{
    [Fact]
    public void ShouldBuild_SkipsOnlineOnlyApiOrphanThumb()
    {
        Assert.True(GalleryHistogram.ShouldBuild(true, false, false, false, true));
        Assert.False(GalleryHistogram.ShouldBuild(true, true, false, false, true));
        Assert.False(GalleryHistogram.ShouldBuild(true, false, true, false, true));
        Assert.False(GalleryHistogram.ShouldBuild(true, false, false, true, true));
        Assert.False(GalleryHistogram.ShouldBuild(true, false, false, false, false));
        Assert.False(GalleryHistogram.ShouldBuild(false, false, false, false, true));
    }

    [Fact]
    public void SampleStride_CapsLargeFrames()
    {
        Assert.Equal(1, GalleryHistogram.SampleStride(100, 256));
        Assert.Equal(2, GalleryHistogram.SampleStride(4, 2));
        Assert.Equal(4, GalleryHistogram.SampleStride(1_000_000, 250_000));
    }

    [Fact]
    public void LogNitsBin_PaperWhiteSitsBetweenSdrAndHighlights()
    {
        var paper = GalleryHistogram.LogNitsBin(GalleryPresent.SdrReferenceNits);
        var sdr = GalleryHistogram.LogNitsBin(GalleryPresent.ScrgbNits);
        var bright = GalleryHistogram.LogNitsBin(1_000f);
        var peak = GalleryHistogram.LogNitsBin(GalleryHistogram.LumaMaxNits);
        Assert.InRange(paper, 1, GalleryHistogram.BinCount - 2);
        Assert.True(sdr < paper);
        Assert.True(paper < bright);
        Assert.Equal(GalleryHistogram.BinCount - 1, peak);
        Assert.Equal(0, GalleryHistogram.LogNitsBin(0));
        Assert.Equal(0, GalleryHistogram.LogNitsBin(-1));
    }

    [Fact]
    public void LinearScrgbBin_ClipsNegativesAndPqPeak()
    {
        Assert.Equal(0, GalleryHistogram.LinearScrgbBin(-0.5f));
        Assert.Equal(0, GalleryHistogram.LinearScrgbBin(0));
        Assert.Equal(GalleryHistogram.BinCount - 1, GalleryHistogram.LinearScrgbBin(GalleryHistogram.ChannelMaxScrgb));
        Assert.Equal(GalleryHistogram.BinCount - 1, GalleryHistogram.LinearScrgbBin(200f));
        var mid = GalleryHistogram.LinearScrgbBin(GalleryHistogram.ChannelMaxScrgb * 0.5f);
        Assert.InRange(mid, 30, 33);
    }

    [Fact]
    public void FromScrgb_UniformGrayLandsInOneLumaBin()
    {
        var rgba = ScrgbPixel(1f, 1f, 1f);
        var bins = GalleryHistogram.FromScrgb(rgba, 1, 1, bt2020: false);
        var expected = GalleryHistogram.LogNitsBin(GalleryPresent.ScrgbNits);
        Assert.Equal(1, bins.SampleCount);
        Assert.Equal(1, bins.Luma[expected]);
        Assert.Equal(1, bins.Luma.Sum());
        Assert.Equal(1, bins.Red[GalleryHistogram.LinearScrgbBin(1f)]);
    }

    [Fact]
    public void FromScrgb_Bt2020RedDiffersFrom709()
    {
        var rgba = ScrgbPixel(10f, 0f, 0f);
        var rec709 = GalleryHistogram.FromScrgb(rgba, 1, 1, bt2020: false);
        var bt2020 = GalleryHistogram.FromScrgb(rgba, 1, 1, bt2020: true);
        var y709 = GalleryPresent.LuminanceY(10f * 80f, 0, 0, false);
        var y2020 = GalleryPresent.LuminanceY(10f * 80f, 0, 0, true);
        Assert.NotEqual(y709, y2020);
        Assert.Equal(1, rec709.Luma[GalleryHistogram.LogNitsBin(y709)]);
        Assert.Equal(1, bt2020.Luma[GalleryHistogram.LogNitsBin(y2020)]);
        if (GalleryHistogram.LogNitsBin(y709) != GalleryHistogram.LogNitsBin(y2020))
        {
            Assert.NotEqual(rec709.Luma, bt2020.Luma);
        }
    }

    [Fact]
    public void FromScrgb_PqHighlightFillsLastChannelBin()
    {
        var rgba = ScrgbPixel(GalleryHistogram.ChannelMaxScrgb, 0.1f, 0.1f);
        var bins = GalleryHistogram.FromScrgb(rgba, 1, 1, bt2020: true);
        Assert.Equal(1, bins.Red[^1]);
        Assert.Equal(0, bins.Red[0]);
    }

    [Fact]
    public void FromScrgb_EmptyOrShortBufferIsEmpty()
    {
        Assert.False(GalleryHistogram.FromScrgb([], 0, 0, false).HasSamples);
        Assert.False(GalleryHistogram.FromScrgb(new float[4], 2, 1, false).HasSamples);
        Assert.False(GalleryHistogramBins.Empty.HasSamples);
    }

    [Fact]
    public void FromScrgb_HonorsStrideOnLargeFrames()
    {
        var pixels = 100;
        var rgba = new float[pixels * 4];
        var maxSamples = 10;
        Assert.Equal(10, GalleryHistogram.SampleStride(pixels, maxSamples));
        var bins = GalleryHistogram.FromScrgb(rgba, 10, 10, false, GalleryHistogram.BinCount, maxSamples);
        Assert.Equal(10, bins.SampleCount);
    }

    [Fact]
    public void FillHeights_LogKeepsSmallBinsVisible()
    {
        var heights = new float[2];
        GalleryHistogram.FillHeights([1, 1000], heights);
        Assert.True(heights[0] > 0.05f);
        Assert.Equal(1f, heights[1], 3);
        GalleryHistogram.FillHeights([0, 0], heights);
        Assert.Equal(0f, heights[0]);
        Assert.Equal(0f, heights[1]);
    }

    [Fact]
    public void LayoutBars_SpanThePlotWidth()
    {
        float[] heights = [0.5f, 1f, 0.25f, 0f];
        var dest = new (double X, double Width, double Height)[4];
        GalleryHistogram.LayoutBars(4, 100, 40, heights, dest);
        Assert.Equal(0, dest[0].X);
        Assert.Equal(25, dest[0].Width);
        Assert.Equal(75, dest[3].X);
        Assert.Equal(20, dest[0].Height);
        Assert.Equal(40, dest[1].Height);
        Assert.Equal(0, dest[3].Height);
        Assert.Equal(100, dest.Sum(b => b.Width));
    }

    [Fact]
    public void Caption_NamesPresentedFrameNotThumb()
    {
        Assert.Equal(
            "CIE Y · 64 bins · presented frame",
            GalleryHistogram.Caption(false, 64, 10));
        Assert.Equal(
            "scRGB R/G/B · 64 bins · presented frame",
            GalleryHistogram.Caption(true, 64, 10));
        Assert.Equal("", GalleryHistogram.Caption(false, 64, 0));
    }

    private static float[] ScrgbPixel(float r, float g, float b) =>
        [r, g, b, 1f];
}
