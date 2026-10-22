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

    /// <summary>N1: above ref white the SDR curve must rise (real shoulder), not flat-clip.</summary>
    [Fact]
    public void N1_SdrShoulder_StrictlyIncreasingAboveRefWhite()
    {
        var stats = new ImageStats(
            P50: 0.55f,
            P99995: 8f,
            Max: 8f,
            MaxRgb: 8f,
            Lc: 8f,
            RefWhite: 1f,
            MetadataPeak: 10_000f,
            SourceClipped: false,
            SourceClipRatio: 0);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        float prev = ToneMapper.MapScalar(1f, p);
        Assert.InRange(prev, 0.98f, 1.02f);
        for (var i = 1; i <= 32; i++)
        {
            var x = 1f + ((stats.Lc - 1f) * (i / 32f));
            var y = ToneMapper.MapScalar(x, p);
            Assert.True(y > prev + 1e-5f, $"N1 flat/non-increasing at x={x}: y={y} prev={prev}");
            prev = y;
        }
    }

    /// <summary>N2: tone-mapped output at/near Ld must keep chroma — not greyed by desat.</summary>
    [Fact]
    public void N2_SaturatedHighlight_NotGreyedAtDisplayWhite()
    {
        var stats = new ImageStats(0.5f, 4f, 4f, 4f, 4f, 1f, null, false, 0);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        // Saturated warm at ref-white maxRGB → maps to display white; desat must be ~0 at Ld.
        ToneMapper.MapPixel(1f, 0.25f, 0.08f, p, out var r, out var g, out var b);
        var peak = Math.Max(r, Math.Max(g, b));
        Assert.InRange(peak, 0.85f, 1.05f);
        var y = ImagingConstants.Bt2020Y(r, g, b);
        // Channel spread must remain — greying to Y would collapse chroma.
        Assert.True(Math.Abs(r - y) > 0.08f, $"N2 highlight greyed: rgb=({r},{g},{b}) Y={y}");
        Assert.True(r > g && g > b, $"N2 hue collapsed: rgb=({r},{g},{b})");
    }

    /// <summary>N3: BT.2390 EETF must stay ≤ Ld and be monotonic (no e1&gt;1 blow-up).</summary>
    [Fact]
    public void N3_Bt2390_BoundedAndMonotonic_Lc5_At1000Nit()
    {
        const float lc = 5f;
        var ld = DisplayTarget.Hdr(1000f).Ld; // ≈ 4.926
        float prev = -1f;
        // Includes values that used to overshoot when e1 was unclamped (5.5, 6).
        float[] xs = [0f, 0.5f, 1f, 2f, 4f, 5f, 5.5f, 6f, 10f];
        foreach (var x in xs)
        {
            var y = Bt2390Eetf.MapLinear(x, lc, ld);
            Assert.True(y <= ld + 1e-4f, $"N3 overshoot x={x} → {y} > Ld={ld}");
            Assert.True(y + 1e-5f >= prev, $"N3 non-monotonic x={x}: {y} < {prev}");
            if (x > 0f)
            {
                Assert.True(y >= prev, $"N3 must not go flat-backwards at x={x}");
            }

            prev = y;
        }

        // Past Lc, clamp keeps output at the same bound as x=Lc.
        Assert.Equal(Bt2390Eetf.MapLinear(lc, lc, ld), Bt2390Eetf.MapLinear(6f, lc, ld), 4);
    }

    [Fact]
    public void N4_DisplayTarget_ContentHash_StableAcrossCalls()
    {
        var a = DisplayTarget.Sdr203.ContentHash;
        var b = DisplayTarget.Sdr203.ContentHash;
        Assert.Equal(a, b);
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
        var (_, stats) = MasterFromFixture();
        var d = DisplayTarget.Sdr203;
        var lcBefore = stats.Lc;
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, new ToneMapParams(stats, d, 0f), out var r0, out _, out _);
        ToneMapper.MapPixel(0.18f, 0.18f, 0.18f, new ToneMapParams(stats, d, 1f), out var r1, out _, out _);
        Assert.True(r1 > r0 + 1e-4f);
        Assert.Equal(lcBefore, stats.Lc);
    }

    /// <summary>+3 EV must soft-roll (shoulder), not hard-clip every highlight to white.</summary>
    [Fact]
    public void PlusThreeEv_SoftRollOff_NotHardClip()
    {
        var stats = new ImageStats(0.5f, 4f, 4f, 4f, 4f, 1f, null, false, 0);
        // Curve-level check with +3 EV gain baked into x (MapScalar ignores EV).
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        var gain = MathF.Pow(2f, 3f);
        var y1 = ToneMapper.MapScalar(0.15f * gain, p); // 1.2 — just above white
        var y2 = ToneMapper.MapScalar(0.30f * gain, p); // 2.4 — mid shoulder
        var y3 = ToneMapper.MapScalar(0.50f * gain, p); // 4.0 — Lc
        Assert.True(y1 < y2 && y2 < y3, $"hard-clip/flat: {y1}, {y2}, {y3}");
        Assert.True(y1 > 1f, $"expected shoulder above white, got {y1}");
        Assert.True(y3 <= SdrToneCurve.HighlightHeadroom + 1e-3f);

        // MapPixel still clamps display peak after soft-clip.
        ToneMapper.MapPixel(4f, 4f, 4f, new ToneMapParams(stats, DisplayTarget.Sdr203, 3f), out var r, out var g, out var b);
        Assert.True(Math.Max(r, Math.Max(g, b)) <= 1.01f);
    }

    [Fact]
    public void Curve_StrictlyIncreasing_AboveKnee()
    {
        var stats = new ImageStats(0.55f, 6f, 6f, 6f, 6f, 1f, null, false, 0);
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        float prev = -1f;
        for (var i = 0; i <= 64; i++)
        {
            var x = (i / 64f) * stats.Lc;
            var y = ToneMapper.MapScalar(x, p);
            if (x > 0.55f && x < stats.Lc - 1e-3f)
            {
                Assert.True(y > prev + 1e-6f, $"flat/non-increasing at {x}: {y} ≤ {prev}");
            }
            else
            {
                Assert.True(y + 1e-5f >= prev, $"decreasing at {x}: {y} < {prev}");
            }

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

    /// <summary>
    /// Distinct paths: full = direct MapFrame; thumb = LUT@256 MapPixel with shared stats.
    /// Not a tautology — different code, same contract, dE bound.
    /// </summary>
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
            var dE = DeltaE2000.FromWorkingRgb(full[o], full[o + 1], full[o + 2], r, g, b);
            maxDe = Math.Max(maxDe, dE);
        }

        Assert.True(maxDe < 1.5, $"thumb LUT vs full direct dE {maxDe}");
    }

    [Fact]
    public void DeltaE_VsIndependentReference_MidGreyAndWhite()
    {
        var (_, stats) = MasterFromFixture();
        var p = new ToneMapParams(stats, DisplayTarget.Sdr203, 0f);
        // Independent reference: identity placement at 0.18 / 1.0 for Sdr203.
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

    [Fact]
    public void SceneAverage_P50_DrivesKnee()
    {
        // S4: P50 is the scene-average knee — different knee changes Hermite mid-tones.
        var low = new ImageStats(0.5f, 4f, 4f, 4f, 4f, 1f, null, false, 0);
        var high = new ImageStats(0.7f, 4f, 4f, 4f, 4f, 1f, null, false, 0);
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
