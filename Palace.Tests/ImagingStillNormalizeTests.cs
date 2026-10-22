using Palace.Helpers;
using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

/// <summary>P2: decoder glue maps metadata → Normalize and matches fixture masters.</summary>
public class ImagingStillNormalizeTests
{
    [Fact]
    public void OptionsFromProbe_Jxr_IsScrgb()
    {
        var opts = ImagingStillNormalize.OptionsFromProbe(
            new HdrProbe(HdrKind.HdrJxr, null, null, 800));
        Assert.Equal(TransferKind.Scrgb, opts.Transfer);
        Assert.Equal(ColorPrimaries.Bt709, opts.Primaries);
        Assert.Equal(800, opts.MetadataPeakNits);
    }

    [Fact]
    public void OptionsFromProbe_PqPng_IsPqBt2020()
    {
        var opts = ImagingStillNormalize.OptionsFromProbe(
            new HdrProbe(HdrKind.HdrPng, 9, 16, 1000));
        Assert.Equal(TransferKind.Pq, opts.Transfer);
        Assert.Equal(ColorPrimaries.Bt2020, opts.Primaries);
    }

    [Fact]
    public void PrimariesFromCicp_JxlP3_IsDisplayP3()
    {
        Assert.Equal(ColorPrimaries.DisplayP3, ImagingStillNormalize.PrimariesFromCicp(11));
        Assert.Equal(ColorPrimaries.DisplayP3, ImagingStillNormalize.PrimariesFromCicp(12));
        Assert.Equal(ColorPrimaries.Bt2020, ImagingStillNormalize.PrimariesFromCicp(9));
        Assert.Equal(ColorPrimaries.Bt709, ImagingStillNormalize.PrimariesFromCicp(1));
    }

    [Fact]
    public void OptionsForJxl_StillPq_UsesPq()
    {
        var opts = ImagingStillNormalize.OptionsForJxl(
            JxlBufferTransfer.Pq, HdrTransfer.Pq, 9, 10_000f);
        Assert.Equal(TransferKind.Pq, opts.Transfer);
        Assert.Equal(ColorPrimaries.Bt2020, opts.Primaries);
    }

    [Fact]
    public void OptionsForJxl_LinearRelative_UsesIntensityTargetWhite()
    {
        var opts = ImagingStillNormalize.OptionsForJxl(
            JxlBufferTransfer.Linear, HdrTransfer.Pq, 9, 1000f);
        Assert.Equal(TransferKind.LinearScene, opts.Transfer);
        Assert.Equal(1000f, opts.WhiteLuminance);
        Assert.Equal(ColorPrimaries.Bt2020, opts.Primaries);
    }

    [Fact]
    public void Fixture_PqDump_ViaGlue_WithinOnePercentOfMaster()
    {
        var master = ImagingFixtureIO.LoadF32("master.bt2020.f32");
        var pq = ImagingFixtureIO.LoadF32("master.pq.f32");
        var opts = ImagingStillNormalize.OptionsForEncoded(HdrTransfer.Pq, 9);
        var frame = ImagingStillNormalize.Run(pq, 2, 2, opts);
        for (var i = 0; i < master.Length; i += 4)
        {
            AssertNear(master[i], frame.Rgba[i], 0.01f);
            AssertNear(master[i + 1], frame.Rgba[i + 1], 0.01f);
            AssertNear(master[i + 2], frame.Rgba[i + 2], 0.01f);
        }
    }

    [Fact]
    public void Fixture_HdrRadiance_ViaGlue_UsesExrRefWhite80()
    {
        var (rgba, w, h) = ImagingFixtureIO.LoadHdr();
        var opts = ImagingStillNormalize.OptionsForRadiance();
        var frame = ImagingStillNormalize.Run(rgba, w, h, opts);
        var master = ImagingFixtureIO.LoadF32("master.bt2020.f32");
        AssertNear(master[0], frame.Rgba[0], 0.05f);
        AssertNear(master[4], frame.Rgba[4], 0.05f);
    }

