using System.Text;
using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class StillFormatTests
{
    [Fact]
    public void PathSafe_IndexesSkivStills_NotExr()
    {
        foreach (var ext in new[]
                 {
                     ".avif", ".heic", ".heif", ".jxr", ".wdp", ".hdp",
                     ".jxl", ".hdr", ".psd", ".dds"
                 })
        {
            Assert.True(PathSafe.IsCatalogExt(ext), ext);
            Assert.Equal(AssetKind.Image, PathSafe.KindFromExt(ext));
        }

        Assert.False(PathSafe.IsCatalogExt(".exr"));
        Assert.False(PathSafe.IsCatalogExt(".tga"));
        Assert.True(PathSafe.IsHeif(".heic"));
        Assert.True(PathSafe.IsRadiance(@"D:\a.hdr"));
        Assert.True(PathSafe.IsPsd(".psd"));
        Assert.True(PathSafe.IsJxl(".jxl"));
        Assert.True(PathSafe.IsJxr(".wdp"));
        Assert.True(PathSafe.UsesShellStillThumb(@"D:\a.heic"));
        Assert.True(PathSafe.UsesShellStillThumb(@"D:\a.psd"));
        Assert.False(PathSafe.UsesShellStillThumb(@"D:\a.jpg"));
    }

    [Fact]
    public void ShellThumbs_HeicPsdAvif_NeverOpenOriginal()
    {
        Assert.True(GalleryMedia.UsesShellThumbnail(AssetKind.Image, @"D:\a.heic"));
        Assert.True(GalleryMedia.UsesShellThumbnail(AssetKind.Image, @"D:\a.psd"));
        Assert.True(GalleryMedia.UsesShellThumbnail(AssetKind.Image, @"D:\a.avif"));
        Assert.False(GalleryMedia.MayOpenOriginalForThumb(false, AssetKind.Image, @"D:\a.heic"));
        Assert.False(GalleryMedia.MayOpenOriginalForThumb(false, AssetKind.Image, @"D:\a.psd"));
        Assert.False(GalleryMedia.MayOpenOriginalForThumb(true, AssetKind.Image, @"D:\a.jpg"));
        Assert.False(GalleryMedia.ShouldUpgradeThumb(true, false, false, @"D:\a.heic", "abc"));
        Assert.True(GalleryMedia.PrefersCachedThumbStill(@"D:\board.psd"));
        Assert.Equal(
            @"C:\thumbs\psd.jpg",
            GalleryMedia.OverlayStillPath(false, false, true, @"D:\board.psd", @"C:\thumbs\psd.jpg", null));
    }

    [Fact]
    public void Radiance_HeaderAndRgbe()
    {
        var bytes = RadianceBytes();
        Assert.True(RadianceFile.IsRadiance(bytes));
        Assert.Equal(HdrKind.HdrRadiance, HdrFile.Probe(bytes).Kind);
        Assert.True(HdrFile.Probe(bytes).CanPresentHdr);
        Assert.Equal(HdrTransfer.Scrgb, HdrFile.Probe(bytes).Transfer);
        using var stream = new MemoryStream(bytes);
        Assert.Equal((1, 1), RadianceFile.TryReadSize(stream));
        stream.Position = 0;
        var frame = RadianceFile.TryDecode(stream);
        Assert.NotNull(frame);
        Assert.Equal(1, frame!.Width);
        Assert.InRange(frame.ScrgbRgba[0], 0.49f, 0.51f);
        Assert.Equal((1, 1), ImageDimensions.TryRead(new MemoryStream(bytes)));
    }

    [Fact]
    public void Psd_8bpsSize()
    {
        var psd = PsdHeader(200, 100);
        Assert.True(StillFormats.IsPsd(psd));
        Assert.Equal((200, 100), StillFormats.TryReadPsdSize(psd));
        Assert.Equal((200, 100), ImageDimensions.TryRead(new MemoryStream(psd)));
        Assert.Equal(HdrKind.None, HdrFile.Probe(psd).Kind);
    }

    [Fact]
    public void JxlAndJxr_Signatures()
    {
        var jxl = new byte[] { 0xFF, 0x0A, 0xF8, 0x01, 0x5E, 0x00 };
        Assert.True(StillFormats.IsJxl(jxl));
        var size = StillFormats.TryReadJxlSize(jxl);
        Assert.Equal((48, 64), size);
        Assert.True(StillFormats.IsJxl(StillFormats.JxlContainerSignature));

        var jxr = new byte[] { 0x49, 0x49, 0xBC, 0x01, 8, 0, 0, 0, 0, 0 };
        Assert.True(StillFormats.IsJxr(jxr));
        Assert.False(StillFormats.JxrLooksHdr(jxr));
        Assert.Equal(HdrKind.None, HdrFile.Probe(jxr).Kind);
    }

    [Fact]
    public void Dds_Size()
    {
        var dds = new byte[20];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(dds, 0);
        BitConverter.GetBytes(64).CopyTo(dds, 12);
        BitConverter.GetBytes(32).CopyTo(dds, 16);
        Assert.True(StillFormats.IsDds(dds));
        Assert.Equal((32, 64), StillFormats.TryReadDdsSize(dds));
    }

    [Fact]
    public void Avif_10BitAv1CWithoutColr_IsHdr()
    {
        var avif = AvifWithAv1C(64, 48, highBitDepth: true);
        var info = AvifFile.Probe(avif);
        Assert.Equal(10, info.BitDepth);
        Assert.Null(info.CicpTransfer);
        var probe = HdrFile.Probe(avif);
        Assert.Equal(HdrKind.HdrAvif, probe.Kind);
        Assert.True(probe.CanPresentHdr);
        Assert.Equal(HdrTransfer.Pq, probe.Transfer);
        Assert.Equal(10, probe.BitDepth);
    }

    [Fact]
    public void Avif_NclxMatrixAndRange_RoundTrip()
    {
        var avif = AvifWith(32, 32, 9, 16, matrix: 9, fullRange: true);
        var info = AvifFile.Probe(avif);
        Assert.Equal(9, info.CicpMatrix);
        Assert.True(info.FullRange);
        Assert.True(info.IsAvif);
        var probe = AvifFile.ToHdrProbe(info);
        Assert.Equal(9, probe.CicpMatrix);
        Assert.True(probe.FullRange);
    }

    [Fact]
    public void OnlineOnly_ProbeAndDimensions_DoNotOpenMissing()
    {
        Assert.Equal(HdrKind.None, HdrFile.ProbePath(@"Z:\no\such\cloud.avif").Kind);
        Assert.Null(ImageDimensions.TryRead(@"Z:\no\such\cloud.hdr"));
        Assert.False(GalleryMedia.MayOpenOriginalForThumb(true, AssetKind.Image, @"Z:\a.avif"));
        Assert.False(GalleryMedia.ShouldRequestMosaicThumb(false, @"Z:\a.heic", "h", null, true));
    }

    private static byte[] RadianceBytes()
    {
        using var ms = new MemoryStream();
        var header = Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 1 +X 1\n");
        ms.Write(header);
        ms.WriteByte(128);
        ms.WriteByte(128);
        ms.WriteByte(128);
        ms.WriteByte(128);
        return ms.ToArray();
    }

    private static byte[] PsdHeader(int width, int height)
    {
        var data = new byte[26];
        Encoding.ASCII.GetBytes("8BPS").CopyTo(data, 0);
        data[5] = 1;
        data[13] = 3;
        WriteBe32(data, 14, height);
        WriteBe32(data, 18, width);
        data[23] = 8;
        data[25] = 3;
        return data;
    }

    private static void WriteBe32(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private static byte[] AvifWithAv1C(int width, int height, bool highBitDepth)
    {
        using var ipco = new MemoryStream();
        WriteBox(ipco, "ispe", [.. Be32(width), .. Be32(height)], fullBox: true);
        var flags = (byte)(highBitDepth ? 0x40 : 0);
        WriteBox(ipco, "av1C", [0x81, 0x00, flags, 0x00]);
        using var iprp = new MemoryStream();
        WriteBox(iprp, "ipco", ipco.ToArray());
        using var meta = new MemoryStream();
        WriteBox(meta, "iprp", iprp.ToArray());
        using var ms = new MemoryStream();
        WriteBox(ms, "ftyp", [.. Encoding.ASCII.GetBytes("avif"), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes("avif"), .. Encoding.ASCII.GetBytes("mif1")]);
        WriteBox(ms, "meta", meta.ToArray(), fullBox: true);
        return ms.ToArray();
    }

    private static byte[] AvifWith(int width, int height, int primaries, int transfer, int matrix, bool fullRange)
    {
        using var ipco = new MemoryStream();
        WriteBox(ipco, "ispe", [.. Be32(width), .. Be32(height)], fullBox: true);
        WriteBox(ipco, "colr", Nclx(primaries, transfer, matrix, fullRange));
        using var iprp = new MemoryStream();
        WriteBox(iprp, "ipco", ipco.ToArray());
        using var meta = new MemoryStream();
        WriteBox(meta, "iprp", iprp.ToArray());
        using var ms = new MemoryStream();
        WriteBox(ms, "ftyp", [.. Encoding.ASCII.GetBytes("avif"), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes("avif")]);
        WriteBox(ms, "meta", meta.ToArray(), fullBox: true);
        return ms.ToArray();
    }

    private static byte[] Nclx(int primaries, int transfer, int matrix, bool fullRange)
    {
        var nclx = new byte[11];
        Encoding.ASCII.GetBytes("nclx").CopyTo(nclx, 0);
        nclx[5] = (byte)primaries;
        nclx[7] = (byte)transfer;
        nclx[9] = (byte)matrix;
        nclx[10] = (byte)(fullRange ? 0x80 : 0);
        return nclx;
    }

    private static void WriteBox(Stream stream, string type, byte[] payload, bool fullBox = false)
    {
        var extra = fullBox ? 4 : 0;
        var size = 8 + extra + payload.Length;
        stream.WriteByte((byte)(size >> 24));
        stream.WriteByte((byte)(size >> 16));
        stream.WriteByte((byte)(size >> 8));
        stream.WriteByte((byte)size);
        stream.Write(Encoding.ASCII.GetBytes(type));
        if (fullBox)
        {
            stream.Write(new byte[4]);
        }

        stream.Write(payload);
    }

    private static byte[] Be32(int value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
