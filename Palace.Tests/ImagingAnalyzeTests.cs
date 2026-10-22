using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

public class ImagingAnalyzeTests
{
    [Fact]
    public void Lc_ClampsBetweenFloorAndMetadataPeak()
    {
        // Mostly mid-grey with a hot pixel.
        var rgba = new float[4 * 100];
        for (var i = 0; i < 99; i++)
        {
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 0.5f;
            rgba[i * 4 + 3] = 1f;
        }

        rgba[99 * 4] = rgba[99 * 4 + 1] = rgba[99 * 4 + 2] = 20f;
        rgba[99 * 4 + 3] = 1f;

        var frame = new NormalizedFrame
        {
            Rgba = rgba,
            Width = 10,
            Height = 10,
            SourceClipped = false,
            SourceClipRatio = 0,
            MetadataPeakNits = 1000f, // ≈ 1000/203 ≈ 4.93 working
        };
        var stats = Analyze.Run(frame);
        Assert.True(stats.P50 < 1f);
        Assert.True(stats.Lc >= 1f);
        Assert.True(stats.Lc <= (1000f / ImagingConstants.RefWhiteNits) + 1e-3f);
        Assert.True(stats.MaxRgb >= 20f - 1e-3f);
    }

    [Fact]
    public void ExposureDoesNotChangeStats()
    {
        var encoded = FixtureMaster.Encode(TransferKind.Pq);
        var frame = Normalize.Run(
            encoded,
            FixtureMaster.MasterWidth,
            FixtureMaster.MasterHeight,
            new NormalizeOptions(TransferKind.Pq, MetadataPeakNits: 10_000f));
        var a = Analyze.Run(frame);
        var b = Analyze.Run(frame);
        Assert.Equal(a.StatsHash, b.StatsHash);
        Assert.Equal(a.Lc, b.Lc);
    }
}
