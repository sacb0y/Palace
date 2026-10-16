using System.Buffers.Binary;

namespace Palace.Helpers;

/// <summary>
/// Built-in Photoshop (.psd / .psb) reader: the flattened composite image
/// (raw or PackBits RLE; RGB, Grayscale, CMYK, Indexed, Bitmap; 8/16/32-bit)
/// and, as a fallback, the embedded JPEG thumbnail resource (1036/1033).
/// No Windows codec / shell extension needed.
/// </summary>
public static class PsdFile
{
    public sealed record Image(byte[] Bgra, int Width, int Height);

    public static bool IsPsd(ReadOnlySpan<byte> head) =>
        head.Length >= 6 && head[0] == '8' && head[1] == 'B' && head[2] == 'P' && head[3] == 'S'
        && head[4] == 0 && head[5] is 1 or 2;

    /// <summary>Embedded JFIF thumbnail bytes from image resource 1036 (or legacy 1033), if any.</summary>
    public static byte[]? TryExtractThumbnailJpeg(ReadOnlySpan<byte> data)
    {
        try
        {
            if (!TryLayout(data, out var l, requireImageData: false))
            {
                return null;
            }

            var p = l.ResourcesStart;
            var end = l.ResourcesStart + l.ResourcesLength;
            while (p + 12 <= end)
            {
                if (!data.Slice(p, 4).SequenceEqual("8BIM"u8))
                {
                    return null;
                }

                var id = BinaryPrimitives.ReadUInt16BigEndian(data[(p + 4)..]);
                var nameLen = data[p + 6];
                var nameField = (1 + nameLen + 1) & ~1;
                var sizePos = p + 6 + nameField;
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[sizePos..]);
                var body = sizePos + 4;
                if (body + size > end || size < 0)
                {
                    return null;
                }

                if (id is 1036 or 1033 && size > 28)
                {
                    var format = BinaryPrimitives.ReadUInt32BigEndian(data[body..]);
                    var jpeg = data.Slice(body + 28, size - 28);
                    if (format == 1 && jpeg.Length > 2 && jpeg[0] == 0xFF && jpeg[1] == 0xD8)
                    {
                        // 1033 stores BGR; callers only use it as a last resort.
                        return jpeg.ToArray();
                    }
                }

                p = body + ((size + 1) & ~1);
            }
        }
        catch
        {
            // Malformed resources.
        }

