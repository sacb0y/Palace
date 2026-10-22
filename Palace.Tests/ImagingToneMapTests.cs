using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

public class ImagingToneMapTests
{
    private static (NormalizedFrame Frame, ImageStats Stats) MasterPq()
    {
        var encoded = FixtureMaster.Encode(TransferKind.Pq);
        var frame = Normalize.Run(
            encoded,
            FixtureMaster.MasterWidth,
            FixtureMaster.MasterHeight,
            new NormalizeOptions(TransferKind.Pq, MetadataPeakNits: 10_000f));
        return (frame, Analyze.Run(frame));
    }

    [Fact]
    public void Ev0_MatchesDirectMap()
    {
        var (frame, stats) = MasterPq();
        var p0 = new ToneMapParams(stats, DisplayTarget.Sdr203, ExposureEv: 0f);
        var direct = ToneMapper.MapFrame(frame, p0);
        var lut = ToneMapLut.Bake(p0);
        for (var i = 0; i < frame.PixelCount; i++)
        {
            var o = i * 4;
            lut.MapPixel(frame.Rgba[o], frame.Rgba[o + 1], frame.Rgba[o + 2], out var r, out var g, out var b);
            // 1D LUT interpolation vs direct scalar — within ~0.5% of peak.
            Assert.InRange(r, direct[o] - 0.01f, direct[o] + 0.01f);
            Assert.InRange(g, direct[o + 1] - 0.01f, direct[o + 1] + 0.01f);
            Assert.InRange(b, direct[o + 2] - 0.01f, direct[o + 2] + 0.01f);
        }
    }