    [Fact]
    public void Fixture_ExrDecoded_ViaOptionsForExr_MatchesJxrScrgb()
    {
        var exrBytes = File.ReadAllBytes(Path.Combine(ImagingFixtureIO.Dir, "master.exr"));
        var exr = ImagingFixtureIO.LoadF32("master.exr.decoded.f32");
        var jxr = ImagingFixtureIO.LoadF32("master.jxr.decoded.f32");

        var exrOpts = ImagingStillNormalize.OptionsForExr(exrBytes);
        Assert.Equal(TransferKind.LinearScene, exrOpts.Transfer);
        Assert.Null(exrOpts.WhiteLuminance); // Pregmata-like: no whiteLuminance attr

        var exrFrame = ImagingStillNormalize.Run(exr, 2, 2, exrOpts);
        var jxrFrame = ImagingStillNormalize.Run(
            jxr, 2, 2, ImagingStillNormalize.OptionsForJxr());

        foreach (var o in new[] { 0, 4 })
        {
            AssertNear(jxrFrame.Rgba[o], exrFrame.Rgba[o], 0.01f);
            AssertNear(jxrFrame.Rgba[o + 1], exrFrame.Rgba[o + 1], 0.01f);
            AssertNear(jxrFrame.Rgba[o + 2], exrFrame.Rgba[o + 2], 0.01f);
        }
    }

    [Fact]
    public void OptionsForExr_ReadsWhiteLuminanceAndChromaticities()
    {
        // Synthetic header: Display P3 + whiteLuminance 100.
        var chrom = Chromaticities.DisplayP3;
        using var ms = new MemoryStream();
        ms.Write([0x76, 0x2f, 0x31, 0x01, 0x02, 0x00, 0x00, 0x00]);
        WriteAttr(ms, "chromaticities", "chromaticities", EncodeChrom(chrom));
        WriteAttr(ms, "whiteLuminance", "float", BitConverter.GetBytes(100f));
        ms.WriteByte(0);
        var opts = ImagingStillNormalize.OptionsForExr(ms.ToArray());
        Assert.Equal(100f, opts.WhiteLuminance);
        Assert.Equal(ColorPrimaries.DisplayP3, opts.EffectivePrimaries);
        Assert.False(opts.UsesCustomChromaticities);
    }

    [Fact]
    public void CustomChromaticities_LinearScene_UsesMatrix()
    {
        // Slightly off BT.709 → custom path (not MatchKnown).
        var chrom = new Chromaticities(
            0.64f, 0.33f,
            0.30f, 0.60f,
            0.15f, 0.06f,
            0.3127f, 0.3290f);
        // Nudge red x so MatchKnown fails.
        chrom = chrom with { Rx = 0.65f };
        Assert.Null(chrom.MatchKnown());

        var opts = new NormalizeOptions(
            TransferKind.LinearScene,
            ColorPrimaries.Bt709,
            WhiteLuminance: 80f,
            Chromaticities: chrom);
        Assert.True(opts.UsesCustomChromaticities);

        Normalize.Pixel(1f, 1f, 1f, opts, out var r, out var g, out var b);
        // White should stay near 80/203 after convert.
        var expected = ImagingConstants.ScrgbNits / ImagingConstants.RefWhiteNits;
        AssertNear(expected, ImagingConstants.Bt2020Y(r, g, b), 0.05f);
    }

    private static void AssertNear(float expected, float actual, float tol) =>
        Assert.True(
            Math.Abs(expected - actual) <= tol,
            $"expected {expected}, got {actual}, tol {tol}");

    private static byte[] EncodeChrom(Chromaticities c)
    {
        var buf = new byte[32];
        BitConverter.TryWriteBytes(buf.AsSpan(0), c.Rx);
        BitConverter.TryWriteBytes(buf.AsSpan(4), c.Ry);
        BitConverter.TryWriteBytes(buf.AsSpan(8), c.Gx);
        BitConverter.TryWriteBytes(buf.AsSpan(12), c.Gy);
        BitConverter.TryWriteBytes(buf.AsSpan(16), c.Bx);
        BitConverter.TryWriteBytes(buf.AsSpan(20), c.By);
        BitConverter.TryWriteBytes(buf.AsSpan(24), c.Wx);
        BitConverter.TryWriteBytes(buf.AsSpan(28), c.Wy);
        return buf;
    }

    private static void WriteAttr(Stream s, string name, string type, ReadOnlySpan<byte> value)
    {
        var nb = System.Text.Encoding.ASCII.GetBytes(name);
        s.Write(nb);
        s.WriteByte(0);
        var tb = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(tb);
        s.WriteByte(0);
        s.Write(BitConverter.GetBytes(value.Length));
        s.Write(value);
    }
}
