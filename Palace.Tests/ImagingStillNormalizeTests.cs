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

    /// <summary>A1: BT.2020 linear out must not be tagged DisplayP3 / Bt709.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    [InlineData(11)]
    [InlineData(12)]
    public void A1_OptionsForJxl_Linear_UsesBt2020WhenWideOut(int? cicp)
    {
        var opts = ImagingStillNormalize.OptionsForJxl(
            JxlBufferTransfer.Linear, HdrTransfer.Pq, cicp, 1000f);
        Assert.Equal(ColorPrimaries.Bt2020, opts.Primaries);

        // Identity on a BT.2020 linear pixel (IT=1000 → ×1000/203).
        var buf = new float[] { 0.5f, 0.2f, 0.1f, 1f };
        var frame = ImagingStillNormalize.Run(buf, 1, 1, opts);
        AssertNear(0.5f * 1000f / 203f, frame.Rgba[0], 1e-4f);
        AssertNear(0.2f * 1000f / 203f, frame.Rgba[1], 1e-4f);
        AssertNear(0.1f * 1000f / 203f, frame.Rgba[2], 1e-4f);
    }

    /// <summary>A2: Pq + DisplayP3 must differ from Pq + Bt2020 on a non-grey pixel.</summary>
    [Fact]
    public void A2_Pq_DisplayP3_DiffersFromBt2020()
    {
        var buf = new float[] { 0.5f, 0.2f, 0.1f, 1f };
        var p3 = Normalize.Run(buf, 1, 1, new NormalizeOptions(TransferKind.Pq, ColorPrimaries.DisplayP3));
        var bt = Normalize.Run(buf, 1, 1, new NormalizeOptions(TransferKind.Pq, ColorPrimaries.Bt2020));
        Assert.True(
            Math.Abs(p3.Rgba[0] - bt.Rgba[0]) > 1e-3f
            || Math.Abs(p3.Rgba[1] - bt.Rgba[1]) > 1e-3f
            || Math.Abs(p3.Rgba[2] - bt.Rgba[2]) > 1e-3f,
            "P3 and BT.2020 PQ paths must diverge on a chromatic sample");
    }

    [Fact]
    public void A2_Sdr_DisplayP3_DiffersFromBt709()
    {
        var buf = new float[] { 0.8f, 0.2f, 0.1f, 1f };
        var p3 = Normalize.Run(buf, 1, 1, new NormalizeOptions(TransferKind.Sdr, ColorPrimaries.DisplayP3));
        var bt = Normalize.Run(buf, 1, 1, new NormalizeOptions(TransferKind.Sdr, ColorPrimaries.Bt709));
        Assert.True(Math.Abs(p3.Rgba[0] - bt.Rgba[0]) > 1e-4f);
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
    public void OptionsForExr_WhiteLuminance203_MapsOneToOne()
    {
        using var ms = new MemoryStream();
        ms.Write([0x76, 0x2f, 0x31, 0x01, 0x02, 0x00, 0x00, 0x00]);
        WriteAttr(ms, "whiteLuminance", "float", BitConverter.GetBytes(203f));
        ms.WriteByte(0);
        var opts = ImagingStillNormalize.OptionsForExr(ms.ToArray());
        Assert.Equal(203f, opts.WhiteLuminance);
        Normalize.Pixel(1f, 1f, 1f, opts, out var r, out var g, out var b);
        AssertNear(1f, r, 1e-4f);
        AssertNear(1f, g, 1e-4f);
        AssertNear(1f, b, 1e-4f);
    }

    [Fact]
    public void OptionsForExr_ReadsWhiteLuminanceAndChromaticities()
    {
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

    /// <summary>B3 / A6: ACEScg-like custom chrom — coloured pixel vs reference.</summary>
    [Fact]
    public void CustomChromaticities_AceScgLike_ColouredPixel()
    {
        // ACEScg xy (AP1) D60 white — MatchKnown null.
        var chrom = new Chromaticities(
            0.713f, 0.293f,
            0.165f, 0.830f,
            0.128f, 0.044f,
            0.32168f, 0.33767f);
        Assert.Null(chrom.MatchKnown());

        var opts = new NormalizeOptions(
            TransferKind.LinearScene,
            ColorPrimaries.Bt709,
            WhiteLuminance: 80f,
            Chromaticities: chrom);
        Assert.True(opts.UsesCustomChromaticities);

        Normalize.Pixel(0.5f, 0.2f, 0.1f, opts, out var r, out var g, out var b);
        // Reference from Doktor probe (ACEScg → BT.2020 @ 80/203 scale).
        AssertNear(0.20032f, r, 0.002f);
        AssertNear(0.07865f, g, 0.002f);
        AssertNear(0.03762f, b, 0.002f);
    }

    [Fact]
    public void B2_TryRun_OverBudget_ReturnsNull()
    {
        var opts = ImagingStillNormalize.OptionsForRadiance();
        // Claim a huge frame without allocating — budget check is on w×h.
        Assert.Null(ImagingStillNormalize.TryRun([], ImagingStillNormalize.MaxWorkingPixels + 1, 1, opts));
        Assert.False(ImagingStillNormalize.FitsWorkingBudget(4096, 4096)); // 16.7 MP
        Assert.True(ImagingStillNormalize.FitsWorkingBudget(4000, 4000)); // 16 MP
    }

    /// <summary>N1: huge attribute size must not throw (overflow-safe bound).</summary>
    [Fact]
    public void N1_ExrHeader_HugeAttributeSize_ReturnsFalse()
    {
        using var ms = new MemoryStream();
        ms.Write([0x76, 0x2f, 0x31, 0x01, 0x02, 0x00, 0x00, 0x00]);
        var name = System.Text.Encoding.ASCII.GetBytes("chromaticities");
        ms.Write(name);
        ms.WriteByte(0);
        var type = System.Text.Encoding.ASCII.GetBytes("chromaticities");
        ms.Write(type);
        ms.WriteByte(0);
        ms.Write(BitConverter.GetBytes(int.MaxValue)); // size near MaxValue
        ms.Write(new byte[8]); // truncated payload
        // Must not throw ArgumentOutOfRangeException from Span.Slice.
        Assert.False(ExrHeader.TryReadChromaticities(ms.ToArray(), out _));
        Assert.False(ExrHeader.TryReadWhiteLuminance(ms.ToArray(), out _));
    }

    /// <summary>N2: ExrFile and ExrHeader share one chromaticities parser.</summary>
    [Fact]
    public void N2_ExrFileAndExrHeader_AgreeOnChromaticities()
    {
        var chrom = Chromaticities.DisplayP3;
        using var ms = new MemoryStream();
        ms.Write([0x76, 0x2f, 0x31, 0x01, 0x02, 0x00, 0x00, 0x00]);
        WriteAttr(ms, "chromaticities", "chromaticities", EncodeChrom(chrom));
        ms.WriteByte(0);
        var file = ms.ToArray();

        Assert.True(ExrHeader.TryReadChromaticities(file, out var header));
        var legacy = ExrFile.TryReadChromaticities(file);
        Assert.NotNull(legacy);
        Assert.Equal(header.Rx, legacy![0]);
        Assert.Equal(header.Ry, legacy[1]);
        Assert.Equal(header.Gx, legacy[2]);
        Assert.Equal(header.Gy, legacy[3]);
        Assert.Equal(header.Bx, legacy[4]);
        Assert.Equal(header.By, legacy[5]);
        Assert.Equal(header.Wx, legacy[6]);
        Assert.Equal(header.Wy, legacy[7]);
    }

    /// <summary>N2: out-of-range / non-finite chromaticities rejected by both.</summary>
    [Fact]
    public void N2_ExrHeader_RejectsDegenerateChromaticities()
    {
        var bad = new Chromaticities(0.64f, 0f, 0.3f, 0.6f, 0.15f, 0.06f, 0.3127f, 0.329f);
        Assert.False(ExrHeader.IsValidChromaticities(bad));

        using var ms = new MemoryStream();
        ms.Write([0x76, 0x2f, 0x31, 0x01, 0x02, 0x00, 0x00, 0x00]);
        WriteAttr(ms, "chromaticities", "chromaticities", EncodeChrom(bad));
        ms.WriteByte(0);
        var file = ms.ToArray();
        Assert.False(ExrHeader.TryReadChromaticities(file, out _));
        Assert.Null(ExrFile.TryReadChromaticities(file));
    }

    /// <summary>N3: zeroed ExrRefWhite (object-initializer footgun) → constant 80.</summary>
    [Fact]
    public void N3_NormalizeOptions_ZeroExrRefWhite_UsesConstant()
    {
        var opts = new NormalizeOptions(TransferKind.LinearScene, ExrRefWhite: 0f);
        Assert.Equal(ImagingConstants.ExrRefWhite, opts.EffectiveWhiteNits);
        Normalize.Pixel(1f, 1f, 1f, opts, out var r, out var g, out var b);
        AssertNear(ImagingConstants.ExrRefWhite / 203f, r, 1e-4f);
        AssertNear(ImagingConstants.ExrRefWhite / 203f, g, 1e-4f);
        AssertNear(ImagingConstants.ExrRefWhite / 203f, b, 1e-4f);
    }

    /// <summary>S3 / A6: TryRun swallows short-buffer / bad input — null, no throw.</summary>
    [Fact]
    public void S3_TryRun_ShortBuffer_ReturnsNull()
    {
        var opts = ImagingStillNormalize.OptionsForRadiance();
        // 2×2 claimed but only one pixel of floats → Normalize throws → null.
        Assert.Null(ImagingStillNormalize.TryRun([1f, 1f, 1f, 1f], 2, 2, opts));
    }

    /// <summary>B1: NaN/Inf in EXR-like float buffer → finite present stats.</summary>
    [Fact]
    public void B1_ExrSanitizeNonFinite_PresentStatsFinite()
    {
        var rgba = new float[]
        {
            1f, 1f, 1f, 1f,
            float.NaN, 0.5f, 0.5f, 1f,
            0.5f, float.PositiveInfinity, 0.5f, 1f,
            0.25f, 0.25f, 0.25f, float.NaN,
        };
        ExrFile.SanitizeNonFinite(rgba);
        foreach (var v in rgba)
        {
            Assert.True(float.IsFinite(v), $"non-finite after sanitize: {v}");
        }

        Assert.Equal(0f, rgba[4]); // NaN R → 0
        Assert.Equal(0f, rgba[9]); // +Inf G → 0
        Assert.Equal(1f, rgba[15]); // NaN A → 1

        var frame = RadianceFile.FrameFromScrgb(rgba, 2, 2);
        Assert.True(float.IsFinite(frame.MaxScrgb));
        Assert.True(float.IsFinite(frame.MaxNits));
        Assert.True(float.IsFinite(frame.AvgNits));
        Assert.True(float.IsFinite(frame.MinNits));
        Assert.False(float.IsNaN(frame.MaxScrgb));
    }

    /// <summary>R3: Fit box-scale skips Working; Actual keeps it when asked.</summary>
    [Fact]
    public void R3_ShouldBuildWorking_SkipsWhenFitScales()
    {
        Assert.False(GalleryPresent.ShouldBuildWorkingAtPresentSize(
            buildWorking: true, 4000, 3000, 1280, 720, ImageScaling.Fit));
        Assert.True(GalleryPresent.ShouldBuildWorkingAtPresentSize(
            buildWorking: true, 800, 600, 1280, 720, ImageScaling.Fit));
        Assert.True(GalleryPresent.ShouldBuildWorkingAtPresentSize(
            buildWorking: true, 4000, 3000, 1280, 720, ImageScaling.Actual));
        Assert.False(GalleryPresent.ShouldBuildWorkingAtPresentSize(
            buildWorking: false, 800, 600, 1280, 720, ImageScaling.Actual));
    }

    /// <summary>
    /// R4 / ZProbe138c: 64×64 ACEScg with NaN/Inf — Working ≠ scene array,
    /// TryRun leaves input, in-place convert leaves Working, present matches
    /// clone-then-convert reference.
    /// </summary>
    [Fact]
    public void R4_ZProbe138c_SanitizeWorkingConvertOrder()
    {
        const int w = 64;
        const int h = 64;
        var chrom = new float[]
        {
            0.713f, 0.293f, 0.165f, 0.830f, 0.128f, 0.044f, 0.32168f, 0.33767f,
        };
        var opts = new NormalizeOptions(
            TransferKind.LinearScene,
            ColorPrimaries.Bt709,
            WhiteLuminance: 80f,
            Chromaticities: new Chromaticities(
                0.713f, 0.293f, 0.165f, 0.830f, 0.128f, 0.044f, 0.32168f, 0.33767f));

        var scene = new float[w * h * 4];
        for (var i = 0; i < scene.Length; i += 4)
        {
            scene[i] = 0.5f;
            scene[i + 1] = 0.2f;
            scene[i + 2] = 0.1f;
            scene[i + 3] = 1f;
        }

        // Plant NaN / Inf like Doktor's probe.
        scene[4] = float.NaN;
        scene[9] = float.PositiveInfinity;
        scene[15] = float.NaN; // alpha

        // Reference: sanitize → clone for Working → convert clone for present.
        var forWorking = (float[])scene.Clone();
        ExrFile.SanitizeNonFinite(forWorking);
        var afterSanitize = (float[])forWorking.Clone();
        var workingRef = ImagingStillNormalize.TryRun(forWorking, w, h, opts);
        Assert.NotNull(workingRef);
        Assert.True(BuffersEqual(afterSanitize, forWorking), "TryRun must leave input untouched");

        var presentRef = (float[])afterSanitize.Clone();
        ExrFile.ConvertToRec709(presentRef, chrom);
        var statsRef = RadianceFile.FrameFromScrgb(presentRef, w, h);
        var workingSnapshot = (float[])workingRef!.Rgba.Clone();

        // Production helper on a fresh copy of the unsanitized scene.
        var built = ExrPresentWorking.Run(
            (float[])scene.Clone(), w, h, chrom, opts, buildWorking: true);
        Assert.NotNull(built.Working);
        Assert.False(ReferenceEquals(built.Working!.Rgba, built.PresentRgba));
        Assert.True(BuffersEqual(workingSnapshot, built.Working.Rgba),
            "in-place convert must not mutate Working");
        Assert.True(BuffersEqual(presentRef, built.PresentRgba));
        Assert.Equal(statsRef.MaxScrgb, built.Stats.MaxScrgb, 5);
        Assert.Equal(statsRef.MaxNits, built.Stats.MaxNits, 5);
        Assert.Equal(statsRef.AvgNits, built.Stats.AvgNits, 5);
        Assert.True(float.IsFinite(built.Stats.MaxScrgb));
    }

    private static bool BuffersEqual(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (!float.IsFinite(a[i]) || !float.IsFinite(b[i]))
            {
                if (!(float.IsNaN(a[i]) && float.IsNaN(b[i])))
                {
                    return false;
                }

                continue;
            }

            if (Math.Abs(a[i] - b[i]) > 1e-6f)
            {
                return false;
            }
        }

        return true;
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
