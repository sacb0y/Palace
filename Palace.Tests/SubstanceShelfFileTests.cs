using System.Buffers.Binary;
using System.Text;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Substance Painter shelf metadata saved under image names (<c>.bmp</c> / <c>.tif</c>).
/// Fixtures are synthesized QDataStream maps, not real shelf files.
/// </summary>
public class SubstanceShelfFileTests
{
    // 1x1 RGBA PNG.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static byte[] TinyWebp()
    {
        // RIFF header + VP8L chunk of a 1x1 lossless image.
        byte[] vp8l = [0x2F, 0x00, 0x00, 0x00, 0x00, 0x07, 0x10, 0x11, 0x11, 0x88, 0x88, 0xFE, 0x07, 0x00];
        using var ms = new MemoryStream();
        ms.Write("RIFF"u8);
        ms.Write(LittleEndian((uint)(4 + 8 + vp8l.Length)));
        ms.Write("WEBP"u8);
        ms.Write("VP8L"u8);
        ms.Write(LittleEndian((uint)vp8l.Length));
        ms.Write(vp8l);
        return ms.ToArray();
    }

    private static byte[] LittleEndian(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }

    private static void BigEndian(Stream s, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        s.Write(b);
    }

    private static void QString(Stream s, string text)
    {
        var bytes = Encoding.BigEndianUnicode.GetBytes(text);
        BigEndian(s, (uint)bytes.Length);
        s.Write(bytes);
    }

    private static void QByteArray(Stream s, byte[] bytes)
    {
        BigEndian(s, 12); // QVariant type: QByteArray
        s.WriteByte(0);   // not null
        BigEndian(s, (uint)bytes.Length);
        s.Write(bytes);
    }

    /// <summary>Like the shelf file with a preview: origin URL, a previews map, a resource version.</summary>
    private static byte[] ShelfWithPreview(byte[] preview)
    {
        using var ms = new MemoryStream();
        BigEndian(ms, 3);
        QString(ms, "SHELF_IMPORT_ORIGIN_URL");
        BigEndian(ms, 10); // QVariant type: QString
        ms.WriteByte(0);
        QString(ms, "file:///G:/Downloads/example-SubstancePainter/example.spsm");
        QString(ms, "previews");
        BigEndian(ms, 8);  // QVariant type: QVariantMap
        ms.WriteByte(0);
        BigEndian(ms, 1);
        QString(ms, "__DEFAULT_PREVIEW__");
        BigEndian(ms, (uint)preview.Length);
        ms.Write(preview);
        QString(ms, "resource_version");
        QByteArray(ms, Encoding.ASCII.GetBytes(".0123456789abcdef.image"));
        return ms.ToArray();
    }

    /// <summary>Like the 152-byte shelf file with no picture: only version keys.</summary>
    private static byte[] ShelfWithoutPreview()
    {
        using var ms = new MemoryStream();
        BigEndian(ms, 2);
        QString(ms, "resource_version");
        QByteArray(ms, Encoding.ASCII.GetBytes(".e95c8131576a951c9aac1cc3b30cfc6cfc9c7981.image"));
        QString(ms, "local_unversioned_keys");
        BigEndian(ms, 11); // QVariant type: QStringList
        ms.WriteByte(0);
        BigEndian(ms, 0);
        return ms.ToArray();
    }

    [Fact]
    public void Detects_ShelfMetadata_FromHeader()
    {
        Assert.True(SubstanceShelfFile.IsShelfMetadata(ShelfWithPreview(TinyPng).AsSpan(0, 64)));
        Assert.True(SubstanceShelfFile.IsShelfMetadata(ShelfWithoutPreview().AsSpan(0, 64)));
        // Still not the image its name claims, so it never reaches the BMP / TIFF codecs.
        Assert.True(StillDecode.IsNotAnImage(ShelfWithoutPreview().AsSpan(0, 64), ".bmp"));
        Assert.True(StillDecode.IsNotAnImage(ShelfWithPreview(TinyPng).AsSpan(0, 64), ".tif"));
    }

