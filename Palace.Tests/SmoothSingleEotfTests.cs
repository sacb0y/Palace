using Palace.Helpers;
using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// V3 A/B: DecodePath must not double-EOTF ScrgbRgba. Strip stays
/// encoded + Sdr. Both paths → Working ≈0.216 @ sRGB 128, 1.0 @ 255.
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

    private static float MaxRgb(NormalizedFrame f) =>
        Math.Max(f.Rgba[0], Math.Max(f.Rgba[1], f.Rgba[2]));
}
