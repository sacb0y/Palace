using System.Buffers.Binary;
using Palace.Helpers;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

public sealed class PsdAndCacheTests
{
    private static byte[] U16(int v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)v); return b; }
    private static byte[] U32(int v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, (uint)v); return b; }

    /// <summary>Minimal PSD writer: header, optional palette, optional resources, empty layers, composite.</summary>
    private static byte[] Psd(int mode, int depth, int w, int h, byte[][] planes, bool rle,
        byte[]? colorMode = null, byte[]? resources = null)
    {
        var ms = new MemoryStream();
        ms.Write("8BPS"u8); ms.Write(U16(1)); ms.Write(new byte[6]);
        ms.Write(U16(planes.Length)); ms.Write(U32(h)); ms.Write(U32(w)); ms.Write(U16(depth)); ms.Write(U16(mode));
        ms.Write(U32(colorMode?.Length ?? 0)); if (colorMode is not null) ms.Write(colorMode);
        ms.Write(U32(resources?.Length ?? 0)); if (resources is not null) ms.Write(resources);
        ms.Write(U32(0)); // layer & mask
        ms.Write(U16(rle ? 1 : 0));
        var rowBytes = w * (depth / 8);
        if (!rle)
        {
            foreach (var p in planes) ms.Write(p);
        }
        else
        {
            var rows = new List<byte[]>();
            foreach (var p in planes)
                for (var y = 0; y < h; y++)
                    rows.Add(PackBits(p.AsSpan(y * rowBytes, rowBytes)));
            foreach (var r in rows) ms.Write(U16(r.Length));
            foreach (var r in rows) ms.Write(r);
        }

        return ms.ToArray();
    }

    // Simple encoder: runs of equal bytes, otherwise literals.
    private static byte[] PackBits(ReadOnlySpan<byte> src)
    {
        var o = new List<byte>();
        var i = 0;
        while (i < src.Length)
        {
            var run = 1;
            while (i + run < src.Length && src[i + run] == src[i] && run < 128) run++;
            if (run >= 2)
            {
                o.Add(unchecked((byte)(sbyte)(1 - run))); o.Add(src[i]); i += run;
            }
            else
            {
                var start = i; var n = 0;
                while (i < src.Length && n < 128 && !(i + 1 < src.Length && src[i + 1] == src[i])) { i++; n++; }
                if (n == 0) { i++; n = 1; }
                o.Add((byte)(n - 1)); for (var k = 0; k < n; k++) o.Add(src[start + k]);
            }
        }

        return o.ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Psd_Rgb8Composite(bool rle)
    {
        // 3x2: red, red, red / green, blue, white
        var r = new byte[] { 255, 255, 255, 0, 0, 255 };
        var g = new byte[] { 0, 0, 0, 255, 0, 255 };
        var b = new byte[] { 0, 0, 0, 0, 255, 255 };
        var img = PsdFile.TryDecodeComposite(Psd(3, 8, 3, 2, [r, g, b], rle))!;
        Assert.Equal((3, 2), (img.Width, img.Height));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, img.Bgra[..4]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, img.Bgra[16..20]);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, img.Bgra[20..24]);
    }

    [Fact]
    public void Psd_Gray16WithAlpha()
    {
        // Photoshop 16-bit range is 0..32768: 0x4000 = mid grey, 0x8000 = white.
        var gray = new byte[] { 0x40, 0x00, 0x80, 0x00 };
        var alpha = new byte[] { 0x80, 0x00, 0x20, 0x00 };
        var img = PsdFile.TryDecodeComposite(Psd(1, 16, 2, 1, [gray, alpha], rle: true))!;
        Assert.Equal(new byte[] { 128, 128, 128, 255, 255, 255, 255, 64 }, img.Bgra);
    }

    [Fact]
    public void Psd_CmykInvertedInk()
    {
        // Photoshop stores CMYK inverted: 255 = no ink. Pure cyan = C 0, others 255.
        var img = PsdFile.TryDecodeComposite(Psd(4, 8, 1, 1, [[0], [255], [255], [255]], rle: false))!;
        Assert.Equal(new byte[] { 255, 255, 0, 255 }, img.Bgra);
    }

    [Fact]
    public void Psd_Indexed()
    {
        var pal = new byte[768];
        pal[5] = 10; pal[256 + 5] = 20; pal[512 + 5] = 30;
        var img = PsdFile.TryDecodeComposite(Psd(2, 8, 1, 1, [[5]], rle: false, colorMode: pal))!;
        Assert.Equal(new byte[] { 30, 20, 10, 255 }, img.Bgra);
    }

    private static byte[] ThumbResource(byte[] jpeg)
    {
        var ms = new MemoryStream();
        ms.Write("8BIM"u8); ms.Write(U16(1036)); ms.Write(new byte[] { 0, 0 }); // empty pascal name (padded)
        var body = new MemoryStream();
        body.Write(U32(1)); body.Write(U32(4)); body.Write(U32(4)); body.Write(U32(12));
        body.Write(U32(48)); body.Write(U32(jpeg.Length)); body.Write(U16(24)); body.Write(U16(1));
        body.Write(jpeg);
        ms.Write(U32((int)body.Length)); ms.Write(body.ToArray());
        if (body.Length % 2 == 1) ms.WriteByte(0);
        return ms.ToArray();
    }

    [Fact]
    public void Psd_UniformCompositeFallsBackToEmbeddedThumbnail()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9 };
        var white = new byte[] { 255, 255, 255, 255 };
        var data = Psd(3, 8, 2, 2, [white, white, white], rle: false, resources: ThumbResource(jpeg));
        Assert.Equal(jpeg, PsdFile.TryExtractThumbnailJpeg(data));
        var r = StillDecode.TryDecode(data, ".psd", 512)!;
        Assert.Equal(jpeg, r.EncodedImage);
        Assert.Equal((2, 2), PsdFile.TryReadSize(data));
    }

    [Fact]
    public void Psd_RealCompositePreferredOverThumbnail()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var data = Psd(3, 8, 2, 1, [[255, 0], [0, 0], [0, 255]], rle: true, resources: ThumbResource(jpeg));
        var r = StillDecode.TryDecode(data, ".psd", 512)!;
        Assert.Null(r.EncodedImage);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, r.Bgra);
    }

    [Fact]
    public void Psd_ThumbnailFoundInTruncatedHugeFile()
    {
        // Only header + resources present (the 32 MB head read of a huge PSB).
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var full = Psd(3, 8, 64, 64, [new byte[4096], new byte[4096], new byte[4096]], rle: false, resources: ThumbResource(jpeg));
        var head = full[..(full.Length - 4096 * 3 - 6)];
        Assert.Equal(jpeg, PsdFile.TryExtractThumbnailJpeg(head));
        Assert.Null(PsdFile.TryDecodeComposite(head));
    }

    [Fact]
    public void Psd_GarbageReturnsNull()
    {
        Assert.Null(PsdFile.TryDecodeComposite("8BPS garbage"u8.ToArray()));
        Assert.Null(PsdFile.TryExtractThumbnailJpeg(new byte[10]));
    }

    [Fact]
    public void UnpackBits_RunsAndLiterals()
    {
        var dst = new byte[8];
        PsdFile.UnpackBits(new byte[] { 0xFD, 7, 2, 1, 2, 3, 0x80, 0xFF, 9 }, dst);
        Assert.Equal(new byte[] { 7, 7, 7, 7, 1, 2, 3, 9 }, dst);
    }

    // ---------- thumbnail cache generation ----------

    [Fact]
    public void CacheVersion_MarkerIsCreatedOnceAndReused()
    {
        var dir = Path.Combine(Path.GetTempPath(), "palace-thumbver-" + Guid.NewGuid().ToString("N"));
        try
        {
            var t1 = ThumbCacheVersion.EnsureMarker(dir, () => new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc));
            var t2 = ThumbCacheVersion.EnsureMarker(dir, () => new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal(t1, t2);
            Assert.True(File.Exists(Path.Combine(dir, ThumbCacheVersion.MarkerName)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(@"D:\a.png", true, true)]    // HDR PNG thumb made before v2 -> regenerate (was red)
    [InlineData(@"D:\a.png", false, false)]  // SDR PNG untouched
    [InlineData(@"D:\a.jpg", false, false)]
    [InlineData(@"D:\a.psd", false, true)]
    [InlineData(@"D:\a.jxl", false, true)]
    [InlineData(@"D:\a.exr", false, true)]
    [InlineData(@"D:\a.hdr", true, true)]
    public void CacheVersion_OnlyChangedFormatsGoStale(string path, bool isHdr, bool stale)
    {
        var gen = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(stale, ThumbCacheVersion.IsStale(gen.AddDays(-1), gen, ThumbCacheVersion.RenderingChanged(path, isHdr)));
        Assert.False(ThumbCacheVersion.IsStale(gen.AddMinutes(1), gen, true));
    }

    // ---------- ffmpeg poster ----------

    [Fact]
    public void Ffmpeg_ArgumentsAreSafeAndScaled()
    {
        var args = FfmpegPoster.BuildArguments(@"C:\v\a b;&.mkv", @"C:\t\o.jpg", 512, 3);
        Assert.Contains(@"C:\v\a b;&.mkv", args); // one argv entry, never shell-parsed
        Assert.Equal("3", args[args.ToList().IndexOf("-ss") + 1]);
        Assert.Contains(args, a => a.Contains("min(512,iw)"));
        Assert.Equal("1", args[args.ToList().IndexOf("-frames:v") + 1]);
        Assert.Equal(@"C:\t\o.jpg", args[^1]);
        Assert.False(FfmpegPoster.IsAvailable && !OperatingSystem.IsWindows());
    }
}
