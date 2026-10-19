using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class GalleryHistogramTests
{
    private static float[] ScrgbPixel(float r, float g, float b) => [r, g, b, 1f];

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
    public void ToggleLayer_IndependentButNeverBothOff()
    {
        Assert.True(GalleryHistogram.DefaultShowLuma);
        Assert.True(GalleryHistogram.DefaultShowRgb);
        Assert.Equal((false, true), GalleryHistogram.ToggleLayer(true, true, clickedLuma: true));
        Assert.Equal((true, false), GalleryHistogram.ToggleLayer(true, true, clickedLuma: false));
        // Clicking off the last active layer keeps it on.
        Assert.Equal((false, true), GalleryHistogram.ToggleLayer(false, true, clickedLuma: false));
        Assert.Equal((true, false), GalleryHistogram.ToggleLayer(true, false, clickedLuma: true));
        Assert.Equal((true, true), GalleryHistogram.ToggleLayer(false, true, clickedLuma: true));
    }

    [Fact]
    public void AreaPoints_ClosedPolygonSpansPlot()
    {
        var counts = new int[HistogramScale.BinCount(2)];
        counts[10] = 5;
        counts[^1] = 10;
        var pts = GalleryHistogram.AreaPoints(counts, 2, 300, 100);
        Assert.Equal(counts.Length + 2, pts.Length);
        Assert.Equal(100, pts[0].Y);
        Assert.Equal(100, pts[^1].Y);
        Assert.InRange(pts[^2].X, 299, 300);
        Assert.Equal(0, pts[^2].Y, 6);
        Assert.Empty(GalleryHistogram.AreaPoints([], 0, 300, 100));
    }

    [Fact]
    public void Caption_NamesSectionsAndPresentedFrame()
    {
        Assert.Equal("", GalleryHistogram.Caption(null));
        Assert.Equal("", GalleryHistogram.Caption(GalleryHistogramBins.Empty));
        var sdr = HdrAnalysis.Analyze(ScrgbPixel(1, 1, 1), 1, 1, 1, hdr: false).Histogram;
        Assert.Equal("SDR · presented frame", GalleryHistogram.Caption(sdr));
        var hdr = HdrAnalysis.Analyze(ScrgbPixel(12.5f, 12.5f, 12.5f), 1, 1, 9, hdr: true).Histogram;
        Assert.Equal("SDR + 3 HDR stops · presented frame", GalleryHistogram.Caption(hdr));
    }

    [Fact]
    public void Luma_PresentedBt2020Red_MatchesInfoSourceY()
    {
        HdrColor.EncodedRgbToScrgb(1f, 0f, 0f, HdrTransfer.Pq, 9, out var sr, out var sg, out var sb);
        var sourceY = HdrColor.SourcePrimaryNitsY(1f, 0f, 0f, HdrTransfer.Pq, 9);
        Assert.Equal(sourceY, HdrAnalysis.PixelNits(sr, sg, sb, 9), 0);
        var result = HdrAnalysis.Analyze(ScrgbPixel(sr, sg, sb), 1, 1, 9, hdr: true);
        var bins = result.Histogram;
        Assert.Equal(1, bins.Luma[HistogramScale.Bin(sourceY, bins.StopCount)]);
        Assert.Equal(sourceY, result.Light.MaxNits, 0);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(null)]
    public void Luma_PresentedP3OrUnspecified_MatchesInfoSourceY(int? primaries)
    {
        HdrColor.EncodedRgbToScrgb(1f, 0f, 0f, HdrTransfer.Pq, primaries, out var sr, out var sg, out var sb);
        var sourceY = HdrColor.SourcePrimaryNitsY(1f, 0f, 0f, HdrTransfer.Pq, primaries);
        var result = HdrAnalysis.Analyze(ScrgbPixel(sr, sg, sb), 1, 1, primaries, hdr: true);
        Assert.Equal(1, result.Histogram.Luma[HistogramScale.Bin(sourceY, result.Histogram.StopCount)]);
    }

    [Fact]
    public void UsesBt2100ToScrgbMatrix_MatchesPresentElseBranch()
    {
        Assert.True(HdrColor.UsesBt2100ToScrgbMatrix(9));
        Assert.True(HdrColor.UsesBt2100ToScrgbMatrix(12));
        Assert.True(HdrColor.UsesBt2100ToScrgbMatrix(null));
        Assert.False(HdrColor.UsesBt2100ToScrgbMatrix(1));
        Assert.False(HdrColor.UsesBt2100ToScrgbMatrix(2));
        Assert.False(HdrColor.UsesBt2100ToScrgbMatrix(11));
        Assert.False(HdrColor.UsesBt2100ToScrgbMatrix(10));
        Assert.False(HdrColor.UsesBt2100ToScrgbMatrix(5));
    }
}
