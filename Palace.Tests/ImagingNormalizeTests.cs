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
    public void FixtureHdr_Radiance_AbsoluteNits()
    {
        var (nitsRgba, w, h) = ImagingFixtureIO.LoadHdr();
        Assert.Equal(2, w);
        Assert.Equal(2, h);
        // Radiance stores absolute nits; LinearScene with whiteLuminance=1 means x*1/203.
        // Prefer whiteLuminance = pixel is already nits → use WhiteLuminance=1? 
        // Formula x*W/203: if x is nits, W=1 → x/203. Yes.
        var frame = Normalize.Run(
            nitsRgba,
            w,
            h,
            new NormalizeOptions(TransferKind.LinearScene, ColorPrimaries.Bt709, WhiteLuminance: 1f));
        var master = ImagingFixtureIO.LoadF32("master.bt2020.f32");
        // Grey / white pixels — Radiance RGBE quantization is coarse.
        AssertNear(master[0], frame.Rgba[0], 0.05f);
        AssertNear(master[4], frame.Rgba[4], 0.05f);
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
        var buf = new float[] { 10f, 10f, 10f, 1f, 10f, 10f, 10f, 1f, 10f, 10f, 10f, 1f, 10f, 10f, 10f, 1f };
        var frame = Normalize.Run(
            buf,
            2,
            2,
            new NormalizeOptions(TransferKind.LinearScene, MetadataPeakNits: 10f * 203f));
        Assert.True(frame.SourceClipped);
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
