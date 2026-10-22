using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

public class ImagingToneMapTests
{
    private static (NormalizedFrame Frame, ImageStats Stats) MasterFromFixture()
    {
        var pq = ImagingFixtureIO.LoadF32("master.pq.f32");
        var frame = Normalize.Run(pq, 2, 2, new NormalizeOptions(TransferKind.Pq, MetadataPeakNits: 10_000f));
        return (frame, Analyze.Run(frame));
    }

    [Fact]
    public void RefWhite_AnchorsToDisplayWhite_Sdr203()
    {
        var (_, stats) = MasterFromFixture();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        ToneMapper.MapPixel(1f, 1f, 1f, p, out var r, out var g, out var b);
        Assert.InRange(r, 0.98f, 1.02f);
        Assert.InRange(g, 0.98f, 1.02f);
        Assert.InRange(b, 0.98f, 1.02f);
    }

    [Fact]
    public void MidGrey_Placement_Sdr203()
    {
        var (_, stats) = MasterFromFixture();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, p, out var r, out _, out _);
        Assert.InRange(r, 0.17f, 0.19f);
    }

    [Fact]
    public void RefWhite_AnchorsToDisplayWhite_Sdr80()
    {
        var (_, stats) = MasterFromFixture();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr80, 0f);
        var w = DisplayTarget.Sdr80.PeakNormalized; // 80/203
        ToneMapper.MapPixel(1f, 1f, 1f, p, out var r, out _, out _);
        Assert.InRange(r, w * 0.98f, w * 1.02f);
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, p, out var m, out _, out _);
        Assert.InRange(m, 0.18f * w * 0.95f, 0.18f * w * 1.05f);
    }

    [Fact]
    public void HdrTarget_IdentityWhenLcFits()
    {
        var stats = new ImageStats(0.2f, 1.5f, 1.6f, 1.6f, 1.5f, 1f, 1000f, false, 0);
        var display = DisplayTarget.Hdr(1000f);
        var p = new ToneMapParams(stats, display, 0f);
        Assert.True(stats.Lc <= display.Ld);
        Assert.Equal(1.2f, ToneMapper.MapScalar(1.2f, p), 5);
    }

    [Fact]
    public void Ev0_LutMatchesDirect_Tight()
    {
        var (frame, stats) = MasterFromFixture();
        var p0 = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var direct = ToneMapper.MapFrame(frame, p0);
        var lut = ToneMapLut.Bake(p0, size: 1024);
        for (var i = 0; i < frame.PixelCount; i++)
        {
            var o = i * 4;
            lut.MapPixel(frame.Rgba[o], frame.Rgba[o + 1], frame.Rgba[o + 2], out var r, out var g, out var b);
            Assert.InRange(r, direct[o] - 0.002f, direct[o] + 0.002f);
            Assert.InRange(g, direct[o + 1] - 0.002f, direct[o + 1] + 0.002f);
            Assert.InRange(b, direct[o + 2] - 0.002f, direct[o + 2] + 0.002f);
        }
    }

    [Fact]
    public void PlusOneEv_BrightensMidGrey()
    {
        var (_, stats) = MasterFromFixture();
        var d = DisplayTarget.Sdr203;
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, new ToneMapParams(stats, d, 0f), out var r0, out _, out _);
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, new ToneMapParams(stats, d, 1f), out var r1, out _, out _);
        Assert.True(r1 > r0);
        Assert.Equal(stats.Lc, stats.Lc);
    }

    [Fact]
    public void PlusThreeEv_HighlightsRollOff()
    {
        var (_, stats) = MasterFromFixture();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 3f);
        ToneMapper.MapPixel(4f, 4f, 4f, p, out var r, out var g, out var b);
        var peak = Math.Max(r, Math.Max(g, b));
        Assert.True(peak <= 1.01f);
        Assert.True(peak < 4f * p.ExposureGain);
    }

    [Fact]
    public void Curve_Monotonic()
    {
        var (_, stats) = MasterFromFixture();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        float prev = -1f;
        for (var i = 0; i <= 64; i++)
        {
            var x = (i / 64f) * Math.Max(stats.Lc, 1.01f);
            var y = ToneMapper.MapScalar(x, p);
            Assert.True(y + 1e-4f >= prev, $"non-monotonic at {x}: {y} < {prev}");
            prev = y;
        }
    }

    [Fact]
    public void DoubledImage_Reanalyze_ChangesOutput()
    {
        var (frame, stats) = MasterFromFixture();
        var doubled = (float[])frame.Rgba.Clone();
        for (var i = 0; i < doubled.Length; i += 4)
        {
            doubled[i] *= 2f;
            doubled[i + 1] *= 2f;
            doubled[i + 2] *= 2f;
        }

        var frame2 = new NormalizedFrame
        {
            Rgba = doubled,
            Width = frame.Width,
            Height = frame.Height,
            SourceClipped = frame.SourceClipped,
            SourceClipRatio = frame.SourceClipRatio,
            MetadataPeakNits = frame.MetadataPeakNits,
        };
        var stats2 = Analyze.Run(frame2);
        Assert.NotEqual(stats.StatsHash, stats2.StatsHash);

        var p1 = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var p2 = new ToneMapParams(stats2, DisplayTarget.Sdr203, 0f);
        var a = ToneMapper.MapFrame(frame, p1);
        var b = ToneMapper.MapFrame(frame2, p2);
        Assert.True(Math.Abs(a[4] - b[4]) > 1e-4f || Math.Abs(stats.Lc - stats2.Lc) > 1e-4f);
    }

    [Fact]
    public void ThumbVsFull_DistinctPaths_LowDeltaE()
    {
        var (frame, stats) = MasterFromFixture();
        // Full path: MapFrame. Thumb path: MapPixel per sample with same stats (shared hash).
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var full = ToneMapper.MapFrame(frame, p);

        // Distinct thumb path: 1×1 center crop re-normalized then mapped with FULL stats.
        var thumbSrc = new float[]
        {
            frame.Rgba[4], frame.Rgba[5], frame.Rgba[6], frame.Rgba[7],
        };
        var thumbFrame = new NormalizedFrame
        {
            Rgba = thumbSrc,
            Width = 1,
            Height = 1,
            SourceClipped = false,
            SourceClipRatio = 0,
            MetadataPeakNits = frame.MetadataPeakNits,
        };
        // Intentionally use full-image stats (product contract) — not re-analyzed thumb stats.
        var thumbOut = ToneMapper.MapFrame(thumbFrame, p);
        var dE = DeltaE2000.FromWorkingRgb(
            full[4], full[5], full[6],
            thumbOut[0], thumbOut[1], thumbOut[2]);
        Assert.True(dE < 1.5, $"thumb vs full dE {dE}");
        Assert.Equal(stats.StatsHash, p.Stats.StatsHash);
    }

    [Fact]
    public void LutCacheKey_HasStatsNotExposure()
    {
        var (_, stats) = MasterFromFixture();
        var d = DisplayTarget.Sdr203;
        var a = LutCacheKey.Create("file", d, stats);
        Assert.Contains(stats.StatsHash, a.Token, StringComparison.Ordinal);
        Assert.DoesNotContain("ev", a.Token, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HueDrift_UnderTwoDegrees()
    {
        var (_, stats) = MasterFromFixture();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        float r0 = 0.45f, g0 = 0.32f, b0 = 0.25f;
        ToneMapper.MapPixel(r0, g0, b0, p, out var r1, out var g1, out var b1);
        var h0 = AtanHue(r0, g0, b0);
        var h1 = AtanHue(r1, g1, b1);
        var dh = Math.Abs(h1 - h0);
        if (dh > 180)
        {
            dh = 360 - dh;
        }

        Assert.True(dh < 2.0, $"hue drift {dh:F2}°");
    }

    private static double AtanHue(float r, float g, float b)
    {
        var x = r - ((g + b) * 0.5f);
        var y = (g - b) * 0.8660254f;
        var deg = Math.Atan2(y, x) * 180.0 / Math.PI;
        return deg < 0 ? deg + 360 : deg;
    }
}
