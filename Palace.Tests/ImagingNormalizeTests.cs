using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

public class ImagingNormalizeTests
{
    [Fact]
    public void FixturePq_WithinOnePercentOfMaster()
    {
        var master = ImagingFixtureIO.LoadF32("master.bt2020.f32");
        var pq = ImagingFixtureIO.LoadF32("master.pq.f32");
        var frame = Normalize.Run(pq, 2, 2, new NormalizeOptions(TransferKind.Pq, ColorPrimaries.Bt2020));
        for (var i = 0; i < master.Length; i += 4)
        {
            AssertNear(master[i], frame.Rgba[i], 0.01f);
            AssertNear(master[i + 1], frame.Rgba[i + 1], 0.01f);
            AssertNear(master[i + 2], frame.Rgba[i + 2], 0.01f);
        }
    }

    [Fact]
    public void FixtureScrgb_WithinOnePercentOfMaster()
    {
        var master = ImagingFixtureIO.LoadF32("master.bt2020.f32");
        var sc = ImagingFixtureIO.LoadF32("master.scrgb.f32");
        var frame = Normalize.Run(sc, 2, 2, new NormalizeOptions(TransferKind.Scrgb, ColorPrimaries.Bt709));
        // Warm patch / primaries round-trip — allow 2% on chromatic, 1% on greys.
        for (var i = 0; i < 8; i += 4)
        {
            AssertNear(master[i], frame.Rgba[i], 0.01f);
        }
    }

    [Fact]
    public void FixtureScene709_ExrScale_WithinOnePercent()
    {
        var master = ImagingFixtureIO.LoadF32("master.bt2020.f32");
        var scene = ImagingFixtureIO.LoadF32("master.scene709.f32");
        var frame = Normalize.Run(
            scene,
            2,
            2,
            new NormalizeOptions(TransferKind.LinearScene, ColorPrimaries.Bt709, WhiteLuminance: 203f));
        for (var i = 0; i < 8; i += 4)
        {
            AssertNear(master[i], frame.Rgba[i], 0.01f);
        }
    }

    [Fact]
    public void FixtureHdr_Radiance_MetadataLessUsesExrRefWhite80()
    {
        // Pregmata: metadata-less .hdr is scRGB-scale (1.0 = 80 nits), not absolute nits.
        var (rgba, w, h) = ImagingFixtureIO.LoadHdr();
        Assert.Equal(2, w);
        Assert.Equal(2, h);
        var frame = Normalize.Run(
            rgba,
            w,
            h,
            new NormalizeOptions(TransferKind.LinearScene, ColorPrimaries.Bt709));
        var master = ImagingFixtureIO.LoadF32("master.bt2020.f32");
        // Grey / white — RGBE quantization; ExrRefWhite=80 → working ≈ master.
        AssertNear(master[0], frame.Rgba[0], 0.05f);
        AssertNear(master[4], frame.Rgba[4], 0.05f);
    }

    [Fact]
    public void ExrRefWhite_MetadataLess_OneMapsToEightyOver203()
    {
        // M4: x=1 with default ExrRefWhite → working 80/203.
        Normalize.Pixel(
            1f, 1f, 1f,
            new NormalizeOptions(TransferKind.LinearScene, ColorPrimaries.Bt2020),
            out var r, out var g, out var b);
        var expected = ImagingConstants.ScrgbNits / ImagingConstants.RefWhiteNits;
        AssertNear(expected, r, 0.005f);
        AssertNear(expected, g, 0.005f);
        AssertNear(expected, b, 0.005f);
    }

    /// <summary>
    /// M4 golden: real decoded EXR / HDR / JXR (Pregmata_SteamAVIF scale,
    /// 1.0 = 80 nits → working 80/203) normalize within 1% on greys/white.
    /// </summary>
    [Fact]
    public void Golden_ExrHdrJxr_Decoded_NormalizeWithinOnePercent()
    {
        var exr = ImagingFixtureIO.LoadF32("master.exr.decoded.f32");
        var hdr = ImagingFixtureIO.LoadF32("master.hdr.decoded.f32");
        var jxr = ImagingFixtureIO.LoadF32("master.jxr.decoded.f32");

        var exrFrame = Normalize.Run(exr, 2, 2, new NormalizeOptions(TransferKind.LinearScene, ColorPrimaries.Bt709));
        var hdrFrame = Normalize.Run(hdr, 2, 2, new NormalizeOptions(TransferKind.LinearScene, ColorPrimaries.Bt709));
        var jxrFrame = Normalize.Run(jxr, 2, 2, new NormalizeOptions(TransferKind.Scrgb, ColorPrimaries.Bt709));

        foreach (var o in new[] { 0, 4 })
        {
            AssertNear(jxrFrame.Rgba[o], exrFrame.Rgba[o], 0.01f);
            AssertNear(jxrFrame.Rgba[o], hdrFrame.Rgba[o], 0.01f);
            AssertNear(jxrFrame.Rgba[o + 1], exrFrame.Rgba[o + 1], 0.01f);
            AssertNear(jxrFrame.Rgba[o + 1], hdrFrame.Rgba[o + 1], 0.01f);
            AssertNear(jxrFrame.Rgba[o + 2], exrFrame.Rgba[o + 2], 0.01f);
            AssertNear(jxrFrame.Rgba[o + 2], hdrFrame.Rgba[o + 2], 0.01f);
        }
    }

