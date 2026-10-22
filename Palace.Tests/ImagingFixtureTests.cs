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
    public void Exr_ReadsChromaticitiesAttribute_Bt709()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ImagingFixtureIO.Dir, "master.exr"));
        Assert.True(ExrHeader.TryReadChromaticities(bytes, out var chrom));
        Assert.Equal(ColorPrimaries.Bt709, chrom.MatchKnown());
        Assert.True(ExrHeader.TryReadWhiteLuminance(bytes, out var wl));
        Assert.InRange(wl, 200f, 210f);

        // M3: chromaticities feed EffectivePrimaries (not ignored).
        var opts = new NormalizeOptions(
            TransferKind.LinearScene,
            Primaries: ColorPrimaries.Bt2020, // wrong on purpose
            WhiteLuminance: wl,
            Chromaticities: chrom);
        Assert.Equal(ColorPrimaries.Bt709, opts.EffectivePrimaries);
    }

    [Fact]
    public void PngCicp_HasPqBt2020Chunks()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ImagingFixtureIO.Dir, "master.pq.cicp.png"));
        var ascii = Encoding.ASCII.GetString(bytes);
        Assert.Contains("cICP", ascii, StringComparison.Ordinal);
    }
}
