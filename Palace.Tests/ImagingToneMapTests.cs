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

    public static TheoryData<float> AnchorLcValues => new() { 1.01f, 1.5f, 4f, 59.2f };

    /// <summary>Option A: ref white = displayWhite · PaperWhiteFraction(Lc).</summary>
    [Theory]
    [MemberData(nameof(AnchorLcValues))]
    public void RefWhite_FollowsPaperFraction_AllLc(float lc)
    {
        var expected = SdrToneCurve.PaperWhiteFraction(lc);
        var p = new ToneMapParams(StatsLc(lc, p50: 0.5f), DisplayTarget.Sdr203, 0f);
        ToneMapper.MapPixel(1f, 1f, 1f, p, out var r, out var g, out var b);
        Assert.InRange(r, expected * 0.98f, expected * 1.02f);
        Assert.InRange(g, expected * 0.98f, expected * 1.02f);
        Assert.InRange(b, expected * 0.98f, expected * 1.02f);
        Assert.InRange(expected, 1f - SdrToneCurve.MaxPaperDip - 1e-4f, 1.0001f);
        if (lc <= 1.01f + 1e-4f)
        {
            Assert.InRange(r, 0.98f, 1.02f);
        }
        else
        {
            Assert.True(r < 0.995f, $"Option A: Lc={lc} paper should dip below ~1, got {r}");
        }
    }

    /// <summary>Option A: mid-grey scales with paper fraction (Sdr203).</summary>
    [Theory]
    [MemberData(nameof(AnchorLcValues))]
    public void MidGrey_Placement_AllLc_Sdr203(float lc)
    {
        var paper = SdrToneCurve.PaperWhiteFraction(lc);
        var expected = 0.18f * paper;
        var p = new ToneMapParams(StatsLc(lc, p50: 0.5f), DisplayTarget.Sdr203, 0f);
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, p, out var r, out _, out _);
        Assert.InRange(r, expected * 0.95f, expected * 1.05f);
    }

    /// <summary>Option A: ref white + mid-grey on Sdr80 track paper fraction.</summary>
    [Theory]
    [MemberData(nameof(AnchorLcValues))]
    public void RefWhiteAndMidGrey_Sdr80_AllLc(float lc)
    {
        var w = DisplayTarget.Sdr80.PeakNormalized;
        var paper = SdrToneCurve.PaperWhiteFraction(lc);
        var p = new ToneMapParams(StatsLc(lc, p50: 0.5f), DisplayTarget.Sdr80, 0f);
        ToneMapper.MapPixel(1f, 1f, 1f, p, out var r, out _, out _);
        Assert.InRange(r, w * paper * 0.98f, w * paper * 1.02f);
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, p, out var m, out _, out _);
        Assert.InRange(m, 0.18f * w * paper * 0.95f, 0.18f * w * paper * 1.05f);
    }

    /// <summary>Option A: no mid-tone step across the old PaperWhiteWhenHdr threshold.</summary>
    [Fact]
    public void Continuity_AcrossFormerPaperWhiteThreshold()
    {
        // Old bug: Lc≤1.0101 → whiteOut=w; Lc>1.0101 → whiteOut=0.9w (10% step).
        // Option A: PaperWhiteFraction is continuous — jump across 1.0101 is tiny.
        var lo = new ToneMapParams(StatsLc(1.0100f, p50: 0.5f), DisplayTarget.Sdr203, 0f);
        var hi = new ToneMapParams(StatsLc(1.0102f, p50: 0.5f), DisplayTarget.Sdr203, 0f);
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, lo, out var m0, out _, out _);
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, hi, out var m1, out _, out _);
        ToneMapper.MapPixel(1f, 1f, 1f, lo, out var w0, out _, out _);
        ToneMapper.MapPixel(1f, 1f, 1f, hi, out var w1, out _, out _);
        Assert.True(Math.Abs(m0 - m1) < 1e-4f, $"mid-grey jump across 1.0101: {m0} vs {m1}");
        Assert.True(Math.Abs(w0 - w1) < 1e-4f, $"ref-white jump across 1.0101: {w0} vs {w1}");
    }

    [Fact]
    public void PaperWhiteFraction_Lc1_IsUnity_AndSmoothDip()
    {
        Assert.Equal(1f, SdrToneCurve.PaperWhiteFraction(1f), 5);
        Assert.Equal(1f, SdrToneCurve.PaperWhiteFraction(0.5f), 5);
        // MapScalar floors Lc at 1.01 — C¹ formula keeps paper ≈1 there (not 0.999 step).
        var f101 = SdrToneCurve.PaperWhiteFraction(1.01f);
        Assert.InRange(f101, 0.9999f, 1.0001f);
        var f15 = SdrToneCurve.PaperWhiteFraction(1.5f);
        var f4 = SdrToneCurve.PaperWhiteFraction(4f);
        var f59 = SdrToneCurve.PaperWhiteFraction(59.2f);
        Assert.True(f101 > f15 && f15 > f4 && f4 > f59, $"not monotonic dip: {f101},{f15},{f4},{f59}");
        Assert.InRange(f59, 1f - SdrToneCurve.MaxPaperDip - 1e-3f, 1f - SdrToneCurve.MaxPaperDip + 0.02f);
        Assert.True(f59 >= 1f - 0.12f - 1e-4f && f59 <= 1f - 0.08f + 0.02f);
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

    /// <summary>
    /// Option A / P1: paper dips below white when Lc&gt;1; shoulder reaches display
    /// white at Lc; SDR output ≤ 1.0 (1.12× headroom is HDR-target-only).
    /// </summary>
    [Fact]
    public void P1_SdrShoulder_ReachesDisplayWhiteAtLc_StrictlyIncreasing()
    {
        var stats = StatsLc(8f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var atWhite = ToneMapper.MapScalar(1f, p);
        var paper = SdrToneCurve.PaperWhiteFraction(8f);
        Assert.InRange(atWhite, paper * 0.98f, paper * 1.02f);
        Assert.True(atWhite < 0.98f, $"paper should sit below display white when Lc>1, got {atWhite}");
        float prev = atWhite;
        for (var i = 1; i <= 32; i++)
        {
            var x = 1f + ((stats.Lc - 1f) * (i / 32f));
            var y = ToneMapper.MapScalar(x, p);
            Assert.True(y > prev + 1e-6f, $"P1 shoulder flat at {x}: {y} ≤ {prev}");
            Assert.True(y <= 1.0001f, $"SDR MapScalar({x})={y} > 1");
            prev = y;
        }

        Assert.InRange(ToneMapper.MapScalar(stats.Lc, p), 0.98f, 1.02f);
    }

    [Fact]
    public void P1_MapPixelAndLut_Sdr_StrictlyIncreasing_AtMostDisplayWhite()
    {
        var stats = StatsLc(8f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var lut = ToneMapLut.Bake(p, size: 256);
        float prev = -1f, lutPrev = -1f;
        foreach (var x in new[] { 1f, 1.2f, 2f, 4f, 8f })
        {
            ToneMapper.MapPixel(x, x, x, p, out var r, out _, out _);
            Assert.True(r > prev + 1e-4f, $"P1 MapPixel flat: {x} → {r} ≤ {prev}");
            Assert.True(r <= 1.0001f, $"SDR MapPixel({x})={r} > 1");
            prev = r;
            lut.MapPixel(x, x, x, out var lr, out _, out _);
            Assert.True(lr > lutPrev + 1e-4f, $"P1 LUT flat: {x} → {lr} ≤ {lutPrev}");
            Assert.True(lr <= 1.0001f, $"SDR LUT({x})={lr} > 1");
            lutPrev = lr;
        }

        Assert.InRange(prev, 0.98f, 1.02f);
    }

    /// <summary>
    /// P3 / S-A: chroma ratio g/r of (1, 0.25, 0.08) must stay near 0.25 at
    /// scales ×4 / ×8 / ×12 with Lc=4. Fails on 7c04018 Y-restore soft-clip
    /// which pushed g/r to ~0.46.
    /// </summary>
    [Fact]
    public void P3_SaturatedHighlights_NotOneWashedColour()
    {
        const float grTarget = 0.25f;
        var stats = StatsLc(4f, p50: 0.5f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        foreach (var scale in new[] { 4f, 8f, 12f })
        {
            ToneMapper.MapPixel(1f * scale, 0.25f * scale, 0.08f * scale, p, out var r, out var g, out _);
            Assert.True(r > 1e-6f, $"P3 scale {scale}: r≈0");
            var gr = g / r;
            Assert.True(Math.Abs(gr - grTarget) <= 0.02f,
                $"P3 g/r at ×{scale}: {gr} (7c04018≈0.46; want ~{grTarget}±0.02) rgb=({r},{g})");
        }
    }

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
        Assert.True(r > g && g > b);
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
            // 256 PQ samples are coarse on the 1..Lc shoulder (headroom).
            Assert.InRange(r, direct[o] - 0.04f, direct[o] + 0.04f);
            Assert.InRange(g, direct[o + 1] - 0.04f, direct[o + 1] + 0.04f);
            Assert.InRange(b, direct[o + 2] - 0.04f, direct[o + 2] + 0.04f);
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

    /// <summary>
    /// P2 SDR: +3 EV grades through the Option A shoulder; output ≤ display white.
    /// </summary>
    [Fact]
    public void P2_PlusThreeEv_MapPixel_SoftRollNotHardClip()
    {
        var stats = StatsLc(4f, p50: 0.5f);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 3f);
        // Post-gain ×8: 1.2, 2.4, 4.0 — on the 1..Lc·8 shoulder (lcEff=32).
        ToneMapper.MapPixel(0.15f, 0.15f, 0.15f, p, out var a, out _, out _);
        ToneMapper.MapPixel(0.30f, 0.30f, 0.30f, p, out var b, out _, out _);
        ToneMapper.MapPixel(0.50f, 0.50f, 0.50f, p, out var c, out _, out _);
        Assert.True(a < b && b < c, $"P2 hard-clip: {a}, {b}, {c}");
        Assert.True(c <= 1.0001f, $"SDR +3 EV output {c} exceeds display white");
        Assert.True(Math.Abs(a - b) > 1e-3f && Math.Abs(b - c) > 1e-3f);
    }

    /// <summary>
    /// P2 HDR: +3 EV must keep a strict rise past Analyze Lc.
    /// Fails on 7c04018 where MapScalar clamped to Analyze Lc (y2==y3).
    /// </summary>
    [Fact]
    public void P2_PlusThreeEv_HdrPath_RollsOffNotHardClamp()
    {
        var stats = StatsLc(5f, p50: 0.4f);
        var display = DisplayTarget.Hdr(1000f);
        var p = new ToneMapParams(stats, display, 3f);
        // Post-gain samples: 4 (&lt; Lc·8 domain interior), 8, 16 — 7c04018 clamped at Lc=5.
        var y1 = ToneMapper.MapScalar(0.5f * 8f, p);
        var y2 = ToneMapper.MapScalar(1.0f * 8f, p);
        var y3 = ToneMapper.MapScalar(2.0f * 8f, p);
        Assert.True(y1 + 0.02f < y2, $"P2 HDR no rise 4→8: {y1} → {y2}");
        Assert.True(y2 + 0.005f < y3 || Math.Abs(y3 - display.Ld) < 1e-3f && y2 + 0.002f < y3,
            $"P2 HDR hard-clamp at Analyze Lc: {y1}, {y2}, {y3} (Ld={display.Ld})");
        Assert.True(y3 <= display.Ld + 1e-3f);
        // Explicit 7c04018 failure mode: identical outputs once past Analyze Lc.
        Assert.True(Math.Abs(y2 - y3) > 1e-4f || y2 < display.Ld - 0.05f,
            $"P2 HDR flat past Lc: y2={y2} y3={y3}");
    }

    /// <summary>Option A: strict rise knee→Lc (paper→display white on the shoulder).</summary>
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
