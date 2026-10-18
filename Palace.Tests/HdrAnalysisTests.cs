using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class HdrAnalysisTests
{
    private const float White = HistogramScale.SdrWhiteNits;

    private static float[] Pixels(params (float R, float G, float B)[] px)
    {
        var a = new float[px.Length * 4];
        for (var i = 0; i < px.Length; i++)
        {
            a[i * 4] = px[i].R;
            a[(i * 4) + 1] = px[i].G;
            a[(i * 4) + 2] = px[i].B;
            a[(i * 4) + 3] = 1f;
        }

        return a;
    }

    /// <summary>Display-P3 red (97% red + 3% white) in linear BT.709: outside 709, inside P3.</summary>
    private static (float R, float G, float B) P3Red()
    {
        float r = 0.97f + 0.03f, g = 0.03f, b = 0.03f;
        return (
            (1.2249f * r) - (0.2247f * g),
            (-0.0420f * r) + (1.0419f * g),
            (-0.0197f * r) - (0.0786f * g) + (1.0979f * b));
    }

    [Fact]
    public void StopCount_SdrIsZero_HdrCeilsAndClamps()
    {
        Assert.Equal(0, HistogramScale.StopCount(false, 10_000));
        Assert.Equal(1, HistogramScale.StopCount(true, 100));
        Assert.Equal(1, HistogramScale.StopCount(true, White * 2));
        Assert.Equal(3, HistogramScale.StopCount(true, 1000));
        Assert.Equal(6, HistogramScale.StopCount(true, 10_000));
        Assert.Equal(6, HistogramScale.StopCount(true, 1e9f));
    }

    [Fact]
    public void Scale_SdrSegmentIsPerceptual_HdrSegmentIsStops()
    {
        Assert.Equal(1.0, HistogramScale.SdrFraction(0));
        Assert.Equal(HistogramScale.HdrSdrFraction, HistogramScale.SdrFraction(4));
        Assert.Equal(0, HistogramScale.X01(0, 4));
        Assert.Equal(HistogramScale.HdrSdrFraction, HistogramScale.X01(White, 4), 6);
        Assert.Equal(1.0, HistogramScale.X01(White * 16, 4), 6);
        Assert.Equal(HistogramScale.StopX01(2, 4), HistogramScale.X01(White * 4, 4), 6);
        // sRGB-encoded: 18% grey sits near the middle of the SDR segment.
        Assert.InRange(HistogramScale.X01(White * 0.18f, 0), 0.45, 0.5);
        // SDR-only: above white clips to the right edge.
        Assert.Equal(1.0, HistogramScale.X01(White * 4, 0), 6);
    }

    [Theory]
    [InlineData(0.5f, 0)]
    [InlineData(20f, 0)]
    [InlineData(150f, 3)]
    [InlineData(400f, 3)]
    [InlineData(1500f, 4)]
    public void NitsAtX01_InvertsX01(float nits, int stops)
    {
        var x = HistogramScale.X01(nits, stops);
        Assert.Equal(nits, HistogramScale.NitsAtX01(x, stops), nits * 0.002f);
    }

    [Fact]
    public void Bin_EdgesLandInTheRightSection()
    {
        Assert.Equal(0, HistogramScale.Bin(0, 3));
        Assert.Equal(0, HistogramScale.Bin(-5, 3));
        Assert.Equal(HistogramScale.SdrBins - 1, HistogramScale.Bin(White, 3));
        Assert.Equal(HistogramScale.SdrBins, HistogramScale.Bin(White * 1.001f, 3));
        Assert.Equal(HistogramScale.SdrBins + HistogramScale.BinsPerStop, HistogramScale.Bin(White * 2.01f, 3));
        Assert.Equal(HistogramScale.BinCount(3) - 1, HistogramScale.Bin(1e9f, 3));
        Assert.Equal(HistogramScale.SdrBins - 1, HistogramScale.Bin(White * 8, 0));
    }

    [Fact]
    public void HoverLabel_NamesSectionAndStops()
    {
        Assert.StartsWith("SDR ·", HistogramScale.HoverLabel(0.3, 3));
        Assert.Equal("HDR +2.0 stops · 812 nits", HistogramScale.HoverLabel(HistogramScale.StopX01(2, 3), 3));
    }

    [Fact]
    public void Heatmap_PaletteHitsSkivStops()
    {
        Assert.Equal((0f, 0f, 0f), HdrHeatmap.EncodedAt(0));
        Assert.Equal((0f, 0f, 1f), HdrHeatmap.EncodedAt(3.16f));
        Assert.Equal((0f, 1f, 1f), HdrHeatmap.EncodedAt(10f));
        Assert.Equal((0f, 1f, 0f), HdrHeatmap.EncodedAt(31.6f));
        Assert.Equal((1f, 1f, 0f), HdrHeatmap.EncodedAt(100f));
        Assert.Equal((1f, 0f, 0f), HdrHeatmap.EncodedAt(1000f));
        Assert.Equal((1f, 0f, 1f), HdrHeatmap.EncodedAt(3160f));
        Assert.Equal((1f, 1f, 1f), HdrHeatmap.EncodedAt(10_000f));
        Assert.Equal((1f, 1f, 1f), HdrHeatmap.EncodedAt(50_000f));
        var mid = HdrHeatmap.EncodedAt(MathF.Sqrt(100f * 316f));
        Assert.Equal(1f, mid.R, 3);
        Assert.InRange(mid.G, 0.7f, 0.8f);
        Assert.Equal((255, 255, 0), HdrHeatmap.Srgb8At(100f));
    }

    [Fact]
    public void Gamut_ClassifiesPrimariesIntoSmallestGamut()
    {
        Assert.Equal(GamutKind.Rec709, CieGamut.ClassifyScrgb(1, 0, 0, 17));
        Assert.Equal(GamutKind.Rec709, CieGamut.ClassifyScrgb(1, 1, 1, 80));
        var (pr, pg, pb) = P3Red();
        Assert.Equal(GamutKind.DciP3, CieGamut.ClassifyScrgb(pr, pg, pb, 20));
        GalleryPresent.Bt2020ToBt709(0.03f, 1, 0.03f, out var r, out var g, out var b);
        Assert.Equal(GamutKind.Rec2020, CieGamut.ClassifyScrgb(r, g, b, 50));
        // A spectral-ish blue-green outside 2020 but inside AP0.
        Assert.Equal(GamutKind.AcesAp0, CieGamut.Classify(0.02, 0.5));
        // Outside the spectral locus and AP0.
        Assert.Equal(GamutKind.Invalid, CieGamut.Classify(0.9, 0.5));
        Assert.Equal(GamutKind.Invalid, CieGamut.ClassifyScrgb(-1, -1, -1, -80));
        // Near-black counts as 709 (chromaticity is noise there).
        Assert.Equal(GamutKind.Rec709, CieGamut.ClassifyScrgb(0, 0, 0, 0));
    }

    [Fact]
    public void Analyze_LightStats_MaxCllChannelP99Fall()
    {
        // 99 grey SDR-white pixels + 1 bright blue.
        var px = new (float, float, float)[100];
        for (var i = 0; i < 99; i++)
        {
            px[i] = (2.5f, 2.5f, 2.5f);
        }

        px[99] = (0.1f, 0.2f, 12.5f);
        var result = HdrAnalysis.Analyze(Pixels(px), 10, 10, 1, hdr: true);
        Assert.Equal(12.5f, result.Light.MaxCllScrgb);
        Assert.Equal('B', result.Light.MaxCllChannel);
        Assert.Equal(1000f, result.Light.MaxCllNits);
        Assert.Equal(200f, result.Light.P99Nits, 0.5f);
        var fall = ((99 * 2.5f) + 12.5f) / 100f * 80f;
        Assert.Equal(fall, result.Light.FallNits, 0.01f);
        Assert.Equal(0, result.Light.AboveSdrWhitePercent);
        Assert.Equal(100, result.SampleCount);
        Assert.True(result.IsHdr);
        Assert.Equal(100, result.Gamut.Rec709 + result.Gamut.WideGamut, 6);
    }

    [Fact]
    public void Analyze_P99_IgnoresTheTopPercent()
    {
        var px = new (float, float, float)[1000];
        for (var i = 0; i < px.Length; i++)
        {
            var v = i < 995 ? 1f : 100f;
            px[i] = (v, v, v);
        }

        var light = HdrAnalysis.Analyze(Pixels(px), 1000, 1, 1, hdr: true).Light;
        Assert.Equal(80f, light.P99Nits, 0.1f);
        Assert.Equal(8000f, light.MaxNits, 1f);
        Assert.Equal(0.5, light.AboveSdrWhitePercent, 6);
    }

    [Fact]
    public void Analyze_FullPassMaxCllSeesUnsampledPeak()
    {
        var w = 100;
        var h = 100;
        var rgba = new float[w * h * 4];
        Array.Fill(rgba, 0.5f);
        // Pixel 1 is skipped by stride 2.
        rgba[4] = 40f;
        var result = HdrAnalysis.Analyze(rgba, w, h, 1, hdr: true, maxSamples: 5000);
        Assert.Equal(40f, result.Light.MaxCllScrgb);
        Assert.Equal('R', result.Light.MaxCllChannel);
        Assert.Equal(5000, result.SampleCount);
    }

    [Fact]
    public void Analyze_GamutCoverageAndCieDensity()
    {
        var (pr, pg, pb) = P3Red();
        var result = HdrAnalysis.Analyze(
            Pixels((1, 1, 1), (1, 0, 0), (pr * 2, pg * 2, pb * 2), (0, 0, 0)), 4, 1, 1, hdr: false);
        Assert.Equal(75, result.Gamut.Rec709, 6);
        Assert.Equal(25, result.Gamut.DciP3, 6);
        Assert.Equal(CieDiagram.GridSize * CieDiagram.GridSize, result.CieDensity.Length);
        Assert.Equal(3, result.CieDensity.Sum());
        Assert.True(CieDiagram.TryCell(CieGamut.D65X, CieGamut.D65Y, out var d65));
        Assert.Equal(1, result.CieDensity[d65]);
        Assert.Equal("Rec.709 75.0% · DCI-P3 25.0%", result.Gamut.Summary());
    }

    [Fact]
    public void Analyze_EmptyOrShortBufferIsEmpty()
    {
        Assert.False(HdrAnalysis.Analyze([], 0, 0, null, true).HasSamples);
        Assert.False(HdrAnalysis.Analyze(new float[4], 2, 1, 1, true).HasSamples);
        Assert.Same(HdrAnalysisResult.Empty, HdrAnalysis.Analyze(new float[4], 2, 1, 1, true));
    }

    [Fact]
    public void Analyze_NaNAndInfinityDoNotPoisonStats()
    {
        var result = HdrAnalysis.Analyze(
            Pixels((float.NaN, 1, 1), (float.PositiveInfinity, 1, 1), (1, 1, 1)), 3, 1, 1, hdr: true);
        Assert.True(float.IsFinite(result.Light.FallNits));
        Assert.Equal(1f, result.Light.MaxCllScrgb);
    }

    [Fact]
    public void Analyze_SdrStillHasNoHdrSection_HdrHasStopBins()
    {
        var sdr = HdrAnalysis.Analyze(Pixels((2.5375f, 0, 0)), 1, 1, 1, hdr: false).Histogram;
        Assert.Equal(0, sdr.StopCount);
        Assert.Equal(HistogramScale.SdrBins, sdr.BinCount);
        Assert.Equal(1, sdr.Red[HistogramScale.SdrBins - 1]);

        var hdr = HdrAnalysis.Analyze(Pixels((25f, 0.5f, 0.5f)), 1, 1, 1, hdr: true).Histogram;
        Assert.Equal(4, hdr.StopCount);
        Assert.Equal(HistogramScale.BinCount(4), hdr.Red.Length);
        Assert.Equal(1, hdr.Red[HistogramScale.Bin(2000f, 4)]);
        Assert.True(HistogramScale.Bin(2000f, 4) >= HistogramScale.SdrBins);
    }

    [Fact]
    public void AnalyzePacked8_UsesTheSamePass()
    {
        // BGRA white + black.
        byte[] data = [255, 255, 255, 255, 0, 0, 0, 255];
        var result = HdrAnalysis.AnalyzePacked8(data, 2, 1, HdrPackedFormat.Bgra8);
        Assert.Equal(2, result.SampleCount);
        Assert.Equal(0, result.Histogram.StopCount);
        Assert.Equal(1, result.Histogram.Luma[HistogramScale.SdrBins - 1]);
        Assert.Equal(1, result.Histogram.Luma[0]);
        Assert.False(HdrAnalysis.AnalyzePacked8([1, 2], 2, 1, HdrPackedFormat.Bgra8).HasSamples);
    }

    [Fact]
    public void Heatmap_PixelMapMatchesHistogramPalette()
    {
        // 1000-nit grey through the rasterizer: palette red at SDR white.
        var v = 1000f / 80f;
        var src = new[] { v, v, v, 1f };
        var dest = new ushort[4];
        var map = new HdrPixelMap(1f, 125f, HdrVizMode.Heatmap, HdrPixelMap.VizWhiteFor(true), 1);
        HdrRasterize.Fill(src, 1, 1, ImageScaling.Fit, 1, 1, map, dest, CancellationToken.None);
        HdrHeatmap.ScrgbAt(HdrAnalysis.PixelNits(v, v, v, 1), HdrPixelMap.VizWhiteFor(true), out var r, out var g, out var b);
        Assert.Equal(GalleryPresent.FloatToHalf(r), dest[0]);
        Assert.Equal(GalleryPresent.FloatToHalf(g), dest[1]);
        Assert.Equal(GalleryPresent.FloatToHalf(b), dest[2]);
        Assert.Equal(203f / 80f, r, 3);
        Assert.Equal(0f, g, 3);

        // SDR display: palette white is scRGB 1.0 and still clips.
        var sdr = new HdrPixelMap(0.1f, 1f, HdrVizMode.Heatmap, HdrPixelMap.VizWhiteFor(false), 1);
        sdr.Apply(v, v, v, out var sr, out _, out _);
        Assert.Equal(1f, sr, 3);

        // None = the old scale + clip.
        new HdrPixelMap(0.5f, 1f).Apply(4f, -1f, 1f, out var nr, out var ng, out var nb);
        Assert.Equal((1f, 0f, 0.5f), (nr, ng, nb));
    }

    [Fact]
    public void CieDiagram_RendersLocusTrianglesAndDensity()
    {
        Assert.True(CieDiagram.InsideLocus(CieGamut.D65X, CieGamut.D65Y));
        Assert.False(CieDiagram.InsideLocus(0.7, 0.7));
        Assert.False(CieDiagram.TryCell(0.85, 0.3, out _));
        var empty = CieDiagram.RenderBgra([]);
        Assert.Equal(CieDiagram.GridSize * CieDiagram.GridSize * 4, empty.Length);
        // Outside the locus is transparent; D65 marker is white.
        Assert.Equal(0, empty[3]);
        var (wx, wy) = CieDiagram.ToPixel(CieGamut.D65X, CieGamut.D65Y, CieDiagram.GridSize);
        var o = ((wy * CieDiagram.GridSize) + wx) * 4;
        Assert.Equal(255, empty[o]);
        Assert.Equal(255, empty[o + 3]);

        var density = new int[CieDiagram.GridSize * CieDiagram.GridSize];
        Assert.True(CieDiagram.TryCell(0.3, 0.45, out var cell));
        density[cell] = 10;
        var plot = CieDiagram.RenderBgra(density);
        var co = cell * 4;
        Assert.True(plot[co + 1] > empty[co + 1], "density brightens the cell");
    }
}
