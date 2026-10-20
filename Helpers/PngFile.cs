using System.IO.Compression;
using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Header + raw-encoded pixel decode for local PNG stills. Used so HDR
/// (cICP PQ/HLG) present never depends on WinRT <c>BitmapDecoder</c> /
/// <c>BitmapImage</c>, which color-manage toward 8-bit sRGB and drop the
/// scRGB path. Inflates IDAT with zlib; no Magick / stb.
/// </summary>
public static class PngFile
{
    public const int MaxSide = 16384;
    public const long MaxDecodedBytes = 256L * 1024 * 1024;

    public readonly record struct EncodedFrame(
        byte[] Data,
        HdrPackedFormat Format,
        int Width,
        int Height,
        int BitDepth,
        int ColorType);

    public static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 8
        && data[0] == 0x89
        && data[1] == (byte)'P'
        && data[2] == (byte)'N'
        && data[3] == (byte)'G'
        && data[4] == 0x0D
        && data[5] == 0x0A
        && data[6] == 0x1A
        && data[7] == 0x0A;

    /// <summary>
    /// IHDR bit depth / colour type from the first 256 KiB (same window as
    /// <see cref="HdrFile"/>). Null when not a PNG or IHDR is missing.
    /// </summary>
    public static (int BitDepth, int ColorType, int Width, int Height, bool Interlaced)? TryReadIhdr(
        ReadOnlySpan<byte> data)
    {
        if (!IsPng(data) || !TryFindChunk(data, "IHDR", out var payload) || payload.Length < 13)
        {
            return null;
        }

        var width = ReadBe32(payload, 0);
        var height = ReadBe32(payload, 4);
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide)
        {
            return null;
        }