    [Theory]
    [InlineData(new byte[] { 0x42, 0x4D, 0x98, 0, 0, 0, 0, 0, 0, 0, 0x36, 0, 0, 0, 0x28, 0, 0, 0 })] // BMP
    [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]             // TIFF
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0x20, 0, 0x72, 0, 0x65, 0, 0x73, 0, 0x6F })]       // zero entries
    [InlineData(new byte[] { 0, 0, 0, 2, 0, 0, 0, 0x21, 0, 0x72, 0, 0x65, 0, 0x73, 0, 0x6F })]       // odd key length
    [InlineData(new byte[] { 0, 0, 0, 2, 0, 0, 0, 0x20, 0x72, 0, 0x65, 0, 0x73, 0, 0x6F, 0 })]       // little-endian key
    [InlineData(new byte[] { 0, 0, 0, 2, 0, 0, 0, 0x20, 0, 0x72 })]                                  // too short
    public void Rejects_RealImagesAndNearMisses(byte[] head)
    {
        Assert.False(SubstanceShelfFile.IsShelfMetadata(head));
        Assert.Null(SubstanceShelfFile.TryExtractPreview(head));
    }

    [Fact]
    public void Extracts_EmbeddedWebp()
    {
        var webp = TinyWebp();
        Assert.Equal(webp, SubstanceShelfFile.TryExtractPreview(ShelfWithPreview(webp)));
    }

    [Fact]
    public void Extracts_EmbeddedPng()
    {
        Assert.Equal(TinyPng, SubstanceShelfFile.TryExtractPreview(ShelfWithPreview(TinyPng)));
    }

    [Fact]
    public void Extracts_EmbeddedJpeg()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 2, 3, 0xFF, 0xD9];
        Assert.Equal(jpeg, SubstanceShelfFile.TryExtractPreview(ShelfWithPreview(jpeg)));
    }

    [Fact]
    public void NoPreview_ReturnsNull()
    {
        Assert.Null(SubstanceShelfFile.TryExtractPreview(ShelfWithoutPreview()));
    }

    [Fact]
    public void TruncatedPreview_ReturnsNull()
    {
        var file = ShelfWithPreview(TinyWebp());
        var cut = file.AsSpan().IndexOf("RIFF"u8) + 10;
        Assert.Null(SubstanceShelfFile.TryExtractPreview(file[..cut]));
    }

    [Theory]
    [InlineData(@"D:\tex\alpha 02.bmp", true)]
    [InlineData(@"D:\tex\lips02_upper_tr1.tif", true)]
    [InlineData(@"D:\tex\a.TIFF", true)]
    [InlineData(@"D:\tex\a.png", false)]
    [InlineData(@"D:\tex\a.jpg", false)]
    [InlineData(@"D:\tex\a.psd", false)]
    public void CacheGeneration3_OnlyBmpAndTiffGoStale(string path, bool stale)
    {
        var gen = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(stale, ThumbCacheVersion.IsStale(gen.AddDays(-1), gen, ThumbCacheVersion.ShelfPreviewChanged(path)));
        Assert.False(ThumbCacheVersion.IsStale(gen.AddMinutes(1), gen, ThumbCacheVersion.ShelfPreviewChanged(path)));
    }

    [Fact]
    public void CacheGenerations_HaveIndependentMarkers()
    {
        var dir = Path.Combine(Path.GetTempPath(), "palace-thumbgen-" + Guid.NewGuid().ToString("N"));
        try
        {
            var v2 = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
            var v3 = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
            Assert.Equal(v2, ThumbCacheVersion.EnsureMarker(dir, ThumbCacheVersion.DecodersGeneration, () => v2));
            // Upgrading adds the v3 marker without moving v2, so v2 formats don't regenerate again.
            Assert.Equal(v3, ThumbCacheVersion.EnsureMarker(dir, ThumbCacheVersion.ShelfPreviewGeneration, () => v3));
            Assert.Equal(v2, ThumbCacheVersion.EnsureMarker(dir, ThumbCacheVersion.DecodersGeneration, () => v3));
            Assert.True(File.Exists(Path.Combine(dir, ".thumbs-v2")));
            Assert.True(File.Exists(Path.Combine(dir, ".thumbs-v3")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
