using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public class HdrJxlPresentTests
{
    [Fact]
    public void ShouldPresentViaLibjxl_OnlyPresentableHdrJxl()
    {
        Assert.True(HdrJxlPresent.ShouldPresentViaLibjxl(new HdrProbe(HdrKind.HdrJxl, 9, 16, null)));
        Assert.True(HdrJxlPresent.ShouldPresentViaLibjxl(new HdrProbe(HdrKind.HdrJxl, 9, 18, null)));
        // Gain-map / wide-gamut / SDR base: libjxl still decodes the base, but
        // present stays BitmapImage — never the HDR scRGB swapchain path.
        Assert.False(HdrJxlPresent.ShouldPresentViaLibjxl(new HdrProbe(HdrKind.WideGamutJxl, 11, 13, null)));
        Assert.False(HdrJxlPresent.ShouldPresentViaLibjxl(new HdrProbe(HdrKind.HdrAvif, 9, 16, 1000)));
        Assert.False(HdrJxlPresent.ShouldPresentViaLibjxl(HdrProbe.None));
    }

    [Fact]
    public void SkipWicPresent_HdrJxlOrJxlExtension()
    {
        var hdr = new HdrProbe(HdrKind.HdrJxl, 9, 16, null);
        Assert.True(HdrJxlPresent.SkipWicPresent(hdr, @"C:\a.jxl"));
        // Presentable HDR JXL still skips WIC even without a .jxl path.
        Assert.True(HdrJxlPresent.SkipWicPresent(hdr, @"C:\captures\still"));
        Assert.True(HdrJxlPresent.SkipWicPresent(new HdrProbe(HdrKind.WideGamutJxl, 11, 13, null), @"D:\p3.jxl"));
        Assert.True(HdrJxlPresent.SkipWicPresent(HdrProbe.None, "x.JXL"));
        Assert.False(HdrJxlPresent.SkipWicPresent(new HdrProbe(HdrKind.HdrAvif, 9, 16, 1000), @"C:\a.avif"));
        Assert.False(HdrJxlPresent.SkipWicPresent(HdrProbe.None, @"C:\a.png"));
    }

    [Fact]
    public void ScrgbOutputRenderingIntent_IsRelativeNotPerceptual()
    {
        // Relative + BT.2020 primaries keep OOG; plain LinearSRGB Relative
        // would collapse P3. (Old washout was MidGrey boost up to 4×.)
        Assert.Equal(1, HdrJxlPresent.ScrgbOutputRenderingIntentRelative);
        Assert.NotEqual(0, HdrJxlPresent.ScrgbOutputRenderingIntentRelative);
    }

    [Fact]
    public void UsesBt2020LinearOutput_PqHlgAndWideGamutSdr()
    {
        Assert.True(HdrJxlPresent.UsesBt2020LinearOutput(HdrTransfer.Pq, 9));
        Assert.True(HdrJxlPresent.UsesBt2020LinearOutput(HdrTransfer.Hlg, null));
        // 11 = JXL's own P3; 12 = CICP Display P3; 9 = BT.2020.
        Assert.True(HdrJxlPresent.UsesBt2020LinearOutput(HdrTransfer.Srgb, 11));
        Assert.True(HdrJxlPresent.UsesBt2020LinearOutput(HdrTransfer.Srgb, 12));
        Assert.True(HdrJxlPresent.UsesBt2020LinearOutput(HdrTransfer.Srgb, 9));
        Assert.False(HdrJxlPresent.UsesBt2020LinearOutput(HdrTransfer.Srgb, 1));
        Assert.False(HdrJxlPresent.UsesBt2020LinearOutput(HdrTransfer.Srgb, null));
        Assert.False(HdrJxlPresent.UsesBt2020LinearOutput(HdrTransfer.Linear, 1));

        // Header-shaped probes matching StillFormatTests PQ + P3 SDR samples.
        var pq = new HdrProbe(HdrKind.HdrJxl, 9, 16, null);
        Assert.Equal(HdrTransfer.Pq, pq.Transfer);
        Assert.True(HdrJxlPresent.UsesBt2020LinearOutput(pq.Transfer, pq.CicpPrimaries));
        var p3Sdr = new HdrProbe(HdrKind.WideGamutJxl, 11, 13, null);
        Assert.Equal(HdrTransfer.Srgb, p3Sdr.Transfer);
        Assert.True(HdrJxlPresent.UsesBt2020LinearOutput(p3Sdr.Transfer, p3Sdr.CicpPrimaries));
    }

    [Fact]
    public void WideGamutSdrJxl_Bt2020Route_KeepsOogNotRec709Only()
    {
        // Pure BT.2020 red after matrix → negatives → Gamut outside Rec.709.
        // LinearSRGB Relative collapse would be Rec.709 red with no OOG.
        var rgba = new float[] { 1f, 0f, 0f, 1f };
        HdrJxlPresent.Bt2020LinearToScrgb(rgba);
        Assert.True(rgba[1] < 0f || rgba[2] < 0f);
        var gamut = CieGamut.ClassifyScrgb(rgba[0], rgba[1], rgba[2], yNits: 80f);
        Assert.NotEqual(GamutKind.Rec709, gamut);
    }

    [Fact]
    public void ScrgbScaleFromIntensityTarget_RoutesOnPqHlgTransfer()
    {
        // PQ intensity target 10 000 nits → scRGB 1.0 = 80 nits.
        Assert.Equal(
            10_000f / GalleryPresent.ScrgbNits,
            HdrJxlPresent.ScrgbScaleFromIntensityTarget(10_000f, HdrTransfer.Pq),
            3);
        Assert.Equal(
            1600f / GalleryPresent.ScrgbNits,
            HdrJxlPresent.ScrgbScaleFromIntensityTarget(1600f, HdrTransfer.Hlg),
            3);
        // Missing (≤0): PQ → 10 000, HLG → 1000. libjxl default 255 is real
        // (1.0 = 255 nits under Relative) — do not remap to 10k (Kalan whiteout).
        Assert.Equal(10_000f / GalleryPresent.ScrgbNits, HdrJxlPresent.ScrgbScaleFromIntensityTarget(0f, HdrTransfer.Pq), 3);
        Assert.Equal(255f / GalleryPresent.ScrgbNits, HdrJxlPresent.ScrgbScaleFromIntensityTarget(255f, HdrTransfer.Pq), 3);
        Assert.Equal(1000f / GalleryPresent.ScrgbNits, HdrJxlPresent.ScrgbScaleFromIntensityTarget(0f, HdrTransfer.Hlg), 3);
        Assert.Equal(255f / GalleryPresent.ScrgbNits, HdrJxlPresent.ScrgbScaleFromIntensityTarget(255f, HdrTransfer.Hlg), 3);
        // SDR / sRGB transfer stays relative 0..1 — even if IntensityTarget looks high.
        Assert.Equal(1f, HdrJxlPresent.ScrgbScaleFromIntensityTarget(10_000f, HdrTransfer.Srgb));
        Assert.Equal(1f, HdrJxlPresent.ScrgbScaleFromIntensityTarget(255f, HdrTransfer.Srgb));
        Assert.Equal(1f, HdrJxlPresent.ScrgbScaleFromIntensityTarget(100f, HdrTransfer.Linear));
        // Never use BT.2408 paper white (203) as the scRGB divisor.
        Assert.NotEqual(10_000f / 203f, HdrJxlPresent.ScrgbScaleFromIntensityTarget(10_000f, HdrTransfer.Pq), 3);
    }

    [Fact]
    public void ToneMappedPqJxl_KalanLike_NotBlownOut()
    {
        // Mid-grey scene + paper-white patch + small highlight (KalanLiraDuo):
        // exposure 1 + Reinhard — mean readable, not ~1 washout.
        const int n = 64;
        var rgba = new float[n * 4];
        var paper = GalleryPresent.SdrReferenceNits / GalleryPresent.ScrgbNits;
        var mid = 0.5f;
        var highlight = 1000f / GalleryPresent.ScrgbNits;
        for (var i = 0; i < n; i++)
        {
            var v = i == 0 ? highlight : i < 8 ? paper : mid;
            rgba[i * 4] = v;
            rgba[i * 4 + 1] = v;
            rgba[i * 4 + 2] = v;
            rgba[i * 4 + 3] = 1f;
        }

        var (exposure, white) = ToneMap.AnalyzeHdrPresent(rgba, n);
        Assert.Equal(1f, exposure);
        Assert.True(white > 1f);
        var mean = HdrJxlPresent.ToneMappedMeanLuminance(rgba, n);
        Assert.InRange(mean, 0.08f, 0.85f);
        var p90 = PercentileLumaAfterPresent(rgba, n, 0.90);
        Assert.InRange(p90, 0.15f, 0.98f);
        // Paper patch must not stay above composition white after map.
        ToneMap.MapPresentRgb(
            paper, paper, paper, exposure, 1f, white, softNegatives: false,
            out var pr, out _, out _);
        Assert.InRange(pr, 0.05f, 1f);
    }

    [Fact]
    public void IntensityTarget255_RelativeScale_NotTenThousand()
    {
        // Laptop: Kalan / room.jxl with libjxl default IntensityTarget=255.
        // Treating 255 as unset (→10k/80) over-scaled ~39×; after Reinhard the
        // whole frame sat at composition white (median 255, >50% clipped).
        var wrong = HdrJxlPresent.ScrgbScaleFromIntensityTarget(255f, HdrTransfer.Pq);
        Assert.Equal(255f / GalleryPresent.ScrgbNits, wrong, 3);
        Assert.True(wrong < 10f, "must not use 10k/80 for stamped 255");
    }

    [Fact]
    public void ToneMappedPqJxl_Overscaled255AsTenK_WouldWhiteout_CorrectScaleDoesNot()
    {
        // Simulate Relative float where 1.0 = 255 nits, then apply scale.
        // Wrong path (255→10k): midtones land near peak → tonemap washout.
        // Correct path (255/80): midtones stay mid after Reinhard.
        const int n = 64;
        var relative = new float[n * 4];
        for (var i = 0; i < n; i++)
        {
            // ~media white + mid + small highlight in IntensityTarget units.
            var v = i == 0 ? 1f : i < 8 ? 0.8f : 0.35f;
            relative[i * 4] = relative[i * 4 + 1] = relative[i * 4 + 2] = v;
            relative[i * 4 + 3] = 1f;
        }

        var wrongScale = 10_000f / GalleryPresent.ScrgbNits;
        var rightScale = HdrJxlPresent.ScrgbScaleFromIntensityTarget(255f, HdrTransfer.Pq);
        var blown = (float[])relative.Clone();
        var ok = (float[])relative.Clone();
        HdrJxlPresent.ApplyIntensityScale(blown, wrongScale);
        HdrJxlPresent.ApplyIntensityScale(ok, rightScale);

        var blownMean = HdrJxlPresent.ToneMappedMeanLuminance(blown, n);
        var okMean = HdrJxlPresent.ToneMappedMeanLuminance(ok, n);
        var blownClip = FractionClippedAfterPresent(blown, n);
        var okClip = FractionClippedAfterPresent(ok, n);

        // Wrong scale: almost-white after map (laptop retest: ~55% clipped).
        Assert.True(blownMean > 0.85f || blownClip > 0.45f,
            $"expected whiteout under 10k scale, mean={blownMean} clip={blownClip}");
        // Correct scale: readable midtones, low clip rate.
        Assert.InRange(okMean, 0.10f, 0.75f);
        Assert.True(okClip < 0.25f, $"expected low clip under 255 scale, clip={okClip}");
    }

    private static float FractionClippedAfterPresent(float[] rgba, int n)
    {
        var (exposure, white) = ToneMap.AnalyzeHdrPresent(rgba, n);
        var clipped = 0;
        for (var i = 0; i < n; i++)
        {
            ToneMap.MapPresentRgb(
                rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2],
                exposure, 1f, white, softNegatives: false,
                out var r, out var g, out var b);
            if (Math.Max(r, Math.Max(g, b)) >= 0.98f)
            {
                clipped++;
            }
        }

        return clipped / (float)n;
    }

    [Fact]
    public void AnalyzeHdrPresent_ExposureOne_AlwaysShoulder_NoBoost()
    {
        // Dark frame: old MiddleGrey/logAvg could boost up to 4× and overblow.
        // Present stays at exposure 1.0 (fallback SdrWhite=80) and still rolls.
        const int n = 32;
        var rgba = new float[n * 4];
        for (var i = 0; i < n; i++)
        {
            var v = i == 0 ? 0.08f : 0.05f;
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = v;
            rgba[i * 4 + 3] = 1f;
        }

        var thumb = ToneMap.Analyze(rgba, n);
        Assert.Equal(1f, thumb.Exposure);
        Assert.Equal(1f, thumb.White); // p995 ≤ 1 → thumb passthrough

        var present = ToneMap.AnalyzeHdrPresent(rgba, n);
        Assert.Equal(1f, present.Exposure);
        Assert.True(present.White > 1f, "shoulder always on even when p995 ≤ 1");
        Assert.True(present.Exposure <= 1f);
    }

    [Fact]
    public void AnalyzeHdrPresent_UsesSdrWhiteLevel_AttenuatesOnly()
    {
        Span<float> rgba = stackalloc float[] { 1f, 1f, 1f, 1f };
        Assert.Equal(80f, GalleryPresent.EffectiveSdrWhiteNits(0f));
        Assert.Equal(80f, GalleryPresent.EffectiveSdrWhiteNits(-1f));
        Assert.Equal(203f, GalleryPresent.EffectiveSdrWhiteNits(203f));
        Assert.Equal(1f, GalleryPresent.PresentSdrExposure(0f));
        Assert.Equal(1f, GalleryPresent.PresentSdrExposure(80f));
        Assert.Equal(80f / 203f, GalleryPresent.PresentSdrExposure(203f), 4);
        Assert.True(GalleryPresent.PresentSdrExposure(40f) <= 1f); // never boost

        var at80 = ToneMap.AnalyzeHdrPresent(rgba, 1, sdrWhiteLevelNits: 80f);
        Assert.Equal(1f, at80.Exposure);
        var at203 = ToneMap.AnalyzeHdrPresent(rgba, 1, sdrWhiteLevelNits: 203f);
        Assert.Equal(80f / 203f, at203.Exposure, 4);
        Assert.True(at203.White > 1f);
    }

    [Fact]
    public void AnalyzeHdrPresent_PregmataSdrReference_MeanAndPercentileInTolerance()
    {
        // SDR reference PNG for Pregmata: mid-grey scene + soft highlight at
        // scRGB white — mean and p90 stay in a readable band (not crushed /
        // not washed) with exposure 1 + always-on shoulder.
        const int n = 64;
        var rgba = new float[n * 4];
        for (var i = 0; i < n; i++)
        {
            var v = i == 0 ? 1f : i < 8 ? 0.5f : 0.18f;
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = v;
            rgba[i * 4 + 3] = 1f;
        }

        var (exposure, white) = ToneMap.AnalyzeHdrPresent(rgba, n);
        Assert.Equal(1f, exposure);
        Assert.True(white > 1f);
        var mean = HdrJxlPresent.ToneMappedMeanLuminance(rgba, n);
        Assert.InRange(mean, 0.10f, 0.45f);
        var p50 = PercentileLumaAfterPresent(rgba, n, 0.50);
        var p90 = PercentileLumaAfterPresent(rgba, n, 0.90);
        Assert.InRange(p50, 0.08f, 0.35f);
        Assert.InRange(p90, 0.20f, 0.95f);
    }

    [Fact]
    public void AnalyzeHdrPresent_RelativeExr_ShoulderWithoutBoost()
    {
        // Scene-referred EXR diffuse ≤ 1.0: thumbs skip (1,1); present always
        // shoulders. Exposure stays 1 so scene 1.0 → SDR white (no MidGrey boost).
        const int n = 32;
        var rgba = new float[n * 4];
        for (var i = 0; i < n; i++)
        {
            var v = i == 0 ? 0.95f : 0.7f;
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = v;
            rgba[i * 4 + 3] = 1f;
        }

        var thumb = ToneMap.Analyze(rgba, n);
        Assert.Equal(1f, thumb.Exposure);
        Assert.Equal(1f, thumb.White);

        var present = ToneMap.AnalyzeHdrPresent(rgba, n);
        Assert.Equal(1f, present.Exposure);
        Assert.True(present.White > 1f);
        var mean = HdrJxlPresent.ToneMappedMeanLuminance(rgba, n);
        Assert.InRange(mean, 0.40f, 0.95f);
    }

    private static float PercentileLumaAfterPresent(float[] rgba, int n, double fraction)
    {
        var (exposure, white) = ToneMap.AnalyzeHdrPresent(rgba, n);
        var samples = new float[n];
        for (var i = 0; i < n; i++)
        {
            ToneMap.MapPresentRgb(
                rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2],
                exposure, 1f, white, softNegatives: false,
                out var r, out var g, out var b);
            samples[i] = ToneMap.Luminance(r, g, b);
        }

        Array.Sort(samples);
        return samples[Math.Min(n - 1, (int)(n * fraction))];
    }

    [Fact]
    public void Bt2020LinearToScrgb_KeepsOutOfGamutNegatives()
    {
        // Pure BT.2020 red → negative G/B in BT.709.
        Span<float> rgba = stackalloc float[] { 1f, 0f, 0f, 1f };
        HdrJxlPresent.Bt2020LinearToScrgb(rgba);
        Assert.True(rgba[0] > 0f);
        Assert.True(rgba[1] < 0f || rgba[2] < 0f);
    }

    [Fact]
    public void ApplyIntensityScale_Stride4_MultipliesRgbLeavesAlpha()
    {
        Span<float> rgba = stackalloc float[] { 0.5f, 1f, 2f, 0.25f, 0.1f, 0.2f, 0.3f, 1f };
        HdrJxlPresent.ApplyIntensityScale(rgba, 2f);
        Assert.Equal(1f, rgba[0], 4);
        Assert.Equal(2f, rgba[1], 4);
        Assert.Equal(4f, rgba[2], 4);
        Assert.Equal(0.25f, rgba[3], 4);
        Assert.Equal(0.2f, rgba[4], 4);
        Assert.Equal(0.4f, rgba[5], 4);
        Assert.Equal(0.6f, rgba[6], 4);
        Assert.Equal(1f, rgba[7], 4);

        HdrJxlPresent.ApplyIntensityScale(rgba, 1f); // no-op
        Assert.Equal(1f, rgba[0], 4);
    }

    [Fact]
    public void BuildPresentFrame_KeepsNativeStatsAndBoxScalesPixels()
    {
        // 4×4 with one highlight; PresentDecodeSize needs viewport ≥2 to Fit-downscale.
        const float highlight = 1000f / GalleryPresent.ScrgbNits;
        var rgba = new float[4 * 4 * 4];
        for (var i = 0; i < 16; i++)
        {
            rgba[i * 4] = 1f;
            rgba[i * 4 + 1] = 1f;
            rgba[i * 4 + 2] = 1f;
            rgba[i * 4 + 3] = 1f;
        }

        rgba[0] = highlight;
        rgba[1] = 0f;
        rgba[2] = 0f;

        var frame = HdrJxlPresent.BuildPresentFrame(
            rgba, 4, 4, viewportPixelWidth: 2, viewportPixelHeight: 2, ImageScaling.Fit);
        Assert.NotNull(frame);
        Assert.Equal(2, frame!.Width);
        Assert.Equal(2, frame.Height);
        Assert.Equal(16, frame.ScrgbRgba.Length);
        // Native MaxCLL / MaxNits survive the Fit box-scale (same as EXR).
        Assert.Equal(highlight, frame.MaxScrgb, 3);
        Assert.Equal('R', frame.MaxScrgbChannel);
        Assert.True(frame.MaxNits > GalleryPresent.SdrReferenceNits);
    }

    [Fact]
    public void BuildPresentFrame_RejectsBadBuffers()
    {
        Assert.Null(HdrJxlPresent.BuildPresentFrame(null!, 1, 1, 0, 0, ImageScaling.Fit));
        Assert.Null(HdrJxlPresent.BuildPresentFrame(new float[4], 0, 1, 0, 0, ImageScaling.Fit));
        Assert.Null(HdrJxlPresent.BuildPresentFrame(new float[4], 2, 2, 0, 0, ImageScaling.Fit));
    }

    [Fact]
    public void BuildPresentFrame_PqHighlight_SdrTonemapHasDataForInfo()
    {
        // Simulate post-Normalize PQ white at ~203 nits (paper white) in scRGB.
        var paper = GalleryPresent.SdrReferenceNits / GalleryPresent.ScrgbNits;
        var rgba = new float[] { paper, paper, paper, 1f };
        var frame = HdrJxlPresent.BuildPresentFrame(rgba, 1, 1, 0, 0, ImageScaling.Fit);
        Assert.NotNull(frame);
        Assert.Equal(paper, frame!.MaxScrgb, 3);
        Assert.Equal(GalleryPresent.SdrReferenceNits, frame.MaxNits, 1);
        Assert.True(frame.AvgNits > 0);
        // SDR PresentMap needs a positive content MaxCLL — heatmap / Info use this.
        var map = GalleryPresent.PresentMap(false, frame.MaxNits, 0);
        Assert.True(map.Scale > 0);
        Assert.True(map.Scale <= 1f);
    }

    [Fact]
    public void BuildPresentFrame_PreservesOutOf709Negatives()
    {
        // After BT.2020 → BT.709, a channel can go negative — keep for gamut.
        var rgba = new float[] { 0.1f, -0.05f, 0.2f, 1f };
        var frame = HdrJxlPresent.BuildPresentFrame(rgba, 1, 1, 0, 0, ImageScaling.Fit);
        Assert.NotNull(frame);
        Assert.Equal(-0.05f, frame!.ScrgbRgba[1], 4);
    }

    [Fact]
    public void BuildPresentFrame_HonorsCancellationBeforeBoxScale()
    {
        var rgba = new float[4 * 4 * 4];
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            HdrJxlPresent.BuildPresentFrame(rgba, 4, 4, 2, 2, ImageScaling.Fit, cts.Token));
    }

    [Fact]
    public void PixelCaps_Present64M_Thumb120M()
    {
        Assert.Equal(64_000_000, HdrJxlPresent.PresentMaxPixels);
        Assert.Equal(120_000_000, HdrJxlPresent.ThumbMaxPixels);
        Assert.True(HdrJxlPresent.ThumbMaxPixels > HdrJxlPresent.PresentMaxPixels);
    }

    [Fact]
    public void ToneMap_EightyNitScale_PullsHotPaperWhiteDown()
    {
        // Post-Normalize PQ paper white at 203/80 ≈ 2.54 scRGB — ToneMap must
        // expose down (not leave thumbs ~2.5× hotter than the old ÷203 path).
        var paper = GalleryPresent.SdrReferenceNits / GalleryPresent.ScrgbNits;
        Span<float> rgba = stackalloc float[] { paper, paper, paper, 1f };
        var (exposure, white) = ToneMap.Analyze(rgba, 1);
        Assert.True(exposure < 1f, $"expected exposure < 1 for hot paper white, got {exposure}");
        Assert.True(white >= 1f);
        var bgra = ToneMap.ScrgbToBgra8(rgba, 1, 1);
        Assert.InRange(bgra[0], 1, 254); // not clipped black or full white wash
    }

    [Fact]
    public void MapPresentRgb_SdrReinhard_MatchesThumbCurveShape()
    {
        var paper = GalleryPresent.SdrReferenceNits / GalleryPresent.ScrgbNits;
        Span<float> rgba = stackalloc float[] { paper, paper, paper, 1f };
        var (exposure, white) = ToneMap.Analyze(rgba, 1);
        ToneMap.MapPresentRgb(
            paper, paper, paper, exposure, 1f, white, softNegatives: true,
            out var r, out var g, out var b);
        Assert.InRange(r, 0.05f, 1f);
        Assert.Equal(r, g, 4);
        Assert.Equal(g, b, 4);
    }

    [Fact]
    public void MapPresentRgb_HdrPassthrough_AbsoluteScrgb()
    {
        var map = GalleryPresent.PresentMap(true, 1000, 1000);
        Assert.Equal(1f, map.Scale);
        Assert.Equal(1000f / 80f, map.ClipScrgb, 3);
        ToneMap.MapPresentRgb(
            1f, 1f, 1f, map.Scale, map.ClipScrgb, reinhardWhite: 0f, softNegatives: true,
            out var r, out _, out _);
        Assert.Equal(1f, r, 3);
        // OOG negative passes through on HDR.
        ToneMap.MapPresentRgb(
            -0.1f, 0.5f, 0.2f, 1f, 10f, 0f, softNegatives: true,
            out var nr, out _, out _);
        Assert.True(nr < 0f);
    }

    [Fact]
    public void HlgOotf_AppliesOnLuminanceNotPerChannel()
    {
        // EncodedToNits is EOTF only; OOTF is luminance-based.
        var scene = GalleryPresent.HlgEotf(0.75f);
        Assert.Equal(scene * 1000f, GalleryPresent.EncodedToNits(0.75f, HdrTransfer.Hlg), 3);
        Assert.Equal(
            scene * MathF.Pow(scene, GalleryPresent.HlgOotfGamma - 1f) * 1000f,
            GalleryPresent.HlgOotfNits(scene),
            3);

        // Pure red vs grey: shared Y^(γ−1) scale, not independent channel OOTF.
        GalleryPresent.HlgOotfRgb(0.5f, 0f, 0f, bt2020: true, out var rNits, out _, out _);
        var y = GalleryPresent.LuminanceY(0.5f, 0f, 0f, bt2020: true);
        var k = MathF.Pow(y, GalleryPresent.HlgOotfGamma - 1f) * 1000f;
        Assert.Equal(0.5f * k, rNits, 3);
        Assert.NotEqual(GalleryPresent.HlgOotfNits(0.5f), rNits, 2);
    }
}
