using Palace.Helpers;
using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// V3 A/B: DecodePath must not double-EOTF ScrgbRgba. Strip stays
/// encoded + OptionsForEncoded. LoadWic/Avif prefer EncodedRgba when retained
/// so Working / SourceClipped match strip (HLG OOTF, SDR white clip).
/// </summary>
public class SmoothSingleEotfTests
{
    [Fact]
    public void EncodedOptionsForEncoded_Gray128_WorkingApprox0p216()
    {
        float e = 128f / 255f;
        var opts = ImagingStillNormalize.OptionsForEncoded(HdrTransfer.Srgb, cicpPrimaries: 1);
        var frame = ImagingStillNormalize.Run(new float[] { e, e, e, 1f }, 1, 1, opts);
        Assert.InRange(frame.Rgba[0], 0.214f, 0.218f);
        Assert.Equal(frame.Rgba[0], frame.Rgba[1], precision: 5);
        Assert.Equal(frame.Rgba[0], frame.Rgba[2], precision: 5);
    }

    [Fact]
    public void EncodedOptionsForEncoded_White255_WorkingIs1()
    {
        var opts = ImagingStillNormalize.OptionsForEncoded(HdrTransfer.Srgb, cicpPrimaries: 1);
        var frame = ImagingStillNormalize.Run(new float[] { 1f, 1f, 1f, 1f }, 1, 1, opts);
        Assert.Equal(1f, MaxRgb(frame), precision: 5);
    }

    [Fact]
    public void PresentScrgbOptions_MatchesEncodedPath_Gray128AndWhite()
    {
        float e = 128f / 255f;
        var encOpts = ImagingStillNormalize.OptionsForEncoded(HdrTransfer.Srgb, 1);
        var encFrame = ImagingStillNormalize.Run(new float[] { e, e, e, 1f }, 1, 1, encOpts);

        // LoadWic buffer: EncodedRgbToScrgb then OptionsForPresentScrgb.
        HdrColor.EncodedRgbToScrgb(e, e, e, HdrTransfer.Srgb, 1, out var sr, out var sg, out var sb);
        var scrgbOpts = ImagingStillNormalize.OptionsForPresentScrgb();
        Assert.Equal(TransferKind.Scrgb, scrgbOpts.Transfer);
        var scrgbFrame = ImagingStillNormalize.Run(
            new float[] { sr, sg, sb, 1f }, 1, 1, scrgbOpts);

        Assert.Equal(encFrame.Rgba[0], scrgbFrame.Rgba[0], precision: 5);
        Assert.InRange(scrgbFrame.Rgba[0], 0.214f, 0.218f);

        HdrColor.EncodedRgbToScrgb(1f, 1f, 1f, HdrTransfer.Srgb, 1, out sr, out sg, out sb);
        var whiteEnc = ImagingStillNormalize.Run(new float[] { 1f, 1f, 1f, 1f }, 1, 1, encOpts);
        var whiteSc = ImagingStillNormalize.Run(
            new float[] { sr, sg, sb, 1f }, 1, 1, scrgbOpts);
        Assert.Equal(1f, MaxRgb(whiteEnc), precision: 5);
        Assert.Equal(1f, MaxRgb(whiteSc), precision: 5);
        Assert.Equal(MaxRgb(whiteEnc), MaxRgb(whiteSc), precision: 5);
    }

    [Fact]
    public void SourceClipped_SdrWhite255_EncodedTrue_ScrgbFallbackMismatchDocumented()
    {
        // Encoded + OptionsForEncoded (Smooth preferred path): white @ peak → clipped.
        var encOpts = ImagingStillNormalize.OptionsForEncoded(HdrTransfer.Srgb, 1);
        var enc = ImagingStillNormalize.Run(new float[] { 1f, 1f, 1f, 1f }, 1, 1, encOpts);
        Assert.True(enc.SourceClipped);
        Assert.Equal(1f, enc.SourceClipRatio, precision: 5);

        // Scrgb fallback (no MaxCLL): S1 — clipCeil is null, so SourceClipped
        // stays false even when EncodedRgbToScrgb maps SDR white to paper white
        // (203/80 ≈ 2.5375). Do NOT default Scrgb clipCeil to RefWhite/ScrgbNits
        // (that falsely flags JXR HDR highlights without MaxCLL — see S1 guard).
        HdrColor.EncodedRgbToScrgb(1f, 1f, 1f, HdrTransfer.Srgb, 1, out var sr, out var sg, out var sb);
        Assert.InRange(sr, 2.53f, 2.55f);
        var sc = ImagingStillNormalize.Run(
            new float[] { sr, sg, sb, 1f }, 1, 1,
            ImagingStillNormalize.OptionsForPresentScrgb());
        Assert.False(sc.SourceClipped);
        Assert.Equal(0f, sc.SourceClipRatio);
        Assert.NotEqual(enc.SourceClipped, sc.SourceClipped);
    }