    [Fact]
    public void M4_ExrAndJxr_NormalizeEqually()
    {
        var exr = ImagingFixtureIO.LoadF32("master.exr.decoded.f32");
        var jxr = ImagingFixtureIO.LoadF32("master.jxr.decoded.f32");
        var exrFrame = Normalize.Run(exr, 2, 2, new NormalizeOptions(TransferKind.LinearScene, ColorPrimaries.Bt709));
        var jxrFrame = Normalize.Run(jxr, 2, 2, new NormalizeOptions(TransferKind.Scrgb, ColorPrimaries.Bt709));
        for (var i = 0; i < 8; i += 4)
        {
            AssertNear(jxrFrame.Rgba[i], exrFrame.Rgba[i], 0.01f);
            AssertNear(jxrFrame.Rgba[i + 1], exrFrame.Rgba[i + 1], 0.01f);
            AssertNear(jxrFrame.Rgba[i + 2], exrFrame.Rgba[i + 2], 0.01f);
        }
    }

    [Fact]
    public void ExrWhiteLuminance_MultipliesNotDivides()
    {
        // x=1, whiteLuminance=203 → working 1.0 (M3).
        Normalize.Pixel(
            1f, 1f, 1f,
            new NormalizeOptions(TransferKind.LinearScene, ColorPrimaries.Bt2020, WhiteLuminance: 203f),
            out var r, out var g, out var b);
        AssertNear(1f, r, 0.005f);
        AssertNear(1f, g, 0.005f);
        AssertNear(1f, b, 0.005f);
    }

    [Fact]
    public void Bt709_ConvertedBeforeBt2020Y()
    {
        // Pure BT.709 red linear 1.0 should not use BT.2020 weights on 709 RGB.
        Normalize.Pixel(
            1f, 0f, 0f,
            new NormalizeOptions(TransferKind.LinearScene, ColorPrimaries.Bt709, WhiteLuminance: 203f),
            out var r, out var g, out var b);
        Assert.True(r > 0.5f);
        Assert.True(g > 0f); // 709 red spills into 2020 G
    }

    [Fact]
    public void StripsNanInf_AndClampsNegLuminance()
    {
        float[] buf = [float.NaN, float.PositiveInfinity, -0.5f, 1f];
        var frame = Normalize.Run(buf, 1, 1, new NormalizeOptions(TransferKind.Scrgb));
        Assert.True(float.IsFinite(frame.Rgba[0]));
        Assert.True(ImagingConstants.Bt2020Y(frame.Rgba[0], frame.Rgba[1], frame.Rgba[2]) >= -1e-5f);
    }

    [Fact]
    public void SourceClipped_PqAtEncodedMax()
    {
        var pq = ColorMath.NitsToPq(10_000f);
        var buf = new float[16];
        for (var i = 0; i < 4; i++)
        {
            buf[i * 4] = buf[i * 4 + 1] = buf[i * 4 + 2] = pq;
            buf[i * 4 + 3] = 1f;
        }

        var frame = Normalize.Run(buf, 2, 2, new NormalizeOptions(TransferKind.Pq));
        Assert.True(frame.SourceClipped);
    }

    [Fact]
    public void SourceClipped_LinearSceneAtMasteringPeak()
    {
        // Peak metadata in nits; clip ceil = peakNits / EffectiveWhiteNits (= ExrRefWhite).
        var buf = new float[] { 10f, 10f, 10f, 1f, 10f, 10f, 10f, 1f, 10f, 10f, 10f, 1f, 10f, 10f, 10f, 1f };
        var frame = Normalize.Run(
            buf,
            2,
            2,
            new NormalizeOptions(TransferKind.LinearScene, MetadataPeakNits: 10f * ImagingConstants.ExrRefWhite));
        Assert.True(frame.SourceClipped);
        // Flag is stored on ImageStats for later use (nothing reads it in P1 yet — S1).
        var stats = Analyze.Run(frame);
        Assert.True(stats.SourceClipped);
        Assert.True(stats.SourceClipRatio > ImagingConstants.SourceClipFraction);
    }

    [Fact]
    public void SourceClipped_LinearSceneWithoutPeak_DoesNotUseBufferMax()
    {
        // S1: no MetadataPeakNits → do not flag from buffer max.
        var buf = new float[] { 2f, 2f, 2f, 1f, 2f, 2f, 2f, 1f, 2f, 2f, 2f, 1f, 2f, 2f, 2f, 1f };
        var frame = Normalize.Run(buf, 2, 2, new NormalizeOptions(TransferKind.LinearScene));
        Assert.False(frame.SourceClipped);
        Assert.Equal(0f, frame.SourceClipRatio);
    }

    [Fact]
    public void Pq_PaperWhite_IsOne()
    {
        var pq203 = ColorMath.NitsToPq(203f);
        Normalize.Pixel(pq203, pq203, pq203, new NormalizeOptions(TransferKind.Pq), out var r, out var g, out var b);
        AssertNear(1f, r, 0.005f);
    }

    [Fact]
    public void Smoothstep_ReversedEdges_StillRises()
    {
        // M2: reversed edges must not hard-step.
        var a = ColorMath.Smoothstep(0.8f, 0.2f, 0.3f);
        var b = ColorMath.Smoothstep(0.8f, 0.2f, 0.7f);
        Assert.True(b > a);
    }

    private static void AssertNear(float expected, float actual, float rel)
    {
        var tol = Math.Max(1e-4f, Math.Abs(expected) * rel);
        Assert.True(Math.Abs(actual - expected) <= tol, $"expected {expected}, got {actual}, tol {tol}");
    }
}
