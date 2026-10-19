using System.Buffers.Binary;

namespace Palace.Helpers;

/// <summary>
/// Substance 3D Painter shelf metadata: a Qt <c>QDataStream</c> map
/// (big-endian entry count, then UTF-16BE keys) that Painter writes as a
/// resource sidecar. Shelves exported from Painter sometimes carry it under
/// the texture's own name (<c>.bmp</c>, <c>.tif</c> …). Some embed the
/// resource preview (WebP / PNG / JPEG) under <c>previews</c>; that preview
/// is the only picture such a file has.
/// </summary>
public static class SubstanceShelfFile
{
    private const int MaxEntries = 4096;
    private const int MaxKeyBytes = 1024;

    /// <summary>Largest file we read whole looking for an embedded preview.</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>
    /// True when <paramref name="head"/> starts like a shelf metadata map:
    /// a small entry count, then a length-prefixed UTF-16BE key made of
    /// identifier characters. Needs the first 16 bytes or more.
    /// </summary>
    public static bool IsShelfMetadata(ReadOnlySpan<byte> head)
    {
        if (head.Length < 16)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(head);
        var keyBytes = BinaryPrimitives.ReadUInt32BigEndian(head[4..]);
        if (count is 0 or > MaxEntries || keyBytes < 4 || keyBytes > MaxKeyBytes || keyBytes % 2 != 0)
        {
            return false;
        }

        var visible = Math.Min((int)keyBytes, head.Length - 8) & ~1;
        for (var i = 8; i < 8 + visible; i += 2)
        {
            var c = head[i + 1];
            if (head[i] != 0 || !(char.IsAsciiLetterOrDigit((char)c) || c is (byte)'_' or (byte)'.' or (byte)'-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The embedded preview (WebP, PNG or JPEG bytes), or null when the
    /// metadata has none. Searches after the <c>__DEFAULT_PREVIEW__</c> key
    /// first, then anywhere in the file.
    /// </summary>
    public static byte[]? TryExtractPreview(byte[] data)
    {
        if (!IsShelfMetadata(data))
        {
            return null;
        }

        var key = DefaultPreviewKey();
        var at = data.AsSpan().IndexOf(key);
        return (at >= 0 ? FindImage(data, at + key.Length) : null) ?? FindImage(data, 8);
    }

    private static byte[] DefaultPreviewKey()
    {
        const string name = "__DEFAULT_PREVIEW__";
        var bytes = new byte[name.Length * 2];
        for (var i = 0; i < name.Length; i++)
        {
            bytes[i * 2 + 1] = (byte)name[i];
        }

        return bytes;
    }

    private static byte[]? FindImage(byte[] data, int start)
    {
        for (var i = Math.Max(0, start); i + 12 <= data.Length; i++)
        {
            var span = data.AsSpan(i);
            if (span.StartsWith("RIFF"u8) && span.Slice(8, 4).SequenceEqual("WEBP"u8))
            {
                var size = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
                if (size >= 12 && size + 8L <= span.Length)
                {
                    return span[..(int)(size + 8)].ToArray();
                }
            }
            else if (span.StartsWith(PngSignature))
            {
                if (PngLength(span) is { } length)
                {
                    return span[..length].ToArray();
                }
            }
            else if (span[0] == 0xFF && span[1] == 0xD8 && span[2] == 0xFF)
            {
                var end = span.LastIndexOf((ReadOnlySpan<byte>)[0xFF, 0xD9]);
                if (end > 2)
                {
                    return span[..(end + 2)].ToArray();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Length of the PNG starting at <paramref name="png"/>, through its zero-length
    /// <c>IEND</c> chunk. Walks the length-prefixed chunks, so "IEND" inside a text chunk
    /// does not end the image. Null when the chunks run past the data.
    /// </summary>
    internal static int? PngLength(ReadOnlySpan<byte> png)
    {
        var pos = (long)PngSignature.Length;
        while (pos + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(png[(int)pos..]);
            var isEnd = png.Slice((int)pos + 4, 4).SequenceEqual("IEND"u8);
            var next = pos + 12 + length;
            if (next > png.Length)
            {
                return null;
            }

            if (isEnd)
            {
                return length == 0 ? (int)next : null;
            }

            pos = next;
        }

        return null;
    }

    /// <summary>
    /// True when the local file at <paramref name="path"/> has an image-like extension
    /// Painter reuses and its first bytes are shelf metadata. Reads 16 bytes; callers must
    /// not pass online-only cloud files.
    /// </summary>
    public static bool IsShelfMetadataFile(string path)
    {
        if (!ThumbCacheVersion.ShelfPreviewChanged(path))
        {
            return false;
        }

        try
        {
            Span<byte> head = stackalloc byte[16];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length && IsShelfMetadata(head);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
}