        return (payload[8], payload[9], width, height, payload[12] != 0);
    }

    /// <summary>
    /// Decode non-interlaced RGB / RGBA (8 or 16 bpc) to packed
    /// <see cref="HdrPackedFormat.Rgba8"/> / <see cref="HdrPackedFormat.Rgba16"/>.
    /// Samples stay encoded (PQ/HLG/sRGB codes) — callers run
    /// <see cref="GalleryPresent.PngEncodedToScrgb"/>.
    /// </summary>
    public static EncodedFrame? TryDecodeEncodedRgba(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !CloudFile.Exists(path)
            || CloudFile.IsOnlineOnly(path))
        {
            return null;
        }

        try
        {
            return TryDecodeEncodedRgba(File.ReadAllBytes(path));
        }
        catch
        {
            return null;
        }
    }

    public static EncodedFrame? TryDecodeEncodedRgba(ReadOnlySpan<byte> data)
    {
        var ihdr = TryReadIhdr(data);
        if (ihdr is null || ihdr.Value.Interlaced)
        {
            return null;
        }

        var (bitDepth, colorType, width, height, _) = ihdr.Value;
        if (bitDepth is not (8 or 16) || colorType is not (2 or 6))
        {
            return null;
        }

        var channels = colorType == 6 ? 4 : 3;
        var bytesPerSample = bitDepth / 8;
        var rawStride = width * channels * bytesPerSample;
        var filterStride = rawStride + 1;
        long rawBytes = (long)filterStride * height;
        if (rawBytes <= 0 || rawBytes > MaxDecodedBytes)
        {
            return null;
        }

        if (!TryInflateIdat(data, (int)rawBytes, out var inflated) || inflated.Length < rawBytes)
        {
            return null;
        }

        var raw = Unfilter(inflated, width, height, channels, bytesPerSample);
        if (raw is null)
        {
            return null;
        }

        if (bitDepth == 16)
        {
            var rgba = PackRgba16(raw, width, height, channels);
            return new EncodedFrame(rgba, HdrPackedFormat.Rgba16, width, height, bitDepth, colorType);
        }

        var rgba8 = PackRgba8(raw, width, height, channels);
        return new EncodedFrame(rgba8, HdrPackedFormat.Rgba8, width, height, bitDepth, colorType);
    }

    /// <summary>
    /// True when Palace should decode via <see cref="TryDecodeEncodedRgba"/>
    /// instead of WinRT WIC (PQ/HLG HDR PNG present).
    /// </summary>
    public static bool ShouldDecodeManaged(HdrProbe probe) =>
        probe.Kind is HdrKind.HdrPng or HdrKind.WideGamutPng
        && (probe.IsPq || probe.IsHlg);

    private static bool TryFindChunk(ReadOnlySpan<byte> data, string type, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        var i = 8;
        while (i + 12 <= data.Length)
        {
            var len = ReadBe32(data, i);
            if (len < 0 || i + 12 + len > data.Length)
            {
                return false;
            }

            var name = Encoding.ASCII.GetString(data.Slice(i + 4, 4));
            if (name == type)
            {
                payload = data.Slice(i + 8, len);
                return true;
            }

            if (name is "IDAT" or "IEND")
            {
                return false;
            }

            i += 12 + len;
        }

        return false;
    }

    private static bool TryInflateIdat(ReadOnlySpan<byte> data, int expected, out byte[] inflated)
    {
        inflated = [];
        using var zlib = new MemoryStream();
        var i = 8;
        while (i + 12 <= data.Length)
        {
            var len = ReadBe32(data, i);
            if (len < 0 || i + 12 + len > data.Length)
            {
                return false;
            }

            var name = Encoding.ASCII.GetString(data.Slice(i + 4, 4));
            if (name == "IDAT")
            {
                zlib.Write(data.Slice(i + 8, len));
            }
            else if (name == "IEND")
            {
                break;
            }

            i += 12 + len;
        }

        if (zlib.Length < 2)
        {
            return false;
        }

        try
        {
            zlib.Position = 0;
            using var inflater = new ZLibStream(zlib, CompressionMode.Decompress, leaveOpen: true);
            using var outMs = new MemoryStream(expected);
            inflater.CopyTo(outMs);
            inflated = outMs.ToArray();
            return inflated.Length >= expected;
        }
        catch
        {
            return false;
        }
    }

    private static byte[]? Unfilter(
        byte[] filtered,
        int width,
        int height,
        int channels,
        int bytesPerSample)
    {
        var bpp = channels * bytesPerSample;
        var stride = width * bpp;
        var rowBytes = stride + 1;
        if (filtered.Length < (long)rowBytes * height)
        {
            return null;
        }

        var raw = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var src = y * rowBytes;
            var dst = y * stride;
            var filter = filtered[src];
            var row = filtered.AsSpan(src + 1, stride);
            var prev = y > 0 ? raw.AsSpan((y - 1) * stride, stride) : ReadOnlySpan<byte>.Empty;
            var dest = raw.AsSpan(dst, stride);
            switch (filter)
            {
                case 0:
                    row.CopyTo(dest);
                    break;
                case 1:
                    for (var x = 0; x < stride; x++)
                    {
                        var left = x >= bpp ? dest[x - bpp] : (byte)0;
                        dest[x] = (byte)(row[x] + left);
                    }

                    break;
                case 2:
                    for (var x = 0; x < stride; x++)
                    {
                        var up = prev.IsEmpty ? (byte)0 : prev[x];
                        dest[x] = (byte)(row[x] + up);
                    }

                    break;
                case 3:
                    for (var x = 0; x < stride; x++)
                    {
                        var left = x >= bpp ? dest[x - bpp] : (byte)0;
                        var up = prev.IsEmpty ? (byte)0 : prev[x];
                        dest[x] = (byte)(row[x] + ((left + up) / 2));
                    }

                    break;
                case 4:
                    for (var x = 0; x < stride; x++)
                    {
                        var left = x >= bpp ? dest[x - bpp] : (byte)0;
                        var up = prev.IsEmpty ? (byte)0 : prev[x];
                        var upLeft = prev.IsEmpty || x < bpp ? (byte)0 : prev[x - bpp];
                        dest[x] = (byte)(row[x] + Paeth(left, up, upLeft));
                    }

                    break;
                default:
                    return null;
            }
        }

        return raw;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }

    private static byte[] PackRgba8(byte[] raw, int width, int height, int channels)
    {
        var count = width * height;
        var dest = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            var s = i * channels;
            var d = i * 4;
            dest[d] = raw[s];
            dest[d + 1] = raw[s + 1];
            dest[d + 2] = raw[s + 2];
            dest[d + 3] = channels == 4 ? raw[s + 3] : (byte)255;
        }

        return dest;
    }

    private static byte[] PackRgba16(byte[] raw, int width, int height, int channels)
    {
        var count = width * height;
        var dest = new byte[count * 8];
        for (var i = 0; i < count; i++)
        {
            var s = i * channels * 2;
            var d = i * 8;
            // PNG stores 16-bit samples big-endian; WIC Rgba16 is little-endian.
            dest[d] = raw[s + 1];
            dest[d + 1] = raw[s];
            dest[d + 2] = raw[s + 3];
            dest[d + 3] = raw[s + 2];
            dest[d + 4] = raw[s + 5];
            dest[d + 5] = raw[s + 4];
            if (channels == 4)
            {
                dest[d + 6] = raw[s + 7];
                dest[d + 7] = raw[s + 6];
            }
            else
            {
                dest[d + 6] = 0xFF;
                dest[d + 7] = 0xFF;
            }
        }

        return dest;
    }

    private static int ReadBe32(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
