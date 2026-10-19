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

    /// <summary>
    /// Channel ('R', 'G' or 'B') that holds <see cref="MaxScrgb"/> over the native pixels,
    /// or '\0' when unknown. Same tie rule as <see cref="HdrAnalysis"/> (R, then G, then B).
    /// </summary>
    public char MaxScrgbChannel { get; init; }
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

    /// <summary>
    /// Decode to linear scRGB. With a smaller <paramref name="destWidth"/> x
    /// <paramref name="destHeight"/> the scanlines are box-filtered straight
    /// into that size (same bins as HdrPixels.BoxScaleScrgb),
    /// so a 16384x8192 panorama never holds its 2 GB native float frame.
    /// Info stats (CIE Y max / avg / min, scRGB peak) are always native.
    /// </summary>
    public static RadianceFrame? TryDecode(Stream stream, int destWidth = 0, int destHeight = 0)
    {
        if (!TryReadHeader(stream, out var width, out var height)
            || width <= 0 || height <= 0
            || width > 16384 || height > 16384)
        {
            return null;
        }

        var scale = destWidth > 0 && destHeight > 0
            && destWidth <= width && destHeight <= height
            && (destWidth < width || destHeight < height);
        var outW = scale ? destWidth : width;
        var outH = scale ? destHeight : height;

        // One RGBE scanline at a time: no full-size byte[] copy (512 MB for
        // a 16384x8192 panorama).
        var rgba = new float[checked((long)outW * outH * 4)];
        var line = new byte[checked(width * 4)];
        var stats = new ScrgbStats();
        int[]? xBin = null;
        double[]? acc = null;
        int[]? accN = null;
        if (scale)
        {
            xBin = new int[width];
            for (var dx = 0; dx < outW; dx++)
            {
                var x0 = (int)((long)dx * width / outW);
                var x1 = Math.Max(x0 + 1, (int)((long)(dx + 1) * width / outW));
                for (var sx = x0; sx < x1 && sx < width; sx++)
                {
                    xBin[sx] = dx;
                }
            }

            acc = new double[outW * 3];
            accN = new int[outW];
        }

        try
        {
            var dy = 0;
            var rowEnd = scale ? Math.Max(1, (int)((long)height / outH)) : 1;
            for (var y = 0; y < height; y++)
            {
                if (!ReadScanline(stream, line, width))
                {
                    return null;
                }

                if (!scale)
                {
                    var row = (long)y * width * 4;
                    for (var x = 0; x < width; x++)
                    {
                        RgbeToScrgb(line, x * 4, out var r, out var g, out var b);
                        stats.Add(r, g, b);
                        var o = row + (x * 4);
                        rgba[o] = r;
                        rgba[o + 1] = g;
                        rgba[o + 2] = b;
                        rgba[o + 3] = 1f;
                    }

                    continue;
                }

                for (var x = 0; x < width; x++)
                {
                    RgbeToScrgb(line, x * 4, out var r, out var g, out var b);
                    stats.Add(r, g, b);
                    var bin = xBin![x];
                    acc![bin * 3] += r;
                    acc[(bin * 3) + 1] += g;
                    acc[(bin * 3) + 2] += b;
                    accN![bin]++;
                }

                if (y + 1 < rowEnd && y + 1 < height)
                {
                    continue;
                }

                // Flush dest row dy (src rows [dy*h/outH, (dy+1)*h/outH)).
                var o0 = (long)dy * outW * 4;
                for (var dx = 0; dx < outW; dx++)
                {
                    var n = accN![dx];
                    var o = o0 + (dx * 4);
                    rgba[o] = n > 0 ? (float)(acc![dx * 3] / n) : 0f;
                    rgba[o + 1] = n > 0 ? (float)(acc![(dx * 3) + 1] / n) : 0f;
                    rgba[o + 2] = n > 0 ? (float)(acc![(dx * 3) + 2] / n) : 0f;
                    rgba[o + 3] = 1f;
                }

                Array.Clear(acc!);
                Array.Clear(accN!);
                dy++;
                if (dy >= outH)
                {
                    break;
                }

                rowEnd = Math.Max(y + 2, (int)((long)(dy + 1) * height / outH));
            }
        }
        catch
        {
            return null;
        }

        return stats.ToFrame(rgba, outW, outH);
    }

    /// <summary>
    /// Wrap a linear scRGB RGBA buffer (Radiance, EXR) with the Info stats:
    /// CIE Y max / avg / min nits and the scRGB channel peak.
    /// </summary>
    public static RadianceFrame FrameFromScrgb(float[] rgba, int width, int height)
    {
        var count = width * height;
        var stats = new ScrgbStats();
        for (var i = 0; i < count; i++)
        {
            var o = i * 4;
            stats.Add(rgba[o], rgba[o + 1], rgba[o + 2]);
        }

        return stats.ToFrame(rgba, width, height);
    }

    private sealed class ScrgbStats
    {
        private float _maxScrgb;
        private float _maxR;
        private float _maxG;
        private float _maxB;
        private float _maxY;
        private float _minY = float.MaxValue;
        private double _sumY;
        private long _count;

        public void Add(float r, float g, float b)
        {
            _maxScrgb = Math.Max(_maxScrgb, Math.Max(r, Math.Max(g, b)));
            _maxR = Math.Max(_maxR, r);
            _maxG = Math.Max(_maxG, g);
            _maxB = Math.Max(_maxB, b);
            var nits = GalleryPresent.LuminanceY(
                r * GalleryPresent.ScrgbNits,
                g * GalleryPresent.ScrgbNits,
                b * GalleryPresent.ScrgbNits,
                false);
            _maxY = Math.Max(_maxY, nits);
            _minY = Math.Min(_minY, nits);
            _sumY += nits;
            _count++;
        }

        public RadianceFrame ToFrame(float[] rgba, int width, int height) => new()
        {
            ScrgbRgba = rgba,
            Width = width,
            Height = height,
            MaxNits = _maxY,
            AvgNits = _count > 0 ? (float)(_sumY / _count) : 0f,
            MinNits = _maxY <= 0 || _minY == float.MaxValue ? 0f : _minY,
            MaxScrgb = _maxScrgb,
            MaxScrgbChannel = _maxScrgb <= 0 ? '\0'
                : _maxR >= _maxG && _maxR >= _maxB ? 'R'
                : _maxG >= _maxB ? 'G'
                : 'B'
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
