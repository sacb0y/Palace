using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

public class ImagingAnalyzeTests
{
    [Fact]
    public void Lc_WhenMetadataPeakBelowFloor_DoesNotThrow()
    {
        // M6: metadata peak < lower bound must not throw from Math.Clamp.
        var rgba = new float[4 * 4];
        for (var i = 0; i < 4; i++)
        {
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 2f;
            rgba[i * 4 + 3] = 1f;
        }

        var frame = new NormalizedFrame
        {
            Rgba = rgba,
            Width = 2,
            Height = 2,
            SourceClipped = false,
            SourceClipRatio = 0,
            MetadataPeakNits = 50f, // 50/203 ≈ 0.25 < lo≈1
        };
        var stats = Analyze.Run(frame);
        Assert.True(stats.Lc >= 1f);
    }

    [Fact]
    public void HistogramPercentiles_Stable()
    {
        var master = ImagingFixtureIO.LoadF32("master.bt2020.f32");
        var frame = new NormalizedFrame
        {
            Rgba = master,
            Width = 2,
            Height = 2,
            SourceClipped = false,
            SourceClipRatio = 0,
            MetadataPeakNits = 10_000f,
        };
        var stats = Analyze.Run(frame);
        Assert.True(stats.P50 > 0f);
        Assert.True(stats.P99995 >= stats.P50);
        Assert.Equal(stats.StatsHash, Analyze.Run(frame).StatsHash);
    }

    [Fact]
    public void Accumulator_SdrBarsAndGradient_WithinOneBin()
    {
        const int w = 48;
        const int h = 32;
        var rgba = new float[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var o = ((y * w) + x) * 4;
                // Top: gradient; bottom: solid color bars.
                if (y < h / 2)
                {
                    var t = x / (float)(w - 1);
                    rgba[o] = 0.1f + 0.8f * t;
                    rgba[o + 1] = 0.2f + 0.5f * (1f - t);
                    rgba[o + 2] = 0.15f + 0.4f * t * (1f - t);
                }
                else
                {
                    var bar = x * 5 / w;
                    rgba[o] = bar is 0 or 3 or 4 ? 0.9f : 0.05f;
                    rgba[o + 1] = bar is 1 or 3 ? 0.9f : 0.05f;
                    rgba[o + 2] = bar is 2 or 4 ? 0.9f : 0.05f;
                }

                rgba[o + 3] = 1f;
            }
        }

        AssertAccumulatorCloseToRun(rgba, w, h, metadataPeak: null);
    }

    [Fact]
    public void Accumulator_HdrPqHighlight_WithinOneBin()
    {
        // Mostly mid-grey Working + a few PQ-range highlights (y ≫ 1).
        const int w = 40;
        const int h = 40;
        var rgba = new float[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            var o = i * 4;
            rgba[o] = rgba[o + 1] = rgba[o + 2] = 0.4f;
            rgba[o + 3] = 1f;
        }

        // Bright corner (HDR highlight in Working).
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var o = ((y * w) + x) * 4;
                rgba[o] = 12f;
                rgba[o + 1] = 10f;
                rgba[o + 2] = 8f;
            }
        }

        AssertAccumulatorCloseToRun(rgba, w, h, metadataPeak: 4000f);
    }

    [Fact]
    public void Accumulator_AllBlack_Exact()
    {
        var rgba = new float[16]; // 2×2 black
        for (var i = 3; i < 16; i += 4)
        {
            rgba[i] = 1f;
        }

        var frame = Frame(rgba, 2, 2);
        var run = Analyze.Run(frame);
        var acc = AccFrom(rgba, 2, 2);
        var strip = acc.Finish();
        Assert.Equal(0f, strip.Max);
        Assert.Equal(0f, strip.MaxRgb);
        Assert.Equal(run.Max, strip.Max);
        Assert.Equal(run.MaxRgb, strip.MaxRgb);
        Assert.Equal(run.Lc, strip.Lc);
        Assert.Equal(run.P50, strip.P50);
        Assert.Equal(run.P99995, strip.P99995);
    }

    [Fact]
    public void Accumulator_OnePixel_Exact()
    {
        var rgba = new float[] { 0.7f, 0.2f, 0.1f, 1f };
        var frame = Frame(rgba, 1, 1);
        var run = Analyze.Run(frame);
        var acc = new Analyze.Accumulator();
        acc.Add(rgba, 1);
        var strip = acc.Finish();
        Assert.Equal(run.Max, strip.Max);
        Assert.Equal(run.MaxRgb, strip.MaxRgb);
        AssertNearPercentile(run, strip);
        AssertNearLc(run, strip);
    }

    private static void AssertAccumulatorCloseToRun(
        float[] rgba,
        int w,
        int h,
        float? metadataPeak)
    {
        var frame = new NormalizedFrame
        {
            Rgba = rgba,
            Width = w,
            Height = h,
            SourceClipped = false,
            SourceClipRatio = 0,
            MetadataPeakNits = metadataPeak,
        };
        var run = Analyze.Run(frame);
        var acc = new Analyze.Accumulator(metadataPeak: metadataPeak);
        const int stripRows = 7;
        for (var y0 = 0; y0 < h; y0 += stripRows)
        {
            var rows = Math.Min(stripRows, h - y0);
            acc.Add(rgba.AsSpan(y0 * w * 4, rows * w * 4), w, rows);
        }

        var strip = acc.Finish();
        Assert.Equal(run.Max, strip.Max);
        Assert.Equal(run.MaxRgb, strip.MaxRgb);
        Assert.Equal(run.RefWhite, strip.RefWhite);
        Assert.Equal(run.MetadataPeak, strip.MetadataPeak);
        // Lc = Clamp(P99995, lo, hi) — same one-bin contract as P99995.
        AssertNearPercentile(run, strip);
        AssertNearLc(run, strip);
    }

    private static void AssertNearPercentile(ImageStats run, ImageStats strip)
    {
        var w50 = Analyze.OneAnalyzeBinWidthWorking(run.Max, run.P50);
        var w99 = Analyze.OneAnalyzeBinWidthWorking(run.Max, run.P99995);
        Assert.True(
            Math.Abs(run.P50 - strip.P50) <= w50 + 1e-6f,
            $"P50 run={run.P50} strip={strip.P50} bin={w50}");
        Assert.True(
            Math.Abs(run.P99995 - strip.P99995) <= w99 + 1e-6f,
            $"P99995 run={run.P99995} strip={strip.P99995} bin={w99}");
    }

    private static void AssertNearLc(ImageStats run, ImageStats strip)
    {
        // When both sides clamp to the same lo/hi bound, Lc is exact; otherwise
        // it tracks P99995 within one Analyze PQ bin.
        var w = Analyze.OneAnalyzeBinWidthWorking(run.Max, run.Lc);
        Assert.True(
            Math.Abs(run.Lc - strip.Lc) <= w + 1e-6f,
            $"Lc run={run.Lc} strip={strip.Lc} bin={w}");
    }

    private static NormalizedFrame Frame(float[] rgba, int w, int h) => new()
    {
        Rgba = rgba,
        Width = w,
        Height = h,
        SourceClipped = false,
        SourceClipRatio = 0,
        MetadataPeakNits = null,
    };

    private static Analyze.Accumulator AccFrom(float[] rgba, int w, int h)
    {
        var acc = new Analyze.Accumulator();
        acc.Add(rgba, w * h);
        return acc;
    }
}