    [Fact]
    public void SourceClipped_JxrScrgb_1000NitHighlight_NoMaxCll_False()
    {
        // S1 guard: HDR JXR-style scRGB highlight without MaxCLL must NOT use
        // buffer max / paper-white default as clipCeil.
        const float scrgb1000 = 1000f / ImagingConstants.ScrgbNits; // 12.5
        var opts = ImagingStillNormalize.OptionsForPresentScrgb(metadataPeakNits: null);
        Assert.Equal(TransferKind.Scrgb, opts.Transfer);
        Assert.Null(opts.MetadataPeakNits);
        var frame = ImagingStillNormalize.Run(
            new float[] { scrgb1000, scrgb1000, scrgb1000, 1f }, 1, 1, opts);
        Assert.False(frame.SourceClipped);
        Assert.Equal(0f, frame.SourceClipRatio);
    }

    [Fact]
    public void OptionsFromProbeSdr_OnScrgb_DoubleEotf_StillWrong()
    {
        // Guard: the old LoadWic bug must stay detectable.
        float e = 128f / 255f;
        HdrColor.EncodedRgbToScrgb(e, e, e, HdrTransfer.Srgb, 1, out var sr, out var sg, out var sb);
        var probe = new HdrProbe(HdrKind.None, CicpPrimaries: 1, CicpTransfer: null, MaxCllNits: null);
        var buggy = ImagingStillNormalize.Run(
            new float[] { sr, sg, sb, 1f }, 1, 1,
            ImagingStillNormalize.OptionsFromProbe(probe));
        var fixedPath = ImagingStillNormalize.Run(
            new float[] { sr, sg, sb, 1f }, 1, 1,
            ImagingStillNormalize.OptionsForPresentScrgb());
        Assert.True(
            buggy.Rgba[0] > fixedPath.Rgba[0] + 0.03f,
            $"bug={buggy.Rgba[0]} fixed={fixedPath.Rgba[0]}");
        Assert.Equal(TransferKind.Sdr, ImagingStillNormalize.OptionsFromProbe(probe).Transfer);
    }

    [Fact]
    public async Task DecodeAsync_PresentScrgbOpts_MatchesStripEncoded_GrayRamp()
    {
        // Synthetic: encoded sRGB ramp (includes 128 + 255). Strip = encoded+Sdr;
        // DecodeAsync = Scrgb-converted buffer + OptionsForPresentScrgb.
        const int w = 32;
        const int h = 8;
        var encoded = new float[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            var t = (i % w) / (float)(w - 1);
            var o = i * 4;
            encoded[o] = encoded[o + 1] = encoded[o + 2] = t;
            encoded[o + 3] = 1f;
        }

        var stripOpts = ImagingStillNormalize.OptionsForEncoded(HdrTransfer.Srgb, 1);
        using var stripDecode = new SmoothWorkingDecode();
        var strip = await stripDecode.DecodeStripToRgba16FAsync(
            w,
            h,
            (y0, rows, dest) =>
            {
                for (var row = 0; row < rows; row++)
                {
                    var y = y0 + row;
                    Array.Copy(encoded, y * w * 4, dest, row * w * 4, w * 4);
                }

                ImagingStillNormalize.NormalizeStripInPlace(
                    dest.AsSpan(0, rows * w * 4), w, rows, stripOpts);
            });
        Assert.NotNull(strip);

        // Present-scRGB buffer (LoadWic shape) + OptionsForPresentScrgb.
        var scrgb = new float[encoded.Length];
        for (var i = 0; i < w * h; i++)
        {
            var o = i * 4;
            HdrColor.EncodedRgbToScrgb(
                encoded[o], encoded[o + 1], encoded[o + 2],
                HdrTransfer.Srgb, 1,
                out var sr, out var sg, out var sb);
            scrgb[o] = sr;
            scrgb[o + 1] = sg;
            scrgb[o + 2] = sb;
            scrgb[o + 3] = 1f;
        }

        using var pathDecode = new SmoothWorkingDecode();
        var path = await pathDecode.DecodeAsync(
            scrgb, w, h, ImagingStillNormalize.OptionsForPresentScrgb());
        Assert.NotNull(path);

        // ImageStats: Max/MaxRgb exact; percentiles within one Analyze bin.
        Assert.Equal(path!.Stats.Max, strip!.Stats.Max);
        Assert.Equal(path.Stats.MaxRgb, strip.Stats.MaxRgb);
        var w50 = Analyze.OneAnalyzeBinWidthWorking(path.Stats.Max, path.Stats.P50);
        var w99 = Analyze.OneAnalyzeBinWidthWorking(path.Stats.Max, path.Stats.P99995);
        Assert.True(Math.Abs(path.Stats.P50 - strip.Stats.P50) <= w50 + 1e-5f);
        Assert.True(Math.Abs(path.Stats.P99995 - strip.Stats.P99995) <= w99 + 1e-5f);

        // Dedicated sRGB 128 + 255 pixels (exact codes, not ramp quantization).
        float e128 = 128f / 255f;
        HdrColor.EncodedRgbToScrgb(e128, e128, e128, HdrTransfer.Srgb, 1, out var s128, out _, out _);
        var w128 = ImagingStillNormalize.Run(
            new float[] { s128, s128, s128, 1f }, 1, 1,
            ImagingStillNormalize.OptionsForPresentScrgb());
        Assert.InRange(w128.Rgba[0], 0.214f, 0.218f);
        Assert.Equal(1f, path.WorkingRgba[((w - 1) * 4)], precision: 4);
    }