        return null;
    }

    public static (int Width, int Height)? TryReadSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 26 || !IsPsd(data))
        {
            return null;
        }

        var h = (int)BinaryPrimitives.ReadUInt32BigEndian(data[14..]);
        var w = (int)BinaryPrimitives.ReadUInt32BigEndian(data[18..]);
        return w > 0 && h > 0 ? (w, h) : null;
    }

    /// <summary>Decode the merged composite. Returns null for unsupported or empty composites.</summary>
    public static Image? TryDecodeComposite(ReadOnlySpan<byte> data, int maxPixels = 120_000_000)
    {
        try
        {
            if (!TryLayout(data, out var l))
            {
                return null;
            }

            int w = l.Width, h = l.Height, depth = l.Depth, channels = l.Channels, mode = l.Mode;
            if ((long)w * h > maxPixels || depth is not (1 or 8 or 16 or 32))
            {
                return null;
            }

            var p = l.ImageDataStart;
            var compression = BinaryPrimitives.ReadUInt16BigEndian(data[p..]);
            p += 2;
            var rowBytes = depth == 1 ? (w + 7) / 8 : w * (depth / 8);
            var planes = new byte[channels][];
            if (compression == 0)
            {
                for (var c = 0; c < channels; c++)
                {
                    planes[c] = data.Slice(p, rowBytes * h).ToArray();
                    p += rowBytes * h;
                }
            }
            else if (compression == 1)
            {
                var countSize = l.IsPsb ? 4 : 2;
                var counts = new int[channels * h];
                for (var i = 0; i < counts.Length; i++)
                {
                    counts[i] = countSize == 2
                        ? BinaryPrimitives.ReadUInt16BigEndian(data[(p + i * 2)..])
                        : (int)BinaryPrimitives.ReadUInt32BigEndian(data[(p + i * 4)..]);
                }

                p += counts.Length * countSize;
                for (var c = 0; c < channels; c++)
                {
                    var plane = new byte[rowBytes * h];
                    for (var y = 0; y < h; y++)
                    {
                        var n = counts[c * h + y];
                        UnpackBits(data.Slice(p, n), plane.AsSpan(y * rowBytes, rowBytes));
                        p += n;
                    }

                    planes[c] = plane;
                }
            }
            else
            {
                return null; // ZIP composites are not written by Photoshop.
            }

            var palette = mode == 2 && l.ColorModeLength >= 768 ? data.Slice(l.ColorModeStart, 768).ToArray() : null;
            var bgra = new byte[w * h * 4];
            for (var i = 0; i < w * h; i++)
            {
                byte S(int c) => Sample(planes[c], i, depth, w);
                byte r, g, b, a = 255;
                switch (mode)
                {
                    case 3 when channels >= 3: // RGB
                        r = S(0); g = S(1); b = S(2);
                        if (channels >= 4) a = S(3);
                        break;
                    case 1 or 8 when channels >= 1: // Grayscale / Duotone
                        r = g = b = S(0);
                        if (channels >= 2) a = S(1);
                        break;
                    case 0: // Bitmap: 1 = black
                        r = g = b = ((planes[0][(i / w) * ((w + 7) / 8) + (i % w) / 8] >> (7 - (i % w) % 8)) & 1) == 1 ? (byte)0 : (byte)255;
                        break;
                    case 2 when palette is not null:
                        var idx = planes[0][i];
                        r = palette[idx]; g = palette[256 + idx]; b = palette[512 + idx];
                        break;
                    case 4 when channels >= 4: // CMYK stored inverted (0 = full ink)
                        var k = S(3) / 255f;
                        r = (byte)(S(0) * k); g = (byte)(S(1) * k); b = (byte)(S(2) * k);
                        if (channels >= 5) a = S(4);
                        break;
                    default:
                        return null;
                }

                bgra[i * 4] = b;
                bgra[i * 4 + 1] = g;
                bgra[i * 4 + 2] = r;
                bgra[i * 4 + 3] = a;
            }

            return new Image(bgra, w, h);
        }
        catch
        {
            return null;
        }
    }

    private static byte Sample(byte[] plane, int i, int depth, int w) => depth switch
    {
        8 => plane[i],
        // Photoshop 16-bit is 0..32768 (0x8000 = white).
        16 => (byte)Math.Min(255, (BinaryPrimitives.ReadUInt16BigEndian(plane.AsSpan(i * 2)) * 255 + 16384) / 32768),
        32 => ToSrgbByte(BinaryPrimitives.ReadSingleBigEndian(plane.AsSpan(i * 4))),
        _ => 0
    };

    // 32-bit PSD composites are linear light.
    private static byte ToSrgbByte(float v)
    {
        if (!(v > 0))
        {
            return 0;
        }

        var s = v <= 0.0031308f ? v * 12.92f : 1.055f * MathF.Pow(MathF.Min(v, 1f), 1f / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(s * 255f), 0, 255);
    }

    public static void UnpackBits(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        int s = 0, d = 0;
        while (s < src.Length && d < dst.Length)
        {
            var n = (sbyte)src[s++];
            if (n >= 0)
            {
                var len = Math.Min(n + 1, Math.Min(dst.Length - d, src.Length - s));
                src.Slice(s, len).CopyTo(dst[d..]);
                s += n + 1;
                d += len;
            }
            else if (n != -128)
            {
                if (s >= src.Length)
                {
                    break;
                }

                var len = Math.Min(1 - n, dst.Length - d);
                dst.Slice(d, len).Fill(src[s++]);
                d += len;
            }
        }
    }

    private readonly record struct Layout(
        bool IsPsb, int Channels, int Height, int Width, int Depth, int Mode,
        int ColorModeStart, int ColorModeLength, int ResourcesStart, int ResourcesLength, int ImageDataStart);

    private static bool TryLayout(ReadOnlySpan<byte> data, out Layout layout, bool requireImageData = true)
    {
        layout = default;
        if (data.Length < 26 || !IsPsd(data))
        {
            return false;
        }

        var psb = data[5] == 2;
        var channels = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
        var h = (int)BinaryPrimitives.ReadUInt32BigEndian(data[14..]);
        var w = (int)BinaryPrimitives.ReadUInt32BigEndian(data[18..]);
        var depth = BinaryPrimitives.ReadUInt16BigEndian(data[22..]);
        var mode = BinaryPrimitives.ReadUInt16BigEndian(data[24..]);
        var p = 26;
        var cmLen = (int)BinaryPrimitives.ReadUInt32BigEndian(data[p..]);
        var cmStart = p + 4;
        p = cmStart + cmLen;
        var resLen = (int)BinaryPrimitives.ReadUInt32BigEndian(data[p..]);
        var resStart = p + 4;
        p = resStart + resLen;
        if (!requireImageData)
        {
            // Thumbnail-only read of a huge PSB: header + resources are enough.
            if (channels is < 1 or > 56 || w <= 0 || h <= 0 || cmLen < 0 || resLen < 0 || p > data.Length)
            {
                return false;
            }

            layout = new Layout(psb, channels, h, w, depth, mode, cmStart, cmLen, resStart, resLen, -1);
            return true;
        }

        long lmLen = psb ? (long)BinaryPrimitives.ReadUInt64BigEndian(data[p..]) : BinaryPrimitives.ReadUInt32BigEndian(data[p..]);
        p += psb ? 8 : 4;
        var imageStart = p + lmLen;
        if (channels is < 1 or > 56 || w <= 0 || h <= 0 || imageStart + 2 > data.Length || cmLen < 0 || resLen < 0)
        {
            return false;
        }

        layout = new Layout(psb, channels, h, w, depth, mode, cmStart, cmLen, resStart, resLen, (int)imageStart);
        return true;
    }
}
