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

    private static ImageStats StatsLc(float lc, float p50 = 0.55f) =>
        new(p50, lc, lc, lc, lc, 1f, null, false, 0);

    [Fact]
    public void RefWhite_AnchorsToDisplayWhite_WhenContentFits()
    {
        // Anchor holds when Lc ≈ 1 (no HDR shoulder room needed).
        var stats = StatsLc(1.01f, p50: 0.5f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        ToneMapper.MapPixel(1f, 1f, 1f, p, out var r, out var g, out var b);
        Assert.InRange(r, 0.98f, 1.02f);
        Assert.InRange(g, 0.98f, 1.02f);
        Assert.InRange(b, 0.98f, 1.02f);
    }

    [Fact]
    public void MidGrey_Placement_Sdr203()
    {
        var stats = StatsLc(1.01f, p50: 0.5f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, p, out var r, out _, out _);
        Assert.InRange(r, 0.17f, 0.19f);
    }

    [Fact]
    public void RefWhite_AnchorsToDisplayWhite_Sdr80_WhenContentFits()
    {
        var stats = StatsLc(1.01f, p50: 0.5f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr80, 0f);
        var w = DisplayTarget.Sdr80.PeakNormalized;
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

    /// <summary>P1: MapScalar shoulder reaches display white at Lc, strictly increasing.</summary>
    [Fact]
    public void P1_SdrShoulder_ReachesDisplayWhiteAtLc_StrictlyIncreasing()
    {
        var stats = StatsLc(8f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var atWhite = ToneMapper.MapScalar(1f, p);
        Assert.True(atWhite < 0.98f, $"paper should sit below display white when Lc>1, got {atWhite}");
        float prev = atWhite;
        for (var i = 1; i <= 32; i++)
        {
            var x = 1f + ((stats.Lc - 1f) * (i / 32f));
            var y = ToneMapper.MapScalar(x, p);
            Assert.True(y > prev + 1e-5f, $"P1 scalar flat at x={x}: {y} ≤ {prev}");
            prev = y;
        }

        Assert.InRange(ToneMapper.MapScalar(stats.Lc, p), 0.98f, 1.02f);
    }

    /// <summary>P1: MapPixel neutrals ≥ ref white must be strictly increasing (not all 1.0).</summary>
    [Fact]
    public void P1_MapPixel_NeutralsAboveWhite_StrictlyIncreasing()
    {
        var stats = StatsLc(8f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        float prev = -1f;
        foreach (var x in new[] { 1f, 1.2f, 2f, 4f, 8f })
        {
            ToneMapper.MapPixel(x, x, x, p, out var r, out _, out _);
            Assert.True(r > prev + 1e-4f, $"P1 MapPixel flat: {x} → {r} ≤ {prev}");
            Assert.True(r <= 1.01f, $"P1 MapPixel above display white: {r}");
            prev = r;
        }
    }

    /// <summary>P1: LUT path also strictly increasing above white.</summary>
    [Fact]
    public void P1_Lut_NeutralsAboveWhite_StrictlyIncreasing()
    {
        var stats = StatsLc(8f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var lut = ToneMapLut.Bake(p, size: 256);
        float prev = -1f;
        foreach (var x in new[] { 1f, 1.2f, 2f, 4f, 8f })
        {
            lut.MapPixel(x, x, x, out var r, out _, out _);
            Assert.True(r > prev + 1e-4f, $"P1 LUT flat: {x} → {r} ≤ {prev}");
            Assert.True(r <= 1.01f);
            prev = r;
        }
    }

    /// <summary>P3: saturated highlights at different levels must not collapse to one colour.</summary>
    [Fact]
    public void P3_SaturatedHighlights_NotOneWashedColour()
    {
        var stats = StatsLc(8f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        ToneMapper.MapPixel(2f, 0.4f, 0.15f, p, out var r1, out var g1, out var b1);
        ToneMapper.MapPixel(6f, 1.2f, 0.45f, p, out var r2, out var g2, out var b2);
        ToneMapper.MapPixel(8f, 1.6f, 0.6f, p, out var r3, out var g3, out var b3);
        // Distinct outputs — old path collapsed all ≥Lc to ~(.822,.379,.278).
        Assert.True(Math.Abs(r1 - r2) > 0.02f || Math.Abs(g1 - g2) > 0.02f,
            $"P3 collapsed 2 vs 6: ({r1},{g1},{b1}) vs ({r2},{g2},{b2})");
        Assert.True(Math.Abs(r2 - r3) > 0.01f || Math.Abs(g2 - g3) > 0.01f,
            $"P3 collapsed 6 vs 8: ({r2},{g2},{b2}) vs ({r3},{g3},{b3})");
        Assert.True(r1 > g1 && g1 > b1);
        Assert.True(r3 > g3 && g3 > b3);
    }

    /// <summary>N2: tone-mapped output at/near Ld must keep chroma.</summary>
    [Fact]
    public void N2_SaturatedHighlight_NotGreyedAtDisplayWhite()
    {
        var stats = StatsLc(1.01f, p50: 0.5f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        ToneMapper.MapPixel(1f, 0.25f, 0.08f, p, out var r, out var g, out var b);
        var peak = Math.Max(r, Math.Max(g, b));
        Assert.InRange(peak, 0.85f, 1.05f);
        var y = ImagingConstants.Bt2020Y(r, g, b);
        Assert.True(Math.Abs(r - y) > 0.08f, $"N2 highlight greyed: rgb=({r},{g},{b}) Y={y}");
        Assert.True(r > g && g > b, $"N2 hue collapsed: rgb=({r},{g},{b})");
    }

    [Fact]
    public void N3_Bt2390_BoundedAndMonotonic_Lc5_At1000Nit()
    {
        const float lc = 5f;
        var ld = DisplayTarget.Hdr(1000f).Ld;
        float prev = -1f;
        float[] xs = [0f, 0.5f, 1f, 2f, 4f, 5f, 5.5f, 6f, 10f];
        foreach (var x in xs)
        {
            var y = Bt2390Eetf.MapLinear(x, lc, ld);
            Assert.True(y <= ld + 1e-4f, $"N3 overshoot x={x} → {y} > Ld={ld}");
            Assert.True(y + 1e-5f >= prev, $"N3 non-monotonic x={x}: {y} < {prev}");
            prev = y;
        }

        Assert.Equal(Bt2390Eetf.MapLinear(lc, lc, ld), Bt2390Eetf.MapLinear(6f, lc, ld), 4);
    }

    [Fact]
    public void N4_DisplayTarget_ContentHash_StableAcrossCalls()
    {
        var a = DisplayTarget.Sdr203.ContentHash;
        Assert.Equal(a, DisplayTarget.Sdr203.ContentHash);
        Assert.NotEqual(DisplayTarget.Sdr80.ContentHash, a);
        Assert.NotEqual(DisplayTarget.Hdr(1000f).ContentHash, a);
    }

    [Fact]
    public void Ev0_LutMatchesDirect_Tight_At256()
    {
        var (frame, stats) = MasterFromFixture();
        var p0 = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var direct = ToneMapper.MapFrame(frame, p0);
        var lut = ToneMapLut.Bake(p0, size: 256);
        Assert.Equal(256, lut.Size);
        for (var i = 0; i < frame.PixelCount; i++)
        {
            var o = i * 4;
            lut.MapPixel(frame.Rgba[o], frame.Rgba[o + 1], frame.Rgba[o + 2], out var r, out var g, out var b);
            Assert.InRange(r, direct[o] - 0.01f, direct[o] + 0.01f);
            Assert.InRange(g, direct[o + 1] - 0.01f, direct[o + 1] + 0.01f);
            Assert.InRange(b, direct[o + 2] - 0.01f, direct[o + 2] + 0.01f);
        }
    }

    [Fact]
    public void Ev0_LutMatchesDirect_Tight_At1024()
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
    public void PlusOneEv_BrightensMidGrey_LcUnchanged()
    {
        var stats = StatsLc(4f, p50: 0.5f);
        var d = DisplayTarget.Sdr203;
        var lcBefore = stats.Lc;
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, new ToneMapParams(stats, d, 0f), out var r0, out _, out _);
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, new ToneMapParams(stats, d, 1f), out var r1, out _, out _);
        Assert.True(r1 > r0 + 1e-4f);
        Assert.Equal(lcBefore, stats.Lc);
    }

    /// <summary>P2: pixel-level +3 EV must soft-roll — fails on hard clip to white.</summary>
    [Fact]
    public void P2_PlusThreeEv_MapPixel_SoftRollNotHardClip()
    {
        var stats = StatsLc(4f, p50: 0.5f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 3f);
        ToneMapper.MapPixel(0.15f, 0.15f, 0.15f, p, out var a, out _, out _);
        ToneMapper.MapPixel(0.30f, 0.30f, 0.30f, p, out var b, out _, out _);
        ToneMapper.MapPixel(0.50f, 0.50f, 0.50f, p, out var c, out _, out _);
        Assert.True(a < b && b < c, $"P2 hard-clip: {a}, {b}, {c}");
        Assert.True(c <= 1.01f);
        // Hard-clip signature: everything that lands ≥1 after gain becomes identical white.
        Assert.True(Math.Abs(a - b) > 1e-3f && Math.Abs(b - c) > 1e-3f);
    }

    [Fact]
    public void P2_PlusThreeEv_HdrPath_RollsOffNotHardClamp()
    {
        var stats = StatsLc(5f, p50: 0.4f);
        var display = DisplayTarget.Hdr(1000f); // Ld ≈ 4.93 < Lc·8
        var p = new ToneMapParams(stats, display, 3f);
        var y1 = ToneMapper.MapScalar(0.5f * 8f, p);
        var y2 = ToneMapper.MapScalar(1.0f * 8f, p);
        var y3 = ToneMapper.MapScalar(2.0f * 8f, p);
        Assert.True(y1 < y2 && y2 <= y3 + 1e-4f, $"P2 HDR flat/clip: {y1}, {y2}, {y3}");
        Assert.True(y3 <= display.Ld + 1e-3f);
    }

    [Fact]
    public void Curve_StrictlyIncreasing_AboveKnee()
    {
        var stats = StatsLc(6f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        float prev = -1f;
        for (var i = 0; i <= 64; i++)
        {
            var x = (i / 64f) * stats.Lc;
            var y = ToneMapper.MapScalar(x, p);
            if (x > 0.55f && x < stats.Lc - 1e-3f)
            {
                Assert.True(y > prev + 1e-6f, $"flat at {x}: {y} ≤ {prev}");
            }
            else
            {
                Assert.True(y + 1e-5f >= prev);
            }

            prev = y;
        }
    }

    [Fact]
    public void DoubledImage_Reanalyze_ChangesMappedPixels()
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

        var a = ToneMapper.MapFrame(frame, new ToneMapParams(stats, DisplayTarget.Sdr203, 0f));
        var b = ToneMapper.MapFrame(frame2, new ToneMapParams(stats2, DisplayTarget.Sdr203, 0f));
        // Must differ in mapped pixels — not merely in Lc (M8 tautology fix).
        Assert.True(Math.Abs(a[4] - b[4]) > 1e-4f || Math.Abs(a[0] - b[0]) > 1e-4f,
            $"mapped pixels identical (Lc {stats.Lc} vs {stats2.Lc})");
    }

    [Fact]
    public void ThumbLutVsFullDirect_SharedStats_LowDeltaE()
    {
        var (frame, stats) = MasterFromFixture();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var full = ToneMapper.MapFrame(frame, p);
        var lut = ToneMapLut.Bake(p, size: 256);

        double maxDe = 0;
        for (var i = 0; i < frame.PixelCount; i++)
        {
            var o = i * 4;
            lut.MapPixel(frame.Rgba[o], frame.Rgba[o + 1], frame.Rgba[o + 2], out var r, out var g, out var b);
            maxDe = Math.Max(maxDe, DeltaE2000.FromWorkingRgb(full[o], full[o + 1], full[o + 2], r, g, b));
        }

        Assert.True(maxDe < 1.5, $"thumb LUT vs full direct dE {maxDe}");
    }

    /// <summary>
    /// Placement dE vs identity reference for content that fits (Lc≈1).
    /// Not a self-comparison tautology.
    /// </summary>
    [Fact]
    public void PlacementDeltaE_FitsContent_MidGreyAndWhiteNearIdentity()
    {
        var stats = StatsLc(1.01f, p50: 0.5f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, p, out var mr, out var mg, out var mb);
        ToneMapper.MapPixel(1f, 1f, 1f, p, out var wr, out var wg, out var wb);
        var dMid = DeltaE2000.FromWorkingRgb(0.18f, 0.18f, 0.18f, mr, mg, mb);
        var dWhite = DeltaE2000.FromWorkingRgb(1f, 1f, 1f, wr, wg, wb);
        Assert.True(dMid < 1.0, $"mid-grey dE {dMid}");
        Assert.True(dWhite < 1.0, $"ref-white dE {dWhite}");
    }

    [Fact]
    public void LutCacheKey_HasStatsNotExposure()
    {
        var (_, stats) = MasterFromFixture();
        var a = LutCacheKey.Create("file", DisplayTarget.Sdr203, stats);
        Assert.Contains(stats.StatsHash, a.Token, StringComparison.Ordinal);
        Assert.DoesNotContain("ev", a.Token, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HueDrift_UnderTwoDegrees()
    {
        var stats = StatsLc(1.01f, p50: 0.5f);
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

    [Fact]
    public void SceneAverage_P50_DrivesKnee()
    {
        var low = StatsLc(4f, p50: 0.5f);
        var high = StatsLc(4f, p50: 0.7f);
        var yLow = ToneMapper.MapScalar(0.85f, new ToneMapParams(low, DisplayTarget.Sdr203, 0f));
        var yHigh = ToneMapper.MapScalar(0.85f, new ToneMapParams(high, DisplayTarget.Sdr203, 0f));
        Assert.True(Math.Abs(yLow - yHigh) > 1e-4f, "P50/scene-avg unused by curve");
    }

    private static double AtanHue(float r, float g, float b)
    {
        var x = r - ((g + b) * 0.5f);
        var y = (g - b) * 0.8660254f;
        var deg = Math.Atan2(y, x) * 180.0 / Math.PI;
        return deg < 0 ? deg + 360 : deg;
    }
}
