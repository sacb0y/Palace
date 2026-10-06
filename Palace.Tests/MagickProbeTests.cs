using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Cross-platform Magick-free probes for Spike 0 formats. Decode/packaging
/// verification stays on Main-Desktop WinUI (Magick.NET natives).
/// </summary>
public sealed class MagickProbeTests
{
    [Fact]
    public void OpenExr_Magic_IsHdrExr()
    {
        var magic = new byte[] { 0x76, 0x2F, 0x31, 0x01, 0x00, 0x00 };
        Assert.True(HdrFile.IsOpenExr(magic));
        var probe = HdrFile.Probe(magic);
        Assert.Equal(HdrKind.HdrExr, probe.Kind);
        Assert.True(probe.IsHdr);
        Assert.True(probe.CanPresentHdr);
        Assert.Equal(HdrTransfer.Scrgb, probe.Transfer);
    }

    [Fact]
    public void MagickTga_PresentableViaExtensionProbeShape()
    {
        var probe = new HdrProbe(HdrKind.MagickTga, null, null, null);
        Assert.False(probe.IsHdr);
        Assert.True(probe.CanPresentHdr);
        Assert.Equal(HdrTransfer.Srgb, probe.Transfer);
        Assert.Equal("TGA", GalleryPresent.HdrKindLabel(HdrKind.MagickTga));
        Assert.Equal("OpenEXR", GalleryPresent.HdrKindLabel(HdrKind.HdrExr));
    }

    [Fact]
    public void CatalogHdr_ExrWithoutOpening()
    {
        Assert.True(GalleryMedia.CatalogHdrFromHeader(@"D:\shots\sky.exr", mayReadOriginalHeader: false));
        Assert.True(GalleryMedia.CatalogHdrFromHeader(@"D:\shots\sky.hdr", mayReadOriginalHeader: false));
        Assert.False(GalleryMedia.CatalogHdrFromHeader(@"D:\shots\a.tga", mayReadOriginalHeader: false));
    }
}