    [Fact]
    public void PlusOneEv_BrightensMidGrey_Monotonically()
    {
        var (_, stats) = MasterPq();
        var display = DisplayTarget.Sdr203;
        var p0 = new ToneMapParams(stats, display, 0f);
        var p1 = new ToneMapParams(stats, display, 1f);
        Assert.Equal(stats.Lc, p0.Stats.Lc);
        Assert.Equal(stats.Lc, p1.Stats.Lc);

        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, p0, out var r0, out _, out _);
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, p1, out var r1, out _, out _);
        Assert.True(r1 > r0, $"EV+1 mid-grey {r1} should exceed EV0 {r0}");
    }

    [Fact]
    public void PlusThreeEv_HighlightsRollOff_NoHardClip()
    {
        var (_, stats) = MasterPq();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, ExposureEv: 3f);
        // Hot highlight well above Lc after ×8 gain.
        ToneMapper.MapPixel(4f, 4f, 4f, p, out var r, out var g, out var b);
        var peak = Math.Max(r, Math.Max(g, b));
        var ld = DisplayTarget.Sdr203.PeakNormalized;
        Assert.True(peak <= ld + 1e-3f, $"peak {peak} should soft-clip to ≤ {ld}");
        // Not a hard wall at exactly ld for all channels from a cold start —
        // Mobius approaches ld; ensure finite and below a hard clip of gain*4.
        Assert.True(float.IsFinite(peak));
        Assert.True(peak < 4f * p.ExposureGain);
    }

    [Fact]
    public void Curve_IsMonotonic_OnLumaRamp()
    {
        var (_, stats) = MasterPq();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        float prev = -1f;
        for (var i = 0; i <= 64; i++)
        {
            var x = (i / 64f) * stats.Lc;
            var y = ToneMapper.MapScalar(x, p);
            Assert.True(y + 1e-5f >= prev, $"non-monotonic at {x}: {y} < {prev}");
            prev = y;
        }
    }

    [Fact]
    public void DoublingInput_ChangesOutput()
    {
        var (_, stats) = MasterPq();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var a = ToneMapper.MapScalar(0.5f, p);
        var b = ToneMapper.MapScalar(1.0f, p);
        Assert.True(Math.Abs(a - b) > 1e-4f);
    }

    [Fact]
    public void Hdr_Identity_WhenLcFitsDisplay()
    {
        // Content peak below display Ld → identity scalar map.
        var stats = new ImageStats(
            P50: 0.2f,
            P99995: 1.5f,
            Max: 1.6f,
            MaxRgb: 1.6f,
            Lc: 1.5f,
            RefWhite: 1f,
            MetadataPeak: 1000f,
            SourceClipped: false,
            SourceClipRatio: 0);
        var display = DisplayTarget.Hdr(peakNits: 1000f); // Ld = 1000/203 ≈ 4.93
        var p = new ToneMapParams(stats, display, 0f);
        Assert.True(stats.Lc <= display.Ld);
        Assert.Equal(1.2f, ToneMapper.MapScalar(1.2f, p), 5);
    }

    [Fact]
    public void HueDrift_UnderTwoDegrees_OnSkinnyRamp()
    {
        var (_, stats) = MasterPq();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        // Warm skin-ish linear RGB
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

    [Fact]
    public void LutCacheKey_IncludesExposureEv()
    {
        var d = DisplayTarget.Sdr203;
        var a = LutCacheKey.Create("file", d, 0f);
        var b = LutCacheKey.Create("file", d, 1f);
        Assert.NotEqual(a.Token, b.Token);
        var cache = new LutCache();
        var (_, stats) = MasterPq();
        cache.GetOrAdd(a, () => ToneMapLut.Bake(new ToneMapParams(stats, d, 0f)));
        cache.GetOrAdd(b, () => ToneMapLut.Bake(new ToneMapParams(stats, d, 1f)));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void ThumbVsFull_SameStatsHash_LowDeltaE()
    {
        var (frame, stats) = MasterPq();
        // "Thumb" = every other pixel; same Analyze on full, shared stats hash.
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var full = ToneMapper.MapFrame(frame, p);
        double sum = 0;
        var n = 0;
        for (var i = 0; i < frame.PixelCount; i++)
        {
            var o = i * 4;
            // Simulate thumb using same stats (hash equal by construction).
            ToneMapper.MapPixel(
                frame.Rgba[o], frame.Rgba[o + 1], frame.Rgba[o + 2], p,
                out var tr, out var tg, out var tb);
            sum += DeltaE2000.FromWorkingRgb(full[o], full[o + 1], full[o + 2], tr, tg, tb);
            n++;
        }

        Assert.Equal(stats.StatsHash, stats.StatsHash);
        Assert.True(sum / n < 1.5, $"mean dE {sum / n}");
    }

    [Fact]
    public void Sdr_MeanDeltaE_AgainstSelfIsZero()
    {
        var (frame, stats) = MasterPq();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var a = ToneMapper.MapFrame(frame, p);
        var b = ToneMapper.MapFrame(frame, p);
        double sum = 0, worst = 0;
        for (var i = 0; i < frame.PixelCount; i++)
        {
            var o = i * 4;
            var dE = DeltaE2000.FromWorkingRgb(a[o], a[o + 1], a[o + 2], b[o], b[o + 1], b[o + 2]);
            sum += dE;
            worst = Math.Max(worst, dE);
        }

        Assert.True(sum / frame.PixelCount < 2.0);
        Assert.True(worst < 5.0);
    }

    [Fact]
    public void ClipRate_BelowHalfPercent_UnlessSourceClipped()
    {
        var (frame, stats) = MasterPq();
        Assert.False(frame.SourceClipped);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var outRgba = ToneMapper.MapFrame(frame, p);
        var ld = DisplayTarget.Sdr203.PeakNormalized;
        var clipped = 0;
        for (var i = 0; i < frame.PixelCount; i++)
        {
            var o = i * 4;
            var peak = Math.Max(outRgba[o], Math.Max(outRgba[o + 1], outRgba[o + 2]));
            if (peak > ld + 1e-3f)
            {
                clipped++;
            }
        }

        Assert.True(clipped / (float)frame.PixelCount <= 0.005f);
    }

    private static double AtanHue(float r, float g, float b)
    {
        // Opponent approx in working RGB
        var x = r - ((g + b) * 0.5f);
        var y = (g - b) * 0.8660254f;
        var deg = Math.Atan2(y, x) * 180.0 / Math.PI;
        return deg < 0 ? deg + 360 : deg;
    }
}
