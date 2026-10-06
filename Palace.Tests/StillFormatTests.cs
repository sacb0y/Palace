using System.Text;
using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class StillFormatTests
{
    [Fact]
    public void PathSafe_IndexesSkivStills_IncludingExrTga()
    {
        foreach (var ext in new[]
                 {
                     ".avif", ".heic", ".heif", ".jxr", ".wdp", ".hdp",
                     ".jxl", ".hdr", ".exr", ".tga", ".psd", ".dds"
                 })
        {
            Assert.True(PathSafe.IsCatalogExt(ext), ext);
            Assert.Equal(AssetKind.Image, PathSafe.KindFromExt(ext));
        }

        Assert.True(PathSafe.IsExr(".exr"));
        Assert.True(PathSafe.IsTga(".tga"));
        Assert.True(PathSafe.IsHeif(".heic"));
        Assert.True(PathSafe.IsRadiance(@"D:\a.hdr"));
        Assert.True(PathSafe.IsPsd(".psd"));
        Assert.True(PathSafe.IsJxl(".jxl"));
        Assert.True(PathSafe.IsJxr(".wdp"));
        Assert.True(PathSafe.UsesShellStillThumb(@"D:\a.heic"));
        Assert.False(PathSafe.UsesShellStillThumb(@"D:\a.psd"));
        Assert.True(PathSafe.UsesMagickStillThumb(@"D:\a.psd"));
        Assert.False(PathSafe.UsesShellStillThumb(@"D:\a.jpg"));
    }

    [Fact]
    public void ShellThumbs_HeicAvif_MagickPsd_NeverOpenOriginal()
    {
        Assert.True(GalleryMedia.UsesShellThumbnail(AssetKind.Image, @"D:\a.heic"));
        Assert.False(GalleryMedia.UsesShellThumbnail(AssetKind.Image, @"D:\a.psd"));
        Assert.True(GalleryMedia.UsesMagickThumbnail(AssetKind.Image, @"D:\a.psd"));
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
        Assert.Equal(HdrKind.None, HdrFile.Probe(jxl).Kind);
        Assert.True(StillFormats.IsJxl(StillFormats.JxlContainerSignature));

        var jxr = new byte[] { 0x49, 0x49, 0xBC, 0x01, 8, 0, 0, 0, 0, 0 };
        Assert.True(StillFormats.IsJxr(jxr));
        Assert.False(StillFormats.JxrLooksHdr(jxr));
        Assert.Equal(HdrKind.None, HdrFile.Probe(jxr).Kind);
        Assert.False(HdrFile.Probe(jxr).CanPresentHdr);
    }

    [Fact]
    public void Jxr_FloatAndHalfGuids_ArePresentableHdr()
    {
        var half = JxrHeader(StillFormats.JxrGuidRgbaHalf, 32, 16);
        var probeHalf = HdrFile.Probe(half);
        Assert.True(StillFormats.IsJxr(half));
        Assert.True(StillFormats.JxrLooksHdr(half));
        Assert.True(StillFormats.IsHdrJxrPixelFormat(StillFormats.JxrGuidRgbaHalf));
        Assert.Equal(HdrKind.HdrJxr, probeHalf.Kind);
        Assert.True(probeHalf.IsHdr);
        Assert.True(probeHalf.CanPresentHdr);
        Assert.Equal(HdrTransfer.Scrgb, probeHalf.Transfer);
        Assert.Equal((32, 16), StillFormats.TryReadJxrSize(half));
        Assert.Equal((32, 16), ImageDimensions.TryRead(new MemoryStream(half)));

        var flt = JxrHeader(StillFormats.JxrGuidRgbaFloat, 8, 4);
        var probeFloat = HdrFile.Probe(flt);
        Assert.Equal(HdrKind.HdrJxr, probeFloat.Kind);
        Assert.True(probeFloat.CanPresentHdr);
        Assert.True(StillFormats.IsHdrJxrPixelFormat(StillFormats.JxrGuidRgbaFloat));
        Assert.Equal((8, 4), StillFormats.TryReadJxrSize(flt));
    }

    [Fact]
    public void Jxr_1010102XrGuid_LooksHdr()
    {
        Assert.True(StillFormats.IsHdrJxrPixelFormat(StillFormats.JxrGuidRgba1010102Xr));
        var header = JxrHeader(StillFormats.JxrGuidRgba1010102Xr, 64, 32);
        Assert.True(StillFormats.JxrLooksHdr(header));
        Assert.Equal(HdrKind.HdrJxr, HdrFile.Probe(header).Kind);
        Assert.Equal((64, 32), StillFormats.TryReadJxrSize(header));
    }

    [Fact]
    public void Jxr_AnnexAIfd_ReadsPixelsNotDpiFloat()
    {
        // WIC / SKIV write 0xBC82/0xBC83 as FLOAT 96 DPI (0x42C00000 = 1119879168).
        var header = JxrHeader(StillFormats.JxrGuidRgbaHalf, 1920, 1080);
        Assert.Equal((1920, 1080), StillFormats.TryReadJxrSize(header));
        Assert.Equal((1920, 1080), ImageDimensions.TryRead(new MemoryStream(header)));
        var probe = HdrFile.Probe(header);
        Assert.Equal(HdrKind.HdrJxr, probe.Kind);
        Assert.True(probe.CanPresentHdr);
        Assert.Contains(
            "HDR JPEG XR",
            GalleryPresent.StatusLine(probe, true, true, false, 0),
            StringComparison.Ordinal);
        Assert.Contains(
            "HDR JPEG XR",
            GalleryPresent.ImageInfoText(new GalleryImageInfo(
                "cyberpunk-capture.jxr",
                28_200_000,
                1920,
                1080,
                probe,
                GalleryPresent.StatusLine(probe, true, true, false, 0),
                null, null, null, null)),
            StringComparison.Ordinal);
        Assert.DoesNotContain("1119879168", GalleryPresent.ImageInfoText(new GalleryImageInfo(
            "cyberpunk-capture.jxr",
            28_200_000,
            StillFormats.TryReadJxrSize(header)?.Width,
            StillFormats.TryReadJxrSize(header)?.Height,
            probe,
            GalleryPresent.StatusLine(probe, false, false, false, 0),
            null, null, null, null)));
    }

    [Fact]
    public void Jxr_8BitBgraGuid_IsNotHdr()
    {
        ReadOnlySpan<byte> bgra =
            [0x24, 0xC3, 0xDD, 0x6F, 0x03, 0x4E, 0xFE, 0x4B, 0xB1, 0x85, 0x3D, 0x77, 0x76, 0x8D, 0xC9, 0x0F];
        Assert.False(StillFormats.IsHdrJxrPixelFormat(bgra));
        var header = JxrHeader(bgra, 8, 8);
        Assert.False(StillFormats.JxrLooksHdr(header));
        Assert.Equal(HdrKind.None, HdrFile.Probe(header).Kind);
        Assert.False(HdrFile.Probe(header).CanPresentHdr);
        Assert.Equal((8, 8), StillFormats.TryReadJxrSize(header));

        ReadOnlySpan<byte> pbgra =
            [0x24, 0xC3, 0xDD, 0x6F, 0x03, 0x4E, 0xFE, 0x4B, 0xB1, 0x85, 0x3D, 0x77, 0x76, 0x8D, 0xC9, 0x10];
        Assert.False(StillFormats.IsHdrJxrPixelFormat(pbgra));
        Assert.False(StillFormats.JxrLooksHdr(JxrHeader(pbgra, 1920, 1080)));
        Assert.False(HdrFile.Probe(JxrHeader(pbgra, 1920, 1080)).CanPresentHdr);
    }

    [Fact]
    public void Jxl_CodestreamPq10Bit_IsPresentableHdr()
    {
        var jxl = JxlCodestream(transfer: 16, primaries: 9, bitDepth10: true);
        var probe = HdrFile.Probe(jxl);
        Assert.Equal(HdrKind.HdrJxl, probe.Kind);
        Assert.True(probe.CanPresentHdr);
        Assert.Equal(16, probe.CicpTransfer);
        Assert.Equal(9, probe.CicpPrimaries);
        Assert.Equal(10, probe.BitDepth);
        Assert.Equal((8, 8), StillFormats.TryReadJxlSize(jxl));
    }

    [Fact]
    public void Jxl_ContainerColrPq_IsPresentableHdr()
    {
        var jxl = JxlContainerWithColr(primaries: 9, transfer: 16, matrix: 9, fullRange: true);
        var probe = HdrFile.Probe(jxl);
        Assert.Equal(HdrKind.HdrJxl, probe.Kind);
        Assert.True(probe.CanPresentHdr);
        Assert.Equal(16, probe.CicpTransfer);
        Assert.Equal(9, probe.CicpPrimaries);
        Assert.True(probe.FullRange);
        Assert.Equal((8, 8), StillFormats.TryReadJxlSize(jxl));
    }

    [Fact]
    public void Jxl_10BitSrgb_IsNotHdr()
    {
        var jxl = JxlCodestream(transfer: 13, primaries: 1, bitDepth10: true);
        var probe = HdrFile.Probe(jxl);
        Assert.Equal(HdrKind.None, probe.Kind);
        Assert.False(probe.CanPresentHdr);
        Assert.Equal(HdrTransfer.Srgb, probe.Transfer);
        Assert.Equal((8, 8), StillFormats.TryReadJxlSize(jxl));
    }

    [Fact]
    public void Jxl_P3_IsWideGamut()
    {
        var stream = JxlCodestream(transfer: 13, primaries: 11, bitDepth10: false);
        var fromStream = HdrFile.Probe(stream);
        Assert.Equal(HdrKind.WideGamutJxl, fromStream.Kind);
        Assert.False(fromStream.CanPresentHdr);
        Assert.Equal(11, fromStream.CicpPrimaries);

        var colr = JxlContainerWithColr(primaries: 11, transfer: 13, matrix: 0, fullRange: true);
        var fromColr = HdrFile.Probe(colr);
        Assert.Equal(HdrKind.WideGamutJxl, fromColr.Kind);
        Assert.Equal(11, fromColr.CicpPrimaries);
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
    public void Avif_Av1CLimitedRange_IsNotFull()
    {
        var limited = AvifWithAv1CSequence(64, 48, fullRange: false);
        var info = AvifFile.Probe(limited);
        Assert.Equal(10, info.BitDepth);
        Assert.Equal(9, info.CicpPrimaries);
        Assert.Equal(16, info.CicpTransfer);
        Assert.Equal(9, info.CicpMatrix);
        Assert.False(info.FullRange);
        var probe = AvifFile.ToHdrProbe(info);
        Assert.False(probe.FullRange);
        Assert.True(probe.CanPresentHdr);

        var full = AvifWithAv1CSequence(64, 48, fullRange: true);
        Assert.True(AvifFile.Probe(full).FullRange);

        var colrWins = AvifWithColrAndAv1C(fullRangeColr: true, fullRangeAv1C: false);
        Assert.True(AvifFile.Probe(colrWins).FullRange);
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

        var identity = AvifWith(32, 32, 9, 16, matrix: 0, fullRange: true);
        var identityInfo = AvifFile.Probe(identity);
        Assert.Equal(0, identityInfo.CicpMatrix);
        Assert.True(identityInfo.FullRange);
        Assert.Equal(0, AvifFile.ToHdrProbe(identityInfo).CicpMatrix);
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

    private static byte[] JxrHeader(ReadOnlySpan<byte> pixelFormat, int width, int height)
    {
        // Annex A IFD: PIXEL_FORMAT 0xBC01, WIDTH 0xBC80, HEIGHT 0xBC81,
        // H/V resolution 0xBC82/0xBC83 as FLOAT 96 DPI (not pixels).
        const int ifd = 8;
        const int dpi96 = 0x42C00000;
        const int guidOff = 78;
        var data = new byte[guidOff + 16];
        data[0] = 0x49;
        data[1] = 0x49;
        data[2] = 0xBC;
        data[3] = 0x01;
        WriteLe32(data, 4, ifd);
        data[ifd] = 5;
        data[ifd + 1] = 0;
        WriteJxrEntry(data, ifd + 2, 0xBC01, type: 1, count: 16, value: guidOff);
        WriteJxrEntry(data, ifd + 14, 0xBC80, type: 4, count: 1, value: width);
        WriteJxrEntry(data, ifd + 26, 0xBC81, type: 4, count: 1, value: height);
        WriteJxrEntry(data, ifd + 38, 0xBC82, type: 11, count: 1, value: dpi96);
        WriteJxrEntry(data, ifd + 50, 0xBC83, type: 11, count: 1, value: dpi96);
        pixelFormat.CopyTo(data.AsSpan(guidOff, 16));
        return data;
    }

    private static void WriteJxrEntry(byte[] data, int offset, int tag, int type, int count, int value)
    {
        data[offset] = (byte)tag;
        data[offset + 1] = (byte)(tag >> 8);
        data[offset + 2] = (byte)type;
        data[offset + 3] = (byte)(type >> 8);
        WriteLe32(data, offset + 4, count);
        WriteLe32(data, offset + 8, value);
    }

    private static void WriteLe32(byte[] data, int offset, int value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteBe32(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private static byte[] JxlCodestream(int transfer, int primaries, bool bitDepth10)
    {
        var bits = new JxlLsbWriter();
        bits.Write(1, 1);
        bits.Write(5, 0);
        bits.Write(3, 1);
        bits.Write(1, 0);
        bits.Write(1, 0);
        bits.Write(1, 0);
        bits.Write(2, bitDepth10 ? 1 : 0);
        bits.Write(1, 1);
        bits.Write(2, 0);
        bits.Write(1, 0);
        bits.Write(1, 0);
        bits.Write(1, 0);
        bits.WriteEnum(0);
        bits.WriteEnum(1);
        bits.WriteEnum(primaries);
        bits.Write(1, 0);
        bits.WriteEnum(transfer);
        bits.WriteEnum(1);
        var body = bits.ToArray();
        var jxl = new byte[2 + body.Length];
        jxl[0] = 0xFF;
        jxl[1] = 0x0A;
        body.CopyTo(jxl, 2);
        return jxl;
    }

    private static byte[] JxlContainerWithColr(int primaries, int transfer, int matrix, bool fullRange)
    {
        var bits = new JxlLsbWriter();
        bits.Write(1, 1);
        bits.Write(5, 0);
        bits.Write(3, 1);
        bits.Write(1, 1);
        var codestream = new byte[2 + bits.ToArray().Length];
        codestream[0] = 0xFF;
        codestream[1] = 0x0A;
        bits.ToArray().CopyTo(codestream, 2);

        using var ms = new MemoryStream();
        ms.Write(StillFormats.JxlContainerSignature);
        WriteBox(ms, "colr", Nclx(primaries, transfer, matrix, fullRange));
        WriteBox(ms, "jxlc", codestream);
        return ms.ToArray();
    }

    private sealed class JxlLsbWriter
    {
        private readonly List<byte> _bytes = [];
        private int _bit;

        public void Write(int count, int value)
        {
            for (var i = 0; i < count; i++)
            {
                var byteIndex = _bit / 8;
                while (_bytes.Count <= byteIndex)
                {
                    _bytes.Add(0);
                }

                if (((value >> i) & 1) != 0)
                {
                    _bytes[byteIndex] |= (byte)(1 << (_bit % 8));
                }

                _bit++;
            }
        }

        public void WriteEnum(int value)
        {
            if (value == 0)
            {
                Write(2, 0);
            }
            else if (value == 1)
            {
                Write(2, 1);
            }
            else if (value < 18)
            {
                Write(2, 2);
                Write(4, value - 2);
            }
            else
            {
                Write(2, 3);
                Write(6, value - 18);
            }
        }

        public byte[] ToArray() => [.. _bytes];
    }

    private static byte[] AvifWithAv1CSequence(int width, int height, bool fullRange)
    {
        using var ipco = new MemoryStream();
        WriteBox(ipco, "ispe", [.. Be32(width), .. Be32(height)], fullBox: true);
        WriteBox(ipco, "av1C", Av1CWithSequence(fullRange));
        using var iprp = new MemoryStream();
        WriteBox(iprp, "ipco", ipco.ToArray());
        using var meta = new MemoryStream();
        WriteBox(meta, "iprp", iprp.ToArray());
        using var ms = new MemoryStream();
        WriteBox(ms, "ftyp", [.. Encoding.ASCII.GetBytes("avif"), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes("avif"), .. Encoding.ASCII.GetBytes("mif1")]);
        WriteBox(ms, "meta", meta.ToArray(), fullBox: true);
        return ms.ToArray();
    }

    private static byte[] AvifWithColrAndAv1C(bool fullRangeColr, bool fullRangeAv1C)
    {
        using var ipco = new MemoryStream();
        WriteBox(ipco, "ispe", [.. Be32(32), .. Be32(32)], fullBox: true);
        WriteBox(ipco, "colr", Nclx(9, 16, 9, fullRangeColr));
        WriteBox(ipco, "av1C", Av1CWithSequence(fullRangeAv1C));
        using var iprp = new MemoryStream();
        WriteBox(iprp, "ipco", ipco.ToArray());
        using var meta = new MemoryStream();
        WriteBox(meta, "iprp", iprp.ToArray());
        using var ms = new MemoryStream();
        WriteBox(ms, "ftyp", [.. Encoding.ASCII.GetBytes("avif"), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes("avif")]);
        WriteBox(ms, "meta", meta.ToArray(), fullBox: true);
        return ms.ToArray();
    }

    private static byte[] Av1CWithSequence(bool fullRange)
    {
        var obu = new Av1MsbWriter();
        obu.Write(1, 0);
        obu.Write(4, 1);
        obu.Write(1, 0);
        obu.Write(1, 0);
        obu.Write(1, 0);
        obu.Write(3, 0);
        obu.Write(1, 1);
        obu.Write(1, 1);
        obu.Write(5, 0);
        obu.Write(4, 0);
        obu.Write(4, 0);
        obu.Write(1, 0);
        obu.Write(1, 0);
        obu.Write(1, 0);
        obu.Write(1, 0);
        obu.Write(1, 0);
        obu.Write(1, 0);
        obu.Write(1, 0);
        obu.Write(1, 0);
        obu.Write(1, 1);
        obu.Write(1, 0);
        obu.Write(1, 1);
        obu.Write(8, 9);
        obu.Write(8, 16);
        obu.Write(8, 9);
        obu.Write(1, fullRange ? 1 : 0);
        return [0x81, 0x00, 0x40, 0x00, .. obu.ToArray()];
    }

    private sealed class Av1MsbWriter
    {
        private readonly List<byte> _bytes = [];
        private int _bit;

        public void Write(int count, int value)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                var byteIndex = _bit / 8;
                while (_bytes.Count <= byteIndex)
                {
                    _bytes.Add(0);
                }

                if (((value >> i) & 1) != 0)
                {
                    _bytes[byteIndex] |= (byte)(1 << (7 - (_bit % 8)));
                }

                _bit++;
            }
        }

        public byte[] ToArray() => [.. _bytes];
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
