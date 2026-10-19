using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// ICC-only Display P3 PNGs, EXR source primaries on Info, native MaxCLL channel,
/// undecodable resolution and D65-adapted ACES gamut classification.
/// </summary>
public sealed class ColourInfoFollowupTests
{
    private static readonly float[] AcesCgChroma = [0.713f, 0.293f, 0.165f, 0.830f, 0.128f, 0.044f, 0.32168f, 0.33767f];
    private static readonly float[] AcesAp0Chroma = [0.7347f, 0.2653f, 0.0f, 1.0f, 0.0001f, -0.0770f, 0.32168f, 0.33767f];

    // Apple Display P3 colorants (D50-adapted) and sRGB's.
    private static readonly double[][] P3Colorants =
        [[0.515102, 0.241182, -0.001049], [0.291965, 0.692236, 0.041882], [0.157153, 0.066582, 0.784066]];
    private static readonly double[][] SrgbColorants =
        [[0.436066, 0.222488, 0.013916], [0.385147, 0.716873, 0.097076], [0.143066, 0.060608, 0.714096]];

    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0f, 0f, 1f)]
    [InlineData(4f, 0f, 0f)]
    [InlineData(0f, 4f, 0f)]
    [InlineData(0f, 0f, 4f)]
    public void Gamut_AcesCgPrimaries_ReadAsAp1AfterD65Adaptation(float r, float g, float b)
    {
        var (sr, sg, sb) = ToScrgb(AcesCgChroma, r, g, b);
        var y = GalleryPresent.LuminanceY(sr * 80, sg * 80, sb * 80, false);
        Assert.Equal(GamutKind.AcesAp1, CieGamut.ClassifyScrgb(sr, sg, sb, y));
    }

    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0f, 0f, 1f)]
    public void Gamut_Ap0Primaries_ReadAsAp0NotInvalid(float r, float g, float b)
    {
        var (sr, sg, sb) = ToScrgb(AcesAp0Chroma, r, g, b);
        var y = Math.Max(GalleryPresent.LuminanceY(sr * 80, sg * 80, sb * 80, false), 1f);
        Assert.Equal(GamutKind.AcesAp0, CieGamut.ClassifyScrgb(sr, sg, sb, y));
    }

    [Fact]
    public void Gamut_AdaptedAcesTriangles_MatchExrDecodeMatrix()
    {
        AssertPrimaries(CieGamut.AcesAp1, AcesCgChroma);
        AssertPrimaries(CieGamut.AcesAp0, AcesAp0Chroma);
    }

    [Fact]
    public void Gamut_EdgeTolerance_KeepsRoundedPrimariesInside()
    {
        // Rec.709 red nudged 3e-4 outside along the R–G edge normal stays Rec.709; 3e-3 does not.
        var p = CieGamut.Rec709;
        var (nx, ny) = OutwardNormal(p.Rx, p.Ry, p.Gx, p.Gy, p.Bx, p.By);
        var mx = (p.Rx + p.Gx) / 2;
        var my = (p.Ry + p.Gy) / 2;
        Assert.True(CieGamut.Contains(p, mx + (nx * 3e-4), my + (ny * 3e-4)));
        Assert.False(CieGamut.Contains(p, mx + (nx * 3e-3), my + (ny * 3e-3)));
    }

    [Fact]
    public void ExrPrimariesName_MatchesKnownSets()
    {
        Assert.Equal("BT.709", ExrFile.PrimariesName(null));
        Assert.Equal("BT.709", ExrFile.PrimariesName(ExrFile.Rec709Chromaticities));
        Assert.Equal("ACEScg", ExrFile.PrimariesName(AcesCgChroma));
        Assert.Equal("ACES2065-1", ExrFile.PrimariesName(AcesAp0Chroma));
        Assert.Equal("BT.2020", ExrFile.PrimariesName([0.708f, 0.292f, 0.170f, 0.797f, 0.131f, 0.046f, 0.3127f, 0.329f]));
        Assert.Equal("Display P3", ExrFile.PrimariesName([0.68f, 0.32f, 0.265f, 0.69f, 0.15f, 0.06f, 0.3127f, 0.329f]));
        Assert.Equal("DCI-P3", ExrFile.PrimariesName([0.68f, 0.32f, 0.265f, 0.69f, 0.15f, 0.06f, 0.314f, 0.351f]));
        Assert.Equal("Custom primaries", ExrFile.PrimariesName([0.6f, 0.35f, 0.3f, 0.6f, 0.15f, 0.06f, 0.3127f, 0.329f]));
    }

    [Fact]
    public void ColorLabel_ExrShowsSourcePrimariesToScrgb()
    {
        Assert.Equal("ACEScg → scRGB",
            GalleryPresent.ColorLabel(new HdrProbe(HdrKind.HdrExr, 1, null, null, SourcePrimaries: "ACEScg")));
        Assert.Equal("ACES2065-1 → scRGB",
            GalleryPresent.ColorLabel(new HdrProbe(HdrKind.HdrExr, 1, null, null, SourcePrimaries: "ACES2065-1")));
        Assert.Equal("BT.709 · scRGB",
            GalleryPresent.ColorLabel(new HdrProbe(HdrKind.HdrExr, 1, null, null, SourcePrimaries: "BT.709")));
        Assert.Equal("BT.709 · scRGB", GalleryPresent.ColorLabel(new HdrProbe(HdrKind.HdrExr, 1, null, null)));
    }

    [Fact]
    public void MaxCllChannel_PrefersNativeChannelOverBoxFilteredAnalysis()
    {
        // Downscaled file: analysis peak 20 (blue) vs native peak 40 (red).
        Assert.Equal('R', GalleryPresent.MaxCllChannelFor('B', 20f, 40f, 'R'));
        Assert.Equal('G', GalleryPresent.MaxCllChannelFor(' ', 0f, 40f, 'G'));
        Assert.Null(GalleryPresent.MaxCllChannelFor('B', 20f, 40f));
        Assert.Null(GalleryPresent.MaxCllChannelFor(' ', 40f, 40f));
        Assert.Null(GalleryPresent.MaxCllChannelFor('R', 40f, 0f, 'R'));
        // Analysis within 0.5% still names its channel when the decoder has none.
        Assert.Equal('B', GalleryPresent.MaxCllChannelFor('B', 39.9f, 40f));
    }

    [Fact]
    public void ScrgbFrame_TracksNativeMaxChannel()
    {
        float[] rgba = [1f, 5f, 2f, 1f, 4.9f, 0f, 0f, 1f];
        Assert.Equal('G', RadianceFile.FrameFromScrgb(rgba, 2, 1).MaxScrgbChannel);
        Assert.Equal('\0', RadianceFile.FrameFromScrgb(new float[8], 2, 1).MaxScrgbChannel);
        // Ties go R, then G, like HdrAnalysis.
        Assert.Equal('R', RadianceFile.FrameFromScrgb([3f, 3f, 3f, 1f], 1, 1).MaxScrgbChannel);
    }

    [Fact]
    public void FilePixelSize_UndecodableShowsNoResolutionUnlessDecoded()
    {
        Assert.Null(GalleryPresent.FilePixelSize(null, null, 64, 64, 64, 64, undecodable: true));
        Assert.Null(GalleryPresent.FilePixelSize(0, 0, null, null, 64, 64, undecodable: true));
        Assert.Equal((64, 64), GalleryPresent.FilePixelSize(64, 64, null, null, null, null, undecodable: true));
        Assert.Equal((64, 64), GalleryPresent.FilePixelSize(null, null, null, null, 64, 64));
    }

    [Fact]
    public void Probe_IccOnlyDisplayP3Png_IsWideGamutP3()
    {
        var probe = HdrFile.Probe(Png(("iCCP", Iccp(IccRgb(P3Colorants, Para(2.4))))));
        Assert.Equal(HdrKind.WideGamutPng, probe.Kind);
        Assert.Equal(12, probe.CicpPrimaries);
        Assert.Equal(13, probe.CicpTransfer);
        Assert.Equal(HdrTransfer.Srgb, probe.Transfer);
        Assert.Equal("Display P3 (ICC)", probe.SourcePrimaries);
        Assert.Equal("Display P3 (ICC) · sRGB", GalleryPresent.ColorLabel(probe));
        Assert.Equal(9, GalleryPresent.AnalysisPrimaries(probe));
    }

    [Fact]
    public void Probe_IccP3WithSrgbTableCurve_IsWideGamutP3()
    {
        var probe = HdrFile.Probe(Png(("iCCP", Iccp(IccRgb(P3Colorants, SrgbTable())))));
        Assert.Equal(HdrKind.WideGamutPng, probe.Kind);
        Assert.Equal(12, probe.CicpPrimaries);
    }

    [Fact]
    public void Probe_IccNotRecognised_StaysPlainPng()
    {
        // sRGB colorants, linear P3, P3 with an sRGB chunk, and a corrupt profile.
        Assert.Equal(HdrKind.None, HdrFile.Probe(Png(("iCCP", Iccp(IccRgb(SrgbColorants, Para(2.4)))))).Kind);
        Assert.Equal(HdrKind.None, HdrFile.Probe(Png(("iCCP", Iccp(IccRgb(P3Colorants, Para(1.0)))))).Kind);
        Assert.Equal(HdrKind.None,
            HdrFile.Probe(Png(("sRGB", [0]), ("iCCP", Iccp(IccRgb(P3Colorants, Para(2.4)))))).Kind);
        Assert.Equal(HdrKind.None, HdrFile.Probe(Png(("iCCP", [(byte)'x', 0, 0, 1, 2, 3]))).Kind);
        Assert.Null(IccProfile.MatchWideGamut(new byte[64]));
    }

    [Fact]
    public void Probe_CicpWinsOverIcc()
    {
        var probe = HdrFile.Probe(Png(("cICP", [9, 13, 0, 1]), ("iCCP", Iccp(IccRgb(P3Colorants, Para(2.4))))));
        Assert.Equal(HdrKind.WideGamutPng, probe.Kind);
        Assert.Equal(9, probe.CicpPrimaries);
        Assert.Null(probe.SourcePrimaries);
    }

    [Fact]
    public void Icc_NominalD65Bt2020Colorants_Match()
    {
        double[][] c2020 =
        [
            XyzFromXy(0.708, 0.292, 0.2627), XyzFromXy(0.170, 0.797, 0.6780), XyzFromXy(0.131, 0.046, 0.0593)
        ];
        Assert.Equal((9, "BT.2020"), IccProfile.MatchWideGamut(IccRgb(c2020, Para(2.4))));
    }

    private static (float R, float G, float B) ToScrgb(float[] chroma, float r, float g, float b)
    {
        var m = ExrFile.ToRec709Matrix(chroma)!;
        return ((float)((m[0] * r) + (m[1] * g) + (m[2] * b)),
            (float)((m[3] * r) + (m[4] * g) + (m[5] * b)),
            (float)((m[6] * r) + (m[7] * g) + (m[8] * b)));
    }

    private static void AssertPrimaries(CieGamut.Primaries p, float[] chroma)
    {
        double[] got = [p.Rx, p.Ry, p.Gx, p.Gy, p.Bx, p.By];
        for (var i = 0; i < 3; i++)
        {
            var (r, g, b) = ToScrgb(chroma, i == 0 ? 1 : 0, i == 1 ? 1 : 0, i == 2 ? 1 : 0);
            var x = (0.4124 * r) + (0.3576 * g) + (0.1805 * b);
            var y = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
            var z = (0.0193 * r) + (0.1192 * g) + (0.9505 * b);
            Assert.Equal(x / (x + y + z), got[i * 2], 3);
            Assert.Equal(y / (x + y + z), got[(i * 2) + 1], 3);
        }
    }

    private static (double X, double Y) OutwardNormal(double ax, double ay, double bx, double by, double cx, double cy)
    {
        var ex = bx - ax;
        var ey = by - ay;
        var len = Math.Sqrt((ex * ex) + (ey * ey));
        var (nx, ny) = (ey / len, -ex / len);
        // Flip toward the side away from the third vertex.
        return ((nx * (cx - ax)) + (ny * (cy - ay))) > 0 ? (-nx, -ny) : (nx, ny);
    }

    private static double[] XyzFromXy(double x, double y, double bigY) =>
        [x / y * bigY, bigY, (1 - x - y) / y * bigY];

    private static byte[] Para(double gamma)
    {
        var tag = new byte[12 + (4 * 7)];
        "para"u8.CopyTo(tag);
        BinaryPrimitives.WriteUInt16BigEndian(tag.AsSpan(8), 3);
        double[] p = [gamma, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045, 0, 0];
        for (var i = 0; i < 5; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(tag.AsSpan(12 + (i * 4)), (int)Math.Round(p[i] * 65536));
        }

        return tag[..(12 + 20)];
    }

    private static byte[] SrgbTable()
    {
        const int n = 1024;
        var tag = new byte[12 + (n * 2)];
        "curv"u8.CopyTo(tag);
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(8), n);
        for (var i = 0; i < n; i++)
        {
            var v = i / (double)(n - 1);
            var lin = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            BinaryPrimitives.WriteUInt16BigEndian(tag.AsSpan(12 + (i * 2)), (ushort)Math.Round(lin * 65535));
        }

        return tag;
    }

    private static byte[] IccRgb(double[][] colorants, byte[] trc)
    {
        var tags = new List<(string Sig, byte[] Data)>();
        string[] sigs = ["rXYZ", "gXYZ", "bXYZ"];
        for (var i = 0; i < 3; i++)
        {
            var xyz = new byte[20];
            "XYZ "u8.CopyTo(xyz);
            for (var k = 0; k < 3; k++)
            {
                BinaryPrimitives.WriteInt32BigEndian(xyz.AsSpan(8 + (k * 4)), (int)Math.Round(colorants[i][k] * 65536));
            }

            tags.Add((sigs[i], xyz));
        }

        tags.Add(("rTRC", trc));
        tags.Add(("gTRC", trc));
        tags.Add(("bTRC", trc));

        var offset = 128 + 4 + (12 * tags.Count);
        using var body = new MemoryStream();
        var table = new byte[4 + (12 * tags.Count)];
        BinaryPrimitives.WriteUInt32BigEndian(table, (uint)tags.Count);
        for (var t = 0; t < tags.Count; t++)
        {
            Encoding.ASCII.GetBytes(tags[t].Sig).CopyTo(table, 4 + (t * 12));
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(8 + (t * 12)), (uint)(offset + body.Length));
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(12 + (t * 12)), (uint)tags[t].Data.Length);
            body.Write(tags[t].Data);
            while (body.Length % 4 != 0)
            {
                body.WriteByte(0);
            }
        }

        var header = new byte[128];
        "mntr"u8.CopyTo(header.AsSpan(12));
        "RGB "u8.CopyTo(header.AsSpan(16));
        "XYZ "u8.CopyTo(header.AsSpan(20));
        "acsp"u8.CopyTo(header.AsSpan(36));
        var icc = header.Concat(table).Concat(body.ToArray()).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(icc, (uint)icc.Length);
        return icc;
    }

    private static byte[] Iccp(byte[] profile)
    {
        using var ms = new MemoryStream();
        ms.Write("Display P3"u8);
        ms.WriteByte(0);
        ms.WriteByte(0);
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(profile);
        }

        return ms.ToArray();
    }

    private static byte[] Png(params (string Type, byte[] Payload)[] chunks)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(ms, "IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]);
        foreach (var (type, payload) in chunks)
        {
            Chunk(ms, type, payload);
        }

        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] payload)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, payload.Length);
        s.Write(len);
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(payload);
        s.Write(new byte[4]);
    }
}