    [Fact]
    public async Task SourceClipped_StripVsDecodePath_EncodedWhite255()
    {
        // Strip + DecodePath both use encoded + OptionsForEncoded after LoadWic
        // prefers EncodedRgba. DecodePath SourceClipped comes from Normalize.Run;
        // strip Accumulator does not re-count clip (Working hist only) — compare
        // DecodePath to Normalize.Run, and strip Working Max to DecodePath.
        const int w = 4;
        const int h = 4;
        var encoded = new float[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            var o = i * 4;
            encoded[o] = encoded[o + 1] = encoded[o + 2] = 1f;
            encoded[o + 3] = 1f;
        }

        var opts = ImagingStillNormalize.OptionsForEncoded(HdrTransfer.Srgb, 1);
        var direct = ImagingStillNormalize.Run(encoded, w, h, opts);
        Assert.True(direct.SourceClipped);
        Assert.Equal(1f, direct.SourceClipRatio, precision: 5);

        using var pathDecode = new SmoothWorkingDecode();
        var path = await pathDecode.DecodeAsync(encoded, w, h, opts);
        Assert.NotNull(path);
        Assert.Equal(direct.SourceClipped, path!.Stats.SourceClipped);
        Assert.Equal(direct.SourceClipRatio, path.Stats.SourceClipRatio, precision: 5);

        using var stripDecode = new SmoothWorkingDecode();
        var strip = await stripDecode.DecodeStripToRgba16FAsync(
            w,
            h,
            (y0, rows, dest) =>
            {
                for (var row = 0; row < rows; row++)
                {
                    var y = y0 + row;
                    Array.Copy(encoded, y * w * 4, dest, row * w * 4, w * 4);
                }

                ImagingStillNormalize.NormalizeStripInPlace(
                    dest.AsSpan(0, rows * w * 4), w, rows, opts);
            });
        Assert.NotNull(strip);
        Assert.Equal(path.Stats.Max, strip!.Stats.Max);
        Assert.Equal(path.Stats.MaxRgb, strip.Stats.MaxRgb);
    }

