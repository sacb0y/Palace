using System.Text;
using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

/// <summary>M8: real encoded containers from one master must be checked in.</summary>
public class ImagingFixtureTests
{
    [Theory]
    [InlineData("master.jxl", 0, new byte[] { 0xFF, 0x0A })]
    [InlineData("master.avif", 4, new byte[] { (byte)'f', (byte)'t', (byte)'y', (byte)'p' })]
    [InlineData("master.pq.cicp.png", 0, new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData("master.jxr", 0, new byte[] { 0x49, 0x49, 0xBC, 0x01 })]
    [InlineData("master.exr", 0, new byte[] { 0x76, 0x2f, 0x31, 0x01 })]
    [InlineData("master.hdr", 0, new byte[] { (byte)'#', (byte)'?', (byte)'R', (byte)'A' })]
    public void EncodedMasterFixtures_PresentWithMagic(string name, int offset, byte[] magic)
    {
        var path = Path.Combine(ImagingFixtureIO.Dir, name);
        Assert.True(File.Exists(path), $"missing fixture {name}");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= offset + magic.Length);
        for (var i = 0; i < magic.Length; i++)
        {
            Assert.Equal(magic[i], bytes[offset + i]);
        }
    }

    [Fact]
    public void Exr_PregmataLike_NoWhiteLuminanceOrChromaticities()
    {
        // Pregmata_SteamAVIF EXR had neither attribute — master.exr matches that.
        var bytes = File.ReadAllBytes(Path.Combine(ImagingFixtureIO.Dir, "master.exr"));
        Assert.False(ExrHeader.TryReadChromaticities(bytes, out _));
        Assert.False(ExrHeader.TryReadWhiteLuminance(bytes, out _));
    }

    [Fact]
    public void ExrHeader_ReadsChromaticities_WhenPresent()
    {
        // Synthetic header with BT.709 chromaticities (M3 reader path).
        var chrom = Chromaticities.Bt709;
        Span<byte> payload = stackalloc byte[32];
        WriteF32(payload, 0, chrom.Rx);
        WriteF32(payload, 4, chrom.Ry);
        WriteF32(payload, 8, chrom.Gx);
        WriteF32(payload, 12, chrom.Gy);
        WriteF32(payload, 16, chrom.Bx);
        WriteF32(payload, 20, chrom.By);
        WriteF32(payload, 24, chrom.Wx);
        WriteF32(payload, 28, chrom.Wy);

        using var ms = new MemoryStream();
        ms.Write([0x76, 0x2f, 0x31, 0x01, 0x02, 0x00, 0x00, 0x00]); // magic + version
        WriteAttr(ms, "chromaticities", "chromaticities", payload);
        WriteAttr(ms, "whiteLuminance", "float", BitConverter.GetBytes(80f));
        ms.WriteByte(0); // end of header
        var file = ms.ToArray();

        Assert.True(ExrHeader.TryReadChromaticities(file, out var read));
        Assert.Equal(ColorPrimaries.Bt709, read.MatchKnown());
        Assert.True(ExrHeader.TryReadWhiteLuminance(file, out var wl));
        Assert.InRange(wl, 79f, 81f);

        var opts = new NormalizeOptions(
            TransferKind.LinearScene,
            Primaries: ColorPrimaries.Bt2020,
            WhiteLuminance: wl,
            Chromaticities: read);
        Assert.Equal(ColorPrimaries.Bt709, opts.EffectivePrimaries);
    }

    [Fact]
    public void Hdr_PregmataLike_NoExposureOrPrimariesLine()
    {
        var text = File.ReadAllText(Path.Combine(ImagingFixtureIO.Dir, "master.hdr"));
        Assert.DoesNotContain("EXPOSURE", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIMARIES", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PngCicp_HasPqBt2020Chunks()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ImagingFixtureIO.Dir, "master.pq.cicp.png"));
        var ascii = Encoding.ASCII.GetString(bytes);
        Assert.Contains("cICP", ascii, StringComparison.Ordinal);
    }

    /// <summary>M8: decoded container dumps must match live HDR decode within 1%.</summary>
    [Fact]
    public void DecodedFixtures_HdrMatchesLiveRadianceDecode()
    {
        var dumped = ImagingFixtureIO.LoadF32("master.hdr.decoded.f32");
        var (live, w, h) = ImagingFixtureIO.LoadHdr();
        Assert.Equal(2, w);
        Assert.Equal(2, h);
        for (var i = 0; i < dumped.Length; i++)
        {
            var tol = Math.Max(1e-4f, Math.Abs(dumped[i]) * 0.01f);
            Assert.True(Math.Abs(dumped[i] - live[i]) <= tol, $"hdr decode mismatch @ {i}");
        }
    }

    [Fact]
    public void Chromaticities_MatchKnown_IncludesDisplayP3()
    {
        Assert.Equal(ColorPrimaries.DisplayP3, Chromaticities.DisplayP3.MatchKnown());
    }

    private static void WriteF32(Span<byte> dest, int offset, float v) =>
        BitConverter.TryWriteBytes(dest[offset..], v);

    private static void WriteAttr(Stream s, string name, string type, ReadOnlySpan<byte> value)
    {
        var nb = Encoding.ASCII.GetBytes(name);
        s.Write(nb);
        s.WriteByte(0);
        var tb = Encoding.ASCII.GetBytes(type);
        s.Write(tb);
        s.WriteByte(0);
        s.Write(BitConverter.GetBytes(value.Length));
        s.Write(value);
    }
}
