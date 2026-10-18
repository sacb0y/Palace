using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class StillFormatDecodeTests
{
    // ---------- tone mapping ----------

    [Fact]
    public void ToneMap_SdrRangePassesThroughAsSrgb()
    {
        var px = new float[] { 0.5f, 0.5f, 0.5f, 1f, 1f, 0f, 0f, 1f };
        var bgra = ToneMap.ScrgbToBgra8(px, 2, 1);
        Assert.Equal(new byte[] { 188, 188, 188, 255, 0, 0, 255, 255 }, bgra);
    }

    [Fact]
    public void ToneMap_BrightHdrWhiteStaysNeutralAndBelowClip()
    {
        // Mid-grey scene with a 40x-bright white highlight.
        var count = 64;
        var px = new float[count * 4];
        for (var i = 0; i < count; i++)
        {
            var v = i == 0 ? 40f : 0.2f;
            px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = v;
            px[i * 4 + 3] = 1f;
        }

        var bgra = ToneMap.ScrgbToBgra8(px, 8, 8);
        Assert.Equal(bgra[0], bgra[1]);
        Assert.Equal(bgra[1], bgra[2]);
        Assert.True(bgra[0] > 200, $"highlight {bgra[0]}");
        Assert.True(bgra[4] is > 40 and < 200, $"mid-grey {bgra[4]}");
    }

    [Fact]
    public void ToneMap_SaturatedHdrRedKeepsHueInsteadOfFlatRed()
    {
        // Skin-like HDR colour, 6x over SDR white: must not collapse to (255,0,0).
        var px = new float[] { 6f, 3f, 1.8f, 1f, 0.1f, 0.1f, 0.1f, 1f };
        var bgra = ToneMap.ScrgbToBgra8(px, 2, 1);
        Assert.True(bgra[1] > 60, $"green {bgra[1]}");
        Assert.True(bgra[0] > 30, $"blue {bgra[0]}");
        Assert.True(bgra[2] >= bgra[1] && bgra[1] >= bgra[0]);
    }

    [Fact]
    public void ToneMap_IgnoresNaNAndInfinity()
    {
        var px = new float[] { float.NaN, float.PositiveInfinity, -3f, 1f };
        var bgra = ToneMap.ScrgbToBgra8(px, 1, 1);
        Assert.Equal(255, bgra[3]);
    }

    // ---------- TGA ----------

    private static byte[] TgaHeader(int type, int w, int h, int bpp, int desc, int cmapType = 0, int cmapLen = 0, int cmapBits = 0)
    {
        var hdr = new byte[18];
        hdr[1] = (byte)cmapType;
        hdr[2] = (byte)type;
        hdr[5] = (byte)cmapLen;
        hdr[7] = (byte)cmapBits;
        hdr[12] = (byte)w;
        hdr[14] = (byte)h;
        hdr[16] = (byte)bpp;
        hdr[17] = (byte)desc;
        return hdr;
    }

    [Fact]
    public void Tga_Truecolor24_BottomUp()
    {
        // 2x2, bottom row first: red, green / blue, white (BGR on disk).
        var body = new byte[] { 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255 };
        var img = TgaFile.TryDecode([.. TgaHeader(2, 2, 2, 24, 0), .. body])!;
        Assert.Equal(2, img.Width);
        // Top-left after flip is the blue pixel.
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, img.Bgra[..4]);
        // Bottom-left is red.
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, img.Bgra[8..12]);
    }

    [Fact]
    public void Tga_Rle32_TopDownWithAlpha()
    {
        // 3x1 RLE: run of 3 copies of semi-transparent green.
        var body = new byte[] { 0x82, 0, 255, 0, 128 };
        var img = TgaFile.TryDecode([.. TgaHeader(10, 3, 1, 32, 0x28), .. body])!;
        Assert.Equal(3, img.Width);
        for (var x = 0; x < 3; x++)
        {
            Assert.Equal(new byte[] { 0, 255, 0, 128 }, img.Bgra[(x * 4)..(x * 4 + 4)]);
        }
    }

    [Fact]
    public void Tga_ColorMapped8()
    {
        var hdr = TgaHeader(1, 2, 1, 8, 0x20, cmapType: 1, cmapLen: 2, cmapBits: 24);
        var palette = new byte[] { 10, 20, 30, 40, 50, 60 };
        var img = TgaFile.TryDecode([.. hdr, .. palette, 1, 0])!;
        Assert.Equal(new byte[] { 40, 50, 60, 255, 10, 20, 30, 255 }, img.Bgra);
    }

    [Fact]
    public void Tga_ColorMapped16_NoAttributeBits_IsOpaque()
    {
        // 15/16-bit palette entry 0x7C00 = pure red with bit 15 clear; descriptor has 0 alpha bits.
        var hdr = TgaHeader(1, 1, 1, 8, 0x20, cmapType: 1, cmapLen: 1, cmapBits: 16);
        var img = TgaFile.TryDecode([.. hdr, 0x00, 0x7C, 0])!;
        Assert.Equal(255, img.Bgra[3]);
        Assert.True(img.Bgra[2] > 240);
    }

    [Fact]
    public void Tga_RejectsGarbage()
    {
        Assert.Null(TgaFile.TryDecode(Encoding.ASCII.GetBytes("this is not a targa file at all")));
    }

    // ---------- EXR ----------

    private static byte[] ExrFixture(int compression, int w, int h, int pixelType, Func<int, int, string, float> value)
    {
        var channels = new[] { "B", "G", "R" };
        var header = new MemoryStream();
        void Attr(string name, string type, byte[] v)
        {
            header.Write(Encoding.ASCII.GetBytes(name + "\0" + type + "\0"));
            header.Write(BitConverter.GetBytes(v.Length));
            header.Write(v);
        }

        var ch = new MemoryStream();
        foreach (var c in channels)
        {
            ch.Write(Encoding.ASCII.GetBytes(c + "\0"));
            ch.Write(BitConverter.GetBytes(pixelType));
            ch.Write(new byte[4]);
            ch.Write(BitConverter.GetBytes(1));
            ch.Write(BitConverter.GetBytes(1));
        }

        ch.WriteByte(0);
        Attr("channels", "chlist", ch.ToArray());
        Attr("compression", "compression", [(byte)compression]);
        var box = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(box.AsSpan(8), w - 1);
        BinaryPrimitives.WriteInt32LittleEndian(box.AsSpan(12), h - 1);
        Attr("dataWindow", "box2i", box);
        Attr("displayWindow", "box2i", box);
        Attr("lineOrder", "lineOrder", [0]);
        header.WriteByte(0);

        var lpc = compression == 3 ? 16 : 1;
        var chunkCount = (h + lpc - 1) / lpc;
        var ss = pixelType == 1 ? 2 : 4;
        var chunks = new List<byte[]>();
        for (var c = 0; c < chunkCount; c++)
        {
            var raw = new MemoryStream();
            for (var y = c * lpc; y < Math.Min(h, (c + 1) * lpc); y++)
            {
                foreach (var name in channels)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var v = value(x, y, name);
                        var buf = new byte[ss];
                        if (pixelType == 1)
                        {
                            BinaryPrimitives.WriteInt16LittleEndian(buf, BitConverter.HalfToInt16Bits((Half)v));
                        }
                        else
                        {
                            BinaryPrimitives.WriteSingleLittleEndian(buf, v);
                        }

                        raw.Write(buf);
                    }
                }
            }

            var payload = Compress(raw.ToArray(), compression);
            var chunk = new byte[8 + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(chunk, c * lpc);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), payload.Length);
            payload.CopyTo(chunk, 8);
            chunks.Add(chunk);
        }

        var file = new MemoryStream();
        file.Write(BitConverter.GetBytes(ExrFile.Magic));
        file.Write(BitConverter.GetBytes(2));
        file.Write(header.ToArray());
        long offset = file.Length + chunkCount * 8;
        foreach (var c in chunks)
        {
            file.Write(BitConverter.GetBytes((ulong)offset));
            offset += c.Length;
        }

        foreach (var c in chunks)
        {
            file.Write(c);
        }

        return file.ToArray();
    }

    private static byte[] Compress(byte[] raw, int compression)
    {
        if (compression == 0)
        {
            return raw;
        }

        // Interleave halves, then delta predictor (inverse of the decoder).
        var t = new byte[raw.Length];
        var half = (raw.Length + 1) / 2;
        for (int i = 0, a = 0, b = half; i < raw.Length; i++)
        {
            if ((i & 1) == 0) t[a++] = raw[i];
            else t[b++] = raw[i];
        }

        var p = new byte[t.Length];
        p[0] = t[0];
        for (var i = 1; i < t.Length; i++)
        {
            p[i] = (byte)(t[i] - t[i - 1] + 128);
        }

        if (compression == 1)
        {
            // Literal runs only (valid RLE).
            var o = new MemoryStream();
            for (var i = 0; i < p.Length; i += 127)
            {
                var n = Math.Min(127, p.Length - i);
                o.WriteByte(unchecked((byte)(sbyte)-n));
                o.Write(p, i, n);
            }

            return o.ToArray();
        }

        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(p);
        }

        return ms.ToArray();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    public void Exr_DecodesScanlineCompressions(int compression, int pixelType)
    {
        // 5x20 spans two ZIP chunks (16 lines each).
        static float V(int x, int y, string c) => c switch { "R" => x * 0.5f, "G" => y * 0.25f, _ => 2f };
        var data = ExrFixture(compression, 5, 20, pixelType, V);
        var frame = ExrFile.TryDecode(data)!;
        Assert.NotNull(frame);
        Assert.Equal((5, 20), (frame.Width, frame.Height));
        foreach (var (x, y) in new[] { (0, 0), (4, 0), (3, 17), (4, 19) })
        {
            var i = (y * 5 + x) * 4;
            Assert.Equal(V(x, y, "R"), frame.Rgba[i], 2);
            Assert.Equal(V(x, y, "G"), frame.Rgba[i + 1], 2);
            Assert.Equal(2f, frame.Rgba[i + 2], 2);
            Assert.Equal(1f, frame.Rgba[i + 3]);
        }
    }

    [Fact]
    public void Exr_UnsupportedCompressionReturnsNullNotThrow()
    {
        var data = ExrFixture(0, 2, 2, 1, (_, _, _) => 1f);
        // Patch compression byte NONE -> PIZ (4).
        var idx = data.AsSpan().IndexOf("compression\0compression\0"u8) + "compression\0compression\0"u8.Length + 4;
        data[idx] = 4;
        Assert.Null(ExrFile.TryDecode(data));
        Assert.Equal((2, 2), ExrFile.TryReadSize(data));
    }

    [Fact]
    public void Exr_ProbesAsPresentableHdr_OnlyWhenDecodable()
    {
        var data = ExrFixture(3, 8, 4, 1, (_, _, _) => 2f);
        var probe = HdrFile.Probe(data);
        Assert.Equal(HdrKind.HdrExr, probe.Kind);
        Assert.True(probe.IsHdr);
        Assert.True(probe.CanPresentHdr);
        Assert.Equal(HdrTransfer.Scrgb, probe.Transfer);

        // Header-only head (what Probe(Stream) may see) still probes.
        var headLen = data.AsSpan().IndexOf("lineOrder\0lineOrder\0"u8) + 32;
        Assert.Equal(HdrKind.HdrExr, HdrFile.Probe(data.AsSpan(0, Math.Min(headLen, data.Length))).Kind);

        var idx = data.AsSpan().IndexOf("compression\0compression\0"u8) + "compression\0compression\0"u8.Length + 4;
        data[idx] = 4; // PIZ: catalog-thumb only, no HDR present
        Assert.Equal(HdrKind.None, HdrFile.Probe(data).Kind);
    }

    [Fact]
    public void Exr_DecodeScrgb_KeepsLinearValuesAndSanitizesNonFinite()
    {
        var data = ExrFixture(0, 4, 2, 2, (x, y, c) => (x, y, c) switch
        {
            (0, 0, "R") => float.NaN,
            (1, 0, "G") => float.PositiveInfinity,
            (2, 0, "B") => -0.25f,
            (3, 1, _) => 12.5f, // 1000 nits white
            _ => 1f,
        });
        var f = ExrFile.TryDecodeScrgb(data)!;
        Assert.Equal((4, 2), (f.Width, f.Height));
        Assert.All(f.ScrgbRgba, v => Assert.True(float.IsFinite(v)));
        Assert.Equal(0f, f.ScrgbRgba[0]);
        Assert.Equal(0f, f.ScrgbRgba[(1 * 4) + 1]);
        Assert.Equal(-0.25f, f.ScrgbRgba[(2 * 4) + 2]);
        Assert.Equal(12.5f, f.MaxScrgb, 3);
        Assert.Equal(1000f, f.MaxNits, 0);
        Assert.True(f.AvgNits > 80f && f.AvgNits < 1000f);
    }

    [Fact]
    public void Radiance_FrameFromScrgb_Stats()
    {
        float[] px = [1f, 1f, 1f, 1f, 2f, 2f, 2f, 1f];
        var f = RadianceFile.FrameFromScrgb(px, 2, 1);
        Assert.Equal(160f, f.MaxNits, 1);
        Assert.Equal(80f, f.MinNits, 1);
        Assert.Equal(120f, f.AvgNits, 1);
        Assert.Equal(2f, f.MaxScrgb);
    }

    [Fact]
    public void Exr_ThroughStillDecode_IsToneMappedAndScaled()
    {
        var data = ExrFixture(3, 40, 20, 1, (x, _, _) => x < 20 ? 0.18f : 12f);
        var r = StillDecode.TryDecode(data, ".exr", 10)!;
        Assert.True(r.ToneMapped);
        Assert.Equal((10, 5), (r.Width, r.Height));
        var right = r.Bgra![(9 * 4)..(9 * 4 + 3)];
        Assert.True(right.All(v => v > 200), string.Join(",", right));
    }

    // ---------- Radiance ----------

    [Fact]
    public void Radiance_ThroughStillDecode_IsNotBlank()
    {
        var head = Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 2 +X 2\n");
        // RGBE (128,128,128,129) = 1.0; (128,64,32,131) = (4,2,1).
        var px = new byte[] { 128, 128, 128, 129, 128, 64, 32, 131, 128, 128, 128, 129, 128, 64, 32, 131 };
        var r = StillDecode.TryDecode([.. head, .. px], ".hdr", 512)!;
        Assert.NotNull(r.Bgra);
        Assert.Equal((2, 2), (r.Width, r.Height));
        Assert.True(r.Bgra!.Take(3).Max() > 50, string.Join(",", r.Bgra!));
        Assert.True(r.Bgra![4] < r.Bgra![6], string.Join(",", r.Bgra!));
    }

    // ---------- Clip Studio ----------

    [Fact]
    public void Clip_ExtractsCanvasPreviewBlob()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "palace-clipfixture-" + Guid.NewGuid().ToString("N") + ".db");
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 1, 2, 3, 4 };
        try
        {
            using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString()))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "CREATE TABLE CanvasPreview(_PW_ID INTEGER PRIMARY KEY, ImageData BLOB); INSERT INTO CanvasPreview(ImageData) VALUES ($d);";
                cmd.Parameters.AddWithValue("$d", png);
                cmd.ExecuteNonQuery();
            }

            var file = Encoding.ASCII.GetBytes("CSFCHUNK").Concat(new byte[200]).Concat(File.ReadAllBytes(dbPath)).ToArray();
            Assert.Equal(png, ClipFile.TryExtractPreview(file));
            Assert.Equal(png, StillDecode.TryDecode(file, ".clip", 512)!.EncodedImage);
            Assert.Null(ClipFile.TryExtractPreview(Encoding.ASCII.GetBytes("CSFCHUNK no database here")));
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    // ---------- sniffing / placeholder / registration ----------

    [Theory]
    [InlineData(".bmp", new byte[] { 0, 0, 0, 2, 0, 0, 0, 32 }, true)]
    [InlineData(".bmp", new byte[] { (byte)'B', (byte)'M', 0, 0 }, false)]
    [InlineData(".tif", new byte[] { 0, 0, 0, 5, 0, 0, 0, 46 }, true)]
    [InlineData(".tif", new byte[] { (byte)'I', (byte)'I', 42, 0 }, false)]
    [InlineData(".ico", new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8' }, false)]
    [InlineData(".ico", new byte[] { 0, 0, 1, 0 }, false)]
    [InlineData(".tga", new byte[] { 0, 0, 2, 0 }, false)]
    public void IsNotAnImage_SniffsHeaders(string ext, byte[] head, bool expected) =>
        Assert.Equal(expected, StillDecode.IsNotAnImage(head, ext));

    [Fact]
    public void Placeholder_IsOpaqueSquare()
    {
        var p = StillDecode.Placeholder(64);
        Assert.Equal((64, 64), (p.Width, p.Height));
        Assert.All(Enumerable.Range(0, 64 * 64), i => Assert.Equal(255, p.Bgra![i * 4 + 3]));
    }

    [Theory]
    [InlineData(".exr")]
    [InlineData(".tga")]
    [InlineData(".ico")]
    [InlineData(".clip")]
    public void NewExtensionsAreCatalogImages(string ext)
    {
        Assert.True(PathSafe.IsCatalogExt(ext));
        Assert.Equal(Models.AssetKind.Image, PathSafe.KindFromExt(ext));
    }

    [Fact]
    public void BoxScaleBgra_AveragesWithAlphaWeight()
    {
        var src = new byte[] { 0, 0, 200, 255, 0, 0, 0, 0 };
        var dst = StillDecode.BoxScaleBgra(src, 2, 1, 1, 1);
        Assert.Equal(200, dst[2]);
        Assert.Equal(127, dst[3]);
    }
}
