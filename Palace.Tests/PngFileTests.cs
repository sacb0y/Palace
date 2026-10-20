using System.IO.Compression;
using System.Text;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class PngFileTests
{
    [Fact]
    public void TryReadIhdr_ReadsBitDepthAndColorType()
    {
        var png = BuildPng(width: 4, height: 2, bitDepth: 16, colorType: 2, pixels16: SolidRgb16(4 * 2, 0x8000, 0x4000, 0x2000));
        var ihdr = PngFile.TryReadIhdr(png);
        Assert.NotNull(ihdr);
        Assert.Equal(16, ihdr.Value.BitDepth);
        Assert.Equal(2, ihdr.Value.ColorType);
        Assert.Equal(4, ihdr.Value.Width);
        Assert.Equal(2, ihdr.Value.Height);
        Assert.False(ihdr.Value.Interlaced);
    }

    [Fact]
    public void Probe_CicpPq_ParsesMatrixFullRangeAndBitDepth()
    {
        var png = BuildPng(
            width: 1,
            height: 1,
            bitDepth: 16,
            colorType: 2,
            pixels16: SolidRgb16(1, 0xFFFF, 0, 0),
            extra: [("cICP", [9, 16, 0, 1]), ("cLLi", [0, 0, 0x27, 0x10, 0, 0, 0, 0])]);
        var probe = HdrFile.Probe(png);
        Assert.Equal(HdrKind.HdrPng, probe.Kind);
        Assert.Equal(9, probe.CicpPrimaries);
        Assert.Equal(16, probe.CicpTransfer);
        Assert.Equal(0, probe.CicpMatrix);
        Assert.True(probe.FullRange);
        Assert.Equal(16, probe.BitDepth);
        Assert.Equal(1, probe.MaxCllNits);
        Assert.True(probe.CanPresentHdr);
        Assert.True(PngFile.ShouldDecodeManaged(probe));
    }

    [Fact]
    public void TryDecodeEncodedRgba_16BitRgb_KeepsPqCodes()
    {
        // PQ code ~0.5 → mid grey in ST 2084; keep the raw 16-bit sample.
        const ushort code = 0x8000;
        var png = BuildPng(
            width: 2,
            height: 1,
            bitDepth: 16,
            colorType: 2,
            pixels16: SolidRgb16(2, code, code, code),
            extra: [("cICP", [9, 16, 0, 1])]);

        var frame = PngFile.TryDecodeEncodedRgba(png);
        Assert.NotNull(frame);
        Assert.Equal(HdrPackedFormat.Rgba16, frame.Value.Format);
        Assert.Equal(2, frame.Value.Width);
        Assert.Equal(1, frame.Value.Height);

        HdrPixels.Read(frame.Value.Data, 0, HdrPackedFormat.Rgba16, out var r, out var g, out var b, out var a);
        Assert.Equal(code / 65535f, r, 1e-4);
        Assert.Equal(code / 65535f, g, 1e-4);
        Assert.Equal(code / 65535f, b, 1e-4);
        Assert.Equal(1f, a, 1e-4);

        var probe = HdrFile.Probe(png);
        GalleryPresent.PngEncodedToScrgb(r, g, b, probe.Transfer, probe.CicpPrimaries, out var sr, out var sg, out var sb, out var nitsY);
        Assert.True(nitsY > 80f, $"PQ mid should be well above SDR paper white; got {nitsY}");
        Assert.True(sr > 1f && sg > 1f && sb > 1f);
    }

    [Fact]
    public void TryDecodeEncodedRgba_8BitRgba_FilterNone()
    {
        var png = BuildPng(
            width: 1,
            height: 1,
            bitDepth: 8,
            colorType: 6,
            pixels8: [10, 20, 30, 40]);
        var frame = PngFile.TryDecodeEncodedRgba(png);
        Assert.NotNull(frame);
        Assert.Equal(HdrPackedFormat.Rgba8, frame.Value.Format);
        Assert.Equal([10, 20, 30, 40], frame.Value.Data);
    }

    [Fact]
    public void FormatSwapChainLabel_ReportsScrgbPresent()
    {
        Assert.Equal(
            "R16G16B16A16_FLOAT/scRGB(G10/P709)",
            GalleryPresent.FormatSwapChainLabel(
                GalleryPresent.ScrgbSwapChainFormat,
                GalleryPresent.ScrgbColorSpace));
        Assert.Contains(
            "swap=R16G16B16A16_FLOAT/scRGB(G10/P709)",
            GalleryPresent.FormatDisplayProbe(0, 270, 270, 8, true, true, swapChainFormat: 10, swapChainColorSpace: 1));
    }

    private static ushort[] SolidRgb16(int pixels, ushort r, ushort g, ushort b)
    {
        var data = new ushort[pixels * 3];
        for (var i = 0; i < pixels; i++)
        {
            data[i * 3] = r;
            data[i * 3 + 1] = g;
            data[i * 3 + 2] = b;
        }

        return data;
    }

    private static byte[] BuildPng(
        int width,
        int height,
        int bitDepth,
        int colorType,
        byte[]? pixels8 = null,
        ushort[]? pixels16 = null,
        params (string Type, byte[] Payload)[] extra)
    {
        var channels = colorType == 6 ? 4 : 3;
        var bpp = channels * (bitDepth / 8);
        var stride = width * bpp;
        var raw = new byte[(stride + 1) * height];
        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0; // filter None
            if (bitDepth == 8 && pixels8 is not null)
            {
                Buffer.BlockCopy(pixels8, y * stride, raw, y * (stride + 1) + 1, stride);
            }
            else if (bitDepth == 16 && pixels16 is not null)
            {
                var row = y * width * channels;
                var dest = y * (stride + 1) + 1;
                for (var i = 0; i < width * channels; i++)
                {
                    var sample = pixels16[row + i];
                    raw[dest + i * 2] = (byte)(sample >> 8);
                    raw[dest + i * 2 + 1] = (byte)sample;
                }
            }
        }

        using var ms = new MemoryStream();
        ms.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBe32(ihdr, 0, width);
        WriteBe32(ihdr, 4, height);
        ihdr[8] = (byte)bitDepth;
        ihdr[9] = (byte)colorType;
        WriteChunk(ms, "IHDR", ihdr);
        foreach (var (type, payload) in extra)
        {
            WriteChunk(ms, type, payload);
        }

        WriteChunk(ms, "IDAT", ZlibCompress(raw));
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static byte[] ZlibCompress(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return ms.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] payload)
    {
        WriteBe32(stream, payload.Length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(payload);
        // PNG CRC over type+payload; decoder ignores CRC.
        stream.Write(new byte[4]);
    }

    private static void WriteBe32(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteBe32(byte[] dest, int offset, int value)
    {
        dest[offset] = (byte)(value >> 24);
        dest[offset + 1] = (byte)(value >> 16);
        dest[offset + 2] = (byte)(value >> 8);
        dest[offset + 3] = (byte)value;
    }
}
