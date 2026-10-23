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
}