    [Theory]
    [InlineData(80f)]
    [InlineData(203f)]
    [InlineData(400f)]
    [InlineData(1000f)]
    [InlineData(4000f)]
    public void PqAvifStyle_WorkingEqualsEncodedOptions_Within1e4(float nits)
    {
        // PQ AVIF-style: encoded PQ float + OptionsForEncoded (BT.2020).
        var e = ColorMath.NitsToPq(nits);
        var opts = ImagingStillNormalize.OptionsForEncoded(HdrTransfer.Pq, cicpPrimaries: 9);
        var frame = ImagingStillNormalize.Run(new float[] { e, e, e, 1f }, 1, 1, opts);

        // Reference: same buffer + opts again (LoadWic Working contract).
        var again = ImagingStillNormalize.Run(new float[] { e, e, e, 1f }, 1, 1, opts);
        AssertWithin(frame.Rgba[0], again.Rgba[0], 1e-4f);
        AssertWithin(frame.Rgba[1], again.Rgba[1], 1e-4f);
        AssertWithin(frame.Rgba[2], again.Rgba[2], 1e-4f);

        // Expected Working ≈ nits / 203.
        var expected = nits / ImagingConstants.RefWhiteNits;
        AssertWithin(frame.Rgba[0], expected, 1e-3f);
        if (nits > ImagingConstants.RefWhiteNits)
        {
            Assert.True(MaxRgb(frame) > 1f + 1e-3f, $">{nits} nit Working should exceed 1.0");
        }
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    [InlineData(1.0f)]
    public void Hlg_WorkingEqualsEncodedOptions_Within1e4(float encoded)
    {
        var opts = ImagingStillNormalize.OptionsForEncoded(HdrTransfer.Hlg, cicpPrimaries: 9);
        var frame = ImagingStillNormalize.Run(
            new float[] { encoded, encoded, encoded, 1f }, 1, 1, opts);

        // Direct Normalize.Pixel reference (same kernel as strip / DecodePath).
        Normalize.Pixel(encoded, encoded, encoded, opts, out var r, out var g, out var b);
        AssertWithin(frame.Rgba[0], r, 1e-4f);
        AssertWithin(frame.Rgba[1], g, 1e-4f);
        AssertWithin(frame.Rgba[2], b, 1e-4f);

        // HLG peak (encoded 1) → OOTF → >203 nit Working (>1).
        if (encoded >= 1f - 1e-6f)
        {
            Assert.True(MaxRgb(frame) > 1f, $"HLG peak Working={MaxRgb(frame)} should exceed 1.0");
        }
    }

    [Fact]
    public async Task PqHlg_DecodeAsync_MatchesNormalizeRun_Within1e4()
    {
        // LoadWic/Avif DecodePath: encoded + OptionsForEncoded → Working.
        float[] pqEnc =
        [
            ColorMath.NitsToPq(100f), ColorMath.NitsToPq(100f), ColorMath.NitsToPq(100f), 1f,
            ColorMath.NitsToPq(500f), ColorMath.NitsToPq(500f), ColorMath.NitsToPq(500f), 1f,
            ColorMath.NitsToPq(1000f), ColorMath.NitsToPq(1000f), ColorMath.NitsToPq(1000f), 1f,
            ColorMath.NitsToPq(4000f), ColorMath.NitsToPq(4000f), ColorMath.NitsToPq(4000f), 1f,
        ];
        var pqOpts = ImagingStillNormalize.OptionsForEncoded(HdrTransfer.Pq, 9);
        using (var dec = new SmoothWorkingDecode())
        {
            var path = await dec.DecodeAsync(pqEnc, 2, 2, pqOpts);
            Assert.NotNull(path);
            var direct = ImagingStillNormalize.Run(pqEnc, 2, 2, pqOpts);
            for (var i = 0; i < direct.Rgba.Length; i++)
            {
                AssertWithin(path!.WorkingRgba[i], direct.Rgba[i], 1e-4f);
            }
        }

        float[] hlgEnc =
        [
            0.5f, 0.5f, 0.5f, 1f,
            0.75f, 0.75f, 0.75f, 1f,
            0.9f, 0.9f, 0.9f, 1f,
            1f, 1f, 1f, 1f,
        ];
        var hlgOpts = ImagingStillNormalize.OptionsForEncoded(HdrTransfer.Hlg, 9);
        using (var dec = new SmoothWorkingDecode())
        {
            var path = await dec.DecodeAsync(hlgEnc, 2, 2, hlgOpts);
            Assert.NotNull(path);
            var direct = ImagingStillNormalize.Run(hlgEnc, 2, 2, hlgOpts);
            for (var i = 0; i < direct.Rgba.Length; i++)
            {
                AssertWithin(path!.WorkingRgba[i], direct.Rgba[i], 1e-4f);
            }

            Assert.True(MaxOf(path!.WorkingRgba) > 1f, "HLG peak Working > 1");
        }
    }

    [Fact]
    public void Hlg_PresentScrgbPath_DivergesFromEncoded_Ootf()
    {
        // Why LoadWic prefers EncodedRgba: EncodedRgbToScrgb + Scrgb options
        // skip / re-shape HLG OOTF vs Normalize Hlg path.
        const float e = 0.75f;
        var encOpts = ImagingStillNormalize.OptionsForEncoded(HdrTransfer.Hlg, 9);
        var enc = ImagingStillNormalize.Run(new float[] { e, e, e, 1f }, 1, 1, encOpts);

        HdrColor.EncodedRgbToScrgb(e, e, e, HdrTransfer.Hlg, 9, out var sr, out var sg, out var sb);
        var sc = ImagingStillNormalize.Run(
            new float[] { sr, sg, sb, 1f }, 1, 1,
            ImagingStillNormalize.OptionsForPresentScrgb());

        Assert.True(
            Math.Abs(enc.Rgba[0] - sc.Rgba[0]) > 1e-3f,
            $"expected HLG OOTF diverge: enc={enc.Rgba[0]} sc={sc.Rgba[0]}");
    }

    private static float MaxRgb(NormalizedFrame f) =>
        Math.Max(f.Rgba[0], Math.Max(f.Rgba[1], f.Rgba[2]));

    private static float MaxOf(float[] rgba)
    {
        var m = 0f;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            m = Math.Max(m, Math.Max(rgba[i], Math.Max(rgba[i + 1], rgba[i + 2])));
        }

        return m;
    }

    private static void AssertWithin(float actual, float expected, float tol) =>
        Assert.True(
            Math.Abs(actual - expected) <= tol,
            $"actual={actual} expected={expected} tol={tol}");
}
