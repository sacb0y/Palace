using System.Globalization;
using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Radiance RGBE (<c>.hdr</c>) — SKIV uses DirectXTex <c>LoadFromHDRMemory</c>.
/// Isolated C#; no Magick / OpenEXR / WIC. Values stay linear scRGB
/// (1.0 = 80 nits), matching SKIV’s float present.
/// </summary>
public sealed class RadianceFrame
{
    public required float[] ScrgbRgba { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required float MaxNits { get; init; }
    public required float AvgNits { get; init; }
    public required float MinNits { get; init; }
    public required float MaxScrgb { get; init; }
}

public static class RadianceFile
{
    public static bool IsRadiance(ReadOnlySpan<byte> data)
    {
        if (data.Length < 10)
        {
            return false;
        }

        var head = Encoding.ASCII.GetString(data[..Math.Min(data.Length, 128)]);
        return head.StartsWith("#?RADIANCE", StringComparison.Ordinal)
            || head.StartsWith("#?RGBE", StringComparison.Ordinal)
            || head.Contains("FORMAT=32-bit_rle_rgbe", StringComparison.OrdinalIgnoreCase);
    }

    public static (int Width, int Height)? TryReadSize(Stream stream)
    {
        if (!TryReadHeader(stream, out var width, out var height))
        {
            return null;
        }

        return width > 0 && height > 0 ? (width, height) : null;
    }

    public static RadianceFrame? TryDecode(Stream stream)
    {
        if (!TryReadHeader(stream, out var width, out var height)
            || width <= 0 || height <= 0
            || width > 16384 || height > 16384)
        {
            return null;
        }

        var pixels = new byte[checked(width * height * 4)];
        try
        {
            for (var y = 0; y < height; y++)
            {
                if (!ReadScanline(stream, pixels.AsSpan(y * width * 4, width * 4), width))
                {
                    return null;
                }
            }
        }
        catch
        {
            return null;
        }

        var count = width * height;
        var rgba = new float[count * 4];
        var maxScrgb = 0f;
        var maxY = 0f;
        var minY = float.MaxValue;
        var sumY = 0.0;
        for (var i = 0; i < count; i++)
        {
            RgbeToScrgb(pixels, i * 4, out var r, out var g, out var b);
            var o = i * 4;
            rgba[o] = r;
            rgba[o + 1] = g;
            rgba[o + 2] = b;
            rgba[o + 3] = 1f;
            maxScrgb = Math.Max(maxScrgb, Math.Max(r, Math.Max(g, b)));
            var nits = GalleryPresent.LuminanceY(
                r * GalleryPresent.ScrgbNits,
                g * GalleryPresent.ScrgbNits,
                b * GalleryPresent.ScrgbNits,
                false);
            maxY = Math.Max(maxY, nits);
            minY = Math.Min(minY, nits);
            sumY += nits;
        }

        if (maxY <= 0)
        {
            minY = 0;
        }
        else if (minY == float.MaxValue)
        {
            minY = 0;
        }

        return new RadianceFrame
        {
            ScrgbRgba = rgba,
            Width = width,
            Height = height,
            MaxNits = maxY,
            AvgNits = (float)(sumY / count),
            MinNits = minY,
            MaxScrgb = maxScrgb
        };
    }

    public static void RgbeToScrgb(ReadOnlySpan<byte> rgbe, int offset, out float r, out float g, out float b)
    {
        if (offset < 0 || offset + 4 > rgbe.Length || rgbe[offset + 3] == 0)
        {
            r = g = b = 0;
            return;
        }

        var scale = MathF.Pow(2f, rgbe[offset + 3] - (128 + 8));
        r = rgbe[offset] * scale;
        g = rgbe[offset + 1] * scale;
        b = rgbe[offset + 2] * scale;
    }

    private static bool TryReadHeader(Stream stream, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!stream.CanRead)
        {
            return false;
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        string? line;
        var sawFormat = false;
        while ((line = ReadLine(stream)) is not null)
        {
            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("FORMAT=", StringComparison.OrdinalIgnoreCase)
                && line.Contains("32-bit_rle_rgbe", StringComparison.OrdinalIgnoreCase))
            {
                sawFormat = true;
            }
        }

        while ((line = ReadLine(stream)) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseResolution(line, out width, out height))
            {
                _ = sawFormat;
                return width > 0 && height > 0;
            }

            return false;
        }

        return false;
    }

    public static bool TryParseResolution(string line, out int width, out int height)
    {
        width = 0;
        height = 0;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var first)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var second))
        {
            return false;
        }

        var a = parts[0];
        var b = parts[2];
        if ((a is "-Y" or "+Y") && (b is "+X" or "-X"))
        {
            height = first;
            width = second;
            return true;
        }

        if ((a is "-X" or "+X") && (b is "+Y" or "-Y"))
        {
            width = first;
            height = second;
            return true;
        }

        return false;
    }

    private static bool ReadScanline(Stream stream, Span<byte> dest, int width)
    {
        if (dest.Length < width * 4)
        {
            return false;
        }

        if (width < 8 || width > 0x7FFF)
        {
            return ReadUncompressed(stream, dest, width);
        }

        var peek = new byte[4];
        if (ReadExact(stream, peek) != 4)
        {
            return false;
        }

        var scanWidth = (peek[2] << 8) | peek[3];
        if (peek[0] != 2 || peek[1] != 2 || scanWidth != width)
        {
            dest[0] = peek[0];
            dest[1] = peek[1];
            dest[2] = peek[2];
            dest[3] = peek[3];
            return ReadUncompressed(stream, dest[4..], width - 1);
        }

        var channel = new byte[width];
        for (var c = 0; c < 4; c++)
        {
            var i = 0;
            while (i < width)
            {
                var code = stream.ReadByte();
                if (code < 0)
                {
                    return false;
                }

                if (code > 128)
                {
                    var run = code - 128;
                    var value = stream.ReadByte();
                    if (value < 0 || i + run > width)
                    {
                        return false;
                    }

                    while (run-- > 0)
                    {
                        channel[i++] = (byte)value;
                    }
                }
                else
                {
                    var dump = code;
                    if (i + dump > width || ReadExact(stream, channel.AsSpan(i, dump)) != dump)
                    {
                        return false;
                    }

                    i += dump;
                }
            }

            for (var x = 0; x < width; x++)
            {
                dest[(x * 4) + c] = channel[x];
            }
        }

        return true;
    }

    private static bool ReadUncompressed(Stream stream, Span<byte> dest, int pixels)
    {
        var need = pixels * 4;
        if (dest.Length < need)
        {
            return false;
        }

        return ReadExact(stream, dest[..need]) == need;
    }

    private static int ReadExact(Stream stream, Span<byte> dest)
    {
        var n = 0;
        while (n < dest.Length)
        {
            var read = stream.Read(dest[n..]);
            if (read <= 0)
            {
                return n;
            }

            n += read;
        }

        return n;
    }

    private static string? ReadLine(Stream stream)
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0)
            {
                return buffer.Length == 0 ? null : buffer.ToString();
            }

            if (b == '\n')
            {
                return buffer.ToString();
            }

            if (b != '\r')
            {
                buffer.Append((char)b);
            }
        }
    }
}
