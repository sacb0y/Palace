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

    /// <summary>
    /// Wimbly #212: Accumulator strips ≡ Analyze.Run whole — exact P50/P99995/Lc/MaxRgb.
    /// </summary>
    [Fact]
    public void Accumulator_Strips_MatchAnalyzeRunWhole()
    {
        const int w = 32;
        const int h = 24;
        var rgba = new float[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var o = ((y * w) + x) * 4;
                var t = (x + 1) / (float)w;
                var u = (y + 1) / (float)h;
                rgba[o] = 0.2f + 0.8f * t;
                rgba[o + 1] = 0.1f + 0.5f * u;
                rgba[o + 2] = 0.05f + 0.4f * (1f - t) * u;
                rgba[o + 3] = 1f;
            }
        }

        var frame = new NormalizedFrame
        {
            Rgba = rgba,
            Width = w,
            Height = h,
            SourceClipped = true,
            SourceClipRatio = 0.01f,
            MetadataPeakNits = 1000f,
        };
        var whole = Analyze.Run(frame);

        var acc = new Analyze.Accumulator(
            refWhite: 1f,
            metadataPeak: frame.MetadataPeakNits,
            sourceClipped: frame.SourceClipped,
            sourceClipRatio: frame.SourceClipRatio);
        const int stripRows = 5;
        for (var y0 = 0; y0 < h; y0 += stripRows)
        {
            var rows = Math.Min(stripRows, h - y0);
            acc.Add(rgba.AsSpan(y0 * w * 4, rows * w * 4), w, rows);
        }

        var strip = acc.Finish();
        Assert.Equal(whole.P50, strip.P50);
        Assert.Equal(whole.P99995, strip.P99995);
        Assert.Equal(whole.Max, strip.Max);
        Assert.Equal(whole.MaxRgb, strip.MaxRgb);
        Assert.Equal(whole.Lc, strip.Lc);
        Assert.Equal(whole.RefWhite, strip.RefWhite);
        Assert.Equal(whole.MetadataPeak, strip.MetadataPeak);
        Assert.Equal(whole.SourceClipped, strip.SourceClipped);
        Assert.Equal(whole.SourceClipRatio, strip.SourceClipRatio);
        Assert.Equal(whole.StatsHash, strip.StatsHash);
    }
}
