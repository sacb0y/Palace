namespace Palace.Helpers;

/// <summary>
/// Truevision TGA decoder (no WIC codec ships for it). Types 1/2/3
/// (color-mapped, truecolor, grayscale) and their RLE forms 9/10/11;
/// 8/15/16/24/32 bpp. Output is top-down BGRA8, straight alpha.
/// </summary>
public static class TgaFile
{
    public sealed record Image(byte[] Bgra, int Width, int Height);

    public static (int Width, int Height)? TryReadSize(ReadOnlySpan<byte> data)
    {
        if (!TryHeader(data, out var h))
        {
            return null;
        }

        return (h.Width, h.Height);
    }

    public static Image? TryDecode(ReadOnlySpan<byte> data)
    {
        if (!TryHeader(data, out var h))
        {
            return null;
        }

        try
        {
            var pos = 18 + h.IdLength;
            byte[]? palette = null;
            if (h.ColorMapType == 1)
            {
                var entryBytes = (h.ColorMapBits + 7) / 8;
                var mapLen = h.ColorMapLength * entryBytes;
                palette = new byte[h.ColorMapLength * 4];
                for (var i = 0; i < h.ColorMapLength; i++)
                {
                    ReadColor(data.Slice(pos + i * entryBytes, entryBytes), h.ColorMapBits, palette.AsSpan(i * 4, 4), true);
                }

                pos += mapLen;
            }

            var count = h.Width * h.Height;
            var bpp = (h.PixelDepth + 7) / 8;
            var raw = new byte[count * bpp];
            if (h.ImageType >= 9)
            {
                var o = 0;
                while (o < raw.Length)
                {
                    var header = data[pos++];
                    var run = (header & 0x7F) + 1;
                    if ((header & 0x80) != 0)
                    {
                        var px = data.Slice(pos, bpp);
                        pos += bpp;
                        for (var k = 0; k < run && o < raw.Length; k++, o += bpp)
                        {
                            px.CopyTo(raw.AsSpan(o, bpp));
                        }
                    }
                    else
                    {
                        var n = Math.Min(run * bpp, raw.Length - o);
                        data.Slice(pos, n).CopyTo(raw.AsSpan(o, n));
                        pos += run * bpp;
                        o += n;
                    }
                }
            }
            else
            {
                data.Slice(pos, raw.Length).CopyTo(raw);
            }

            var baseType = h.ImageType & 0x7;
            var alphaBits = h.Descriptor & 0x0F;
            var topDown = (h.Descriptor & 0x20) != 0;
            var rightToLeft = (h.Descriptor & 0x10) != 0;
            var output = new byte[count * 4];
            Span<byte> color = stackalloc byte[4];
            for (var y = 0; y < h.Height; y++)
            {
                var dy = topDown ? y : h.Height - 1 - y;
                for (var x = 0; x < h.Width; x++)
                {
                    var dx = rightToLeft ? h.Width - 1 - x : x;
                    var src = raw.AsSpan((y * h.Width + x) * bpp, bpp);
                    var dst = output.AsSpan((dy * h.Width + dx) * 4, 4);
                    if (baseType == 1)
                    {
                        var index = bpp == 1 ? src[0] : src[0] | (src[1] << 8);
                        index -= h.ColorMapFirst;
                        if (palette is not null && index >= 0 && index < h.ColorMapLength)
                        {
                            palette.AsSpan(index * 4, 4).CopyTo(dst);
                        }
                        else
                        {
                            dst[3] = 255;
                        }
                    }
                    else if (baseType == 3)
                    {
                        dst[0] = dst[1] = dst[2] = src[0];
                        dst[3] = bpp >= 2 && alphaBits > 0 ? src[1] : (byte)255;
                    }
                    else
                    {
                        ReadColor(src, h.PixelDepth, color, alphaBits > 0);
                        color.CopyTo(dst);
                    }
                }
            }

            return new Image(output, h.Width, h.Height);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static void ReadColor(ReadOnlySpan<byte> src, int bits, Span<byte> bgra, bool useAlpha)
    {
        switch (bits)
        {
            case 15:
            case 16:
                var v = src[0] | (src[1] << 8);
                bgra[2] = (byte)(((v >> 10) & 0x1F) * 255 / 31);
                bgra[1] = (byte)(((v >> 5) & 0x1F) * 255 / 31);
                bgra[0] = (byte)((v & 0x1F) * 255 / 31);
                bgra[3] = bits == 16 && useAlpha ? ((v & 0x8000) != 0 ? (byte)255 : (byte)0) : (byte)255;
                break;
            case 24:
                bgra[0] = src[0];
                bgra[1] = src[1];
                bgra[2] = src[2];
                bgra[3] = 255;
                break;
            case 32:
                bgra[0] = src[0];
                bgra[1] = src[1];
                bgra[2] = src[2];
                bgra[3] = useAlpha ? src[3] : (byte)255;
                break;
            default:
                bgra[0] = bgra[1] = bgra[2] = src[0];
                bgra[3] = 255;
                break;
        }
    }

    private readonly record struct Header(
        int IdLength, int ColorMapType, int ImageType, int ColorMapFirst, int ColorMapLength,
        int ColorMapBits, int Width, int Height, int PixelDepth, int Descriptor);

    private static bool TryHeader(ReadOnlySpan<byte> d, out Header h)
    {
        h = default;
        if (d.Length < 18)
        {
            return false;
        }

        h = new Header(
            d[0], d[1], d[2],
            d[3] | (d[4] << 8), d[5] | (d[6] << 8), d[7],
            d[12] | (d[13] << 8), d[14] | (d[15] << 8), d[16], d[17]);
        var typeOk = h.ImageType is 1 or 2 or 3 or 9 or 10 or 11;
        var depthOk = h.PixelDepth is 8 or 15 or 16 or 24 or 32;
        var mapOk = (h.ImageType & 7) == 1
            ? h.ColorMapType == 1 && h.ColorMapBits is 15 or 16 or 24 or 32 && h.PixelDepth is 8 or 16
            : h.ColorMapType is 0 or 1;
        return typeOk && depthOk && mapOk
            && h.Width is > 0 and <= 32768 && h.Height is > 0 and <= 32768;
    }
}
