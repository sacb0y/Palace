using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Palace.Helpers;

/// <summary>
/// OpenEXR stills. <see cref="TryDecode"/> uses the bundled OpenEXR library
/// (<see cref="ExrNative"/>: every compression, tiled, multi-part) and falls
/// back to the in-house reader here when that library is unavailable. The
/// in-house reader handles single-part scanline files with NONE, RLE, ZIPS
/// or ZIP compression and HALF / FLOAT / UINT channels. Returns linear RGBA
/// floats (R/G/B/A, or Y as grey).
/// </summary>
public static class ExrFile
{
    public const int Magic = 20000630;

    public sealed record Frame(float[] Rgba, int Width, int Height, string Compression);

    private sealed record Channel(string Name, int Type, int XSampling, int YSampling);

    public static bool IsExr(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && BinaryPrimitives.ReadInt32LittleEndian(data) == Magic;

    public static (int Width, int Height)? TryReadSize(ReadOnlySpan<byte> data) =>
        TryReadHeader(data, out var h, out _)
            ? (h.Width, h.Height)
            : ExrNative.TryInfo(data) is { } info ? (info.Width, info.Height) : null;

    /// <summary>
    /// The header (a probe head is enough) names an image part we can decode:
    /// anything the bundled OpenEXR reads, else the in-house subset.
    /// </summary>
    public static bool IsDecodable(ReadOnlySpan<byte> data) =>
        IsDecodableInHouse(data) || (!HasUnknownCompression(data) && ExrNative.TryInfo(data) is not null);

    /// <summary>
    /// OpenEXR's lenient header parse skips an invalid compression value and
    /// assumes ZIP; treat such files as undecodable instead of guessing.
    /// Checks every part header (tiled and multi-part included), not just the layouts the
    /// in-house reader accepts.
    /// </summary>
    internal static bool HasUnknownCompression(ReadOnlySpan<byte> data) =>
        HeaderAttributes(data).Any(header => header.Any(a =>
            a.Name == "compression" && (a.Value.Length < 1 || CompressionName(a.Value[0]) is null)));

    /// <summary>Single-part scanline NONE / RLE / ZIPS / ZIP (the in-house reader).</summary>
    public static bool IsDecodableInHouse(ReadOnlySpan<byte> data) =>
        TryReadHeader(data, out var h, out _) && h.Compression <= 3;

    /// <summary>
    /// Decode to linear scRGB for HDR presentation: non-finite samples become
    /// 0 (negatives are kept, as scRGB allows), plus Info stats.
    /// </summary>
    public static RadianceFrame? TryDecodeScrgb(ReadOnlySpan<byte> data)
    {
        var frame = TryDecode(data);
        if (frame is null)
        {
            return null;
        }

        var rgba = frame.Rgba;
        for (var i = 0; i < rgba.Length; i++)
        {
            if (!float.IsFinite(rgba[i]))
            {
                rgba[i] = (i & 3) == 3 ? 1f : 0f;
            }
        }

        return RadianceFile.FrameFromScrgb(rgba, frame.Width, frame.Height);
    }

    public static string? CompressionName(int c) => c switch
    {
        0 => "none",
        1 => "rle",
        2 => "zips",
        3 => "zip",
        4 => "piz",
        5 => "pxr24",
        6 => "b44",
        7 => "b44a",
        8 => "dwaa",
        9 => "dwab",
        10 => "htj2k256",
        11 => "htj2k32",
        12 => "lj2k",
        13 => "zstd",
        _ => null
    };

    private sealed record Header(List<Channel> Channels, int Compression, int XMin, int YMin, int Width, int Height)
    {
        public int LinesPerChunk => Compression == 3 ? 16 : 1;
    }

    public static Frame? TryDecode(ReadOnlySpan<byte> data)
    {
        var frame = (HasUnknownCompression(data) ? null : ExrNative.TryDecode(data)) ?? TryDecodeInHouse(data);
        if (frame is not null)
        {
            ConvertToRec709(frame.Rgba, TryReadChromaticities(data));
        }

        return frame;
    }

    public static Frame? TryDecodeInHouse(ReadOnlySpan<byte> data)
    {
        if (!TryReadHeader(data, out var h, out var offset) || h.Compression > 3)
        {
            return null;
        }

        try
        {
            var chunks = (h.Height + h.LinesPerChunk - 1) / h.LinesPerChunk;
            var rgba = new float[checked(h.Width * h.Height * 4)];
            var idxR = h.Channels.FindIndex(c => c.Name == "R");
            var idxG = h.Channels.FindIndex(c => c.Name == "G");
            var idxB = h.Channels.FindIndex(c => c.Name == "B");
            var idxA = h.Channels.FindIndex(c => c.Name == "A");
            var idxY = h.Channels.FindIndex(c => c.Name == "Y");
            if (idxR < 0 && idxG < 0 && idxB < 0 && idxY < 0)
            {
                return null;
            }

            for (var i = 0; i < h.Width * h.Height; i++)
            {
                rgba[i * 4 + 3] = 1f;
            }

            var lineBytes = h.Channels.Sum(c => h.Width * SampleSize(c.Type));
            for (var chunk = 0; chunk < chunks; chunk++)
            {
                var chunkOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + chunk * 8, 8)));
                var y = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(chunkOffset, 4));
                var size = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(chunkOffset + 4, 4));
                var payload = data.Slice(chunkOffset + 8, size);
                var row0 = y - h.YMin;
                var lines = Math.Min(h.LinesPerChunk, h.Height - row0);
                var expected = lines * lineBytes;
                var raw = size == expected ? payload.ToArray() : Decompress(payload, h.Compression, expected);
                if (raw is null || raw.Length < expected)
                {
                    return null;
                }

                var pos = 0;
                for (var line = 0; line < lines; line++)
                {
                    var row = row0 + line;
                    for (var ci = 0; ci < h.Channels.Count; ci++)
                    {
                        var ch = h.Channels[ci];
                        var ss = SampleSize(ch.Type);
                        int dest = ci == idxR ? 0 : ci == idxG ? 1 : ci == idxB ? 2 : ci == idxA ? 3 : ci == idxY ? -2 : -1;
                        if (dest != -1 && row >= 0 && row < h.Height)
                        {
                            for (var x = 0; x < h.Width; x++)
                            {
                                var v = ReadSample(raw.AsSpan(pos + x * ss, ss), ch.Type);
                                var p = (row * h.Width + x) * 4;
                                if (dest == -2)
                                {
                                    if (idxR < 0) rgba[p] = v;
                                    if (idxG < 0) rgba[p + 1] = v;
                                    if (idxB < 0) rgba[p + 2] = v;
                                }
                                else
                                {
                                    rgba[p + dest] = v;
                                }
                            }
                        }

                        pos += h.Width * ss;
                    }
                }
            }

            return new Frame(rgba, h.Width, h.Height, CompressionName(h.Compression) ?? "?");
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException
                                       or InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>Rec.709 / sRGB primaries and D65 white: the OpenEXR default when <c>chromaticities</c> is absent.</summary>
    public static readonly float[] Rec709Chromaticities = [0.64f, 0.33f, 0.30f, 0.60f, 0.15f, 0.06f, 0.3127f, 0.3290f];

    /// <summary>
    /// The first <c>chromaticities</c> attribute (red, green, blue, white xy) in the part headers,
    /// or null when absent or malformed. Walks attributes without the in-house reader's layout limits.
    /// </summary>
    public static float[]? TryReadChromaticities(ReadOnlySpan<byte> data)
    {
        foreach (var (name, type, value) in HeaderAttributes(data).SelectMany(h => h))
        {
            if (name == "chromaticities" && type == "chromaticities" && value.Length >= 32)
            {
                var c = new float[8];
                for (var i = 0; i < 8; i++)
                {
                    c[i] = BinaryPrimitives.ReadSingleLittleEndian(value.AsSpan(i * 4, 4));
                }

                return c.All(v => float.IsFinite(v) && v is > 0f and < 1f) ? c : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Attributes of each part header as (name, type, value): one header, or every header of a
    /// multi-part file. Lenient about layout (tiled, deep); stops at malformed or truncated data.
    /// </summary>
    internal static List<List<(string Name, string Type, byte[] Value)>> HeaderAttributes(ReadOnlySpan<byte> d)
    {
        var headers = new List<List<(string, string, byte[])>>();
        if (!IsExr(d) || d.Length < 8)
        {
            return headers;
        }

        var multipart = (BinaryPrimitives.ReadInt32LittleEndian(d.Slice(4, 4)) & 0x1000) != 0;
        var list = new List<(string, string, byte[])>();
        headers.Add(list);
        var pos = 8;
        try
        {
            while (pos < d.Length)
            {
                var name = ReadCString(d, ref pos);
                if (name.Length == 0)
                {
                    // End of this header. Multi-part: another header follows unless the next
                    // byte is the empty header that ends the list.
                    if (!multipart || pos >= d.Length || d[pos] == 0 || headers.Count >= 1024)
                    {
                        break;
                    }

                    list = new List<(string, string, byte[])>();
                    headers.Add(list);
                    continue;
                }

                var type = ReadCString(d, ref pos);
                var size = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(pos, 4));
                pos += 4;
                if (size < 0)
                {
                    break;
                }

                list.Add((name, type, d.Slice(pos, size).ToArray()));
                pos += size;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Truncated probe head: keep what was read.
        }

        return headers;
    }

    /// <summary>
    /// Linear RGB in <paramref name="chromaticities"/> → linear BT.709 / D65 (scRGB), Bradford-adapting
    /// the white point. Null when the primaries already are Rec.709 / D65 (or degenerate).
    /// Row-major 3×3.
    /// </summary>
    public static double[]? ToRec709Matrix(float[]? chromaticities)
    {
        if (chromaticities is not { Length: >= 8 } c)
        {
            return null;
        }

        var src = RgbToXyz(c);
        var dst = RgbToXyz(Rec709Chromaticities);
        if (src is null || dst is null || Invert(dst) is not { } xyzTo709)
        {
            return null;
        }

        var adapt = Bradford(c[6], c[7], Rec709Chromaticities[6], Rec709Chromaticities[7]);
        var m = Mul(xyzTo709, Mul(adapt, src));
        var identity = true;
        for (var i = 0; i < 9; i++)
        {
            identity &= Math.Abs(m[i] - (i % 4 == 0 ? 1 : 0)) < 1e-3;
        }

        return identity ? null : m;
    }

    /// <summary>Converts RGBA floats in place to BT.709 when the file declares other primaries.</summary>
    public static void ConvertToRec709(float[] rgba, float[]? chromaticities)
    {
        if (ToRec709Matrix(chromaticities) is not { } m)
        {
            return;
        }

        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            double r = rgba[i], g = rgba[i + 1], b = rgba[i + 2];
            rgba[i] = (float)((m[0] * r) + (m[1] * g) + (m[2] * b));
            rgba[i + 1] = (float)((m[3] * r) + (m[4] * g) + (m[5] * b));
            rgba[i + 2] = (float)((m[6] * r) + (m[7] * g) + (m[8] * b));
        }
    }

    private static double[] XyToXyz(double x, double y) => [x / y, 1, (1 - x - y) / y];

    private static double[]? RgbToXyz(float[] c)
    {
        var r = XyToXyz(c[0], c[1]);
        var g = XyToXyz(c[2], c[3]);
        var b = XyToXyz(c[4], c[5]);
        double[] p = [r[0], g[0], b[0], r[1], g[1], b[1], r[2], g[2], b[2]];
        if (Invert(p) is not { } inv)
        {
            return null;
        }

        var w = XyToXyz(c[6], c[7]);
        var s0 = (inv[0] * w[0]) + (inv[1] * w[1]) + (inv[2] * w[2]);
        var s1 = (inv[3] * w[0]) + (inv[4] * w[1]) + (inv[5] * w[2]);
        var s2 = (inv[6] * w[0]) + (inv[7] * w[1]) + (inv[8] * w[2]);
        return [p[0] * s0, p[1] * s1, p[2] * s2, p[3] * s0, p[4] * s1, p[5] * s2, p[6] * s0, p[7] * s1, p[8] * s2];
    }

    private static double[] Bradford(double sx, double sy, double dx, double dy)
    {
        double[] ma = [0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296];
        var maInv = Invert(ma)!;
        var s = Apply(ma, XyToXyz(sx, sy));
        var d = Apply(ma, XyToXyz(dx, dy));
        double[] scale = [d[0] / s[0], 0, 0, 0, d[1] / s[1], 0, 0, 0, d[2] / s[2]];
        return Mul(maInv, Mul(scale, ma));
    }

    private static double[] Apply(double[] m, double[] v) =>
    [
        (m[0] * v[0]) + (m[1] * v[1]) + (m[2] * v[2]),
        (m[3] * v[0]) + (m[4] * v[1]) + (m[5] * v[2]),
        (m[6] * v[0]) + (m[7] * v[1]) + (m[8] * v[2])
    ];

    private static double[] Mul(double[] a, double[] b)
    {
        var m = new double[9];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                m[(r * 3) + c] = (a[r * 3] * b[c]) + (a[(r * 3) + 1] * b[3 + c]) + (a[(r * 3) + 2] * b[6 + c]);
            }
        }

        return m;
    }

    private static double[]? Invert(double[] m)
    {
        var det = (m[0] * ((m[4] * m[8]) - (m[5] * m[7])))
                  - (m[1] * ((m[3] * m[8]) - (m[5] * m[6])))
                  + (m[2] * ((m[3] * m[7]) - (m[4] * m[6])));
        if (Math.Abs(det) < 1e-12)
        {
            return null;
        }

        var k = 1 / det;
        return
        [
            ((m[4] * m[8]) - (m[5] * m[7])) * k, ((m[2] * m[7]) - (m[1] * m[8])) * k, ((m[1] * m[5]) - (m[2] * m[4])) * k,
            ((m[5] * m[6]) - (m[3] * m[8])) * k, ((m[0] * m[8]) - (m[2] * m[6])) * k, ((m[2] * m[3]) - (m[0] * m[5])) * k,
            ((m[3] * m[7]) - (m[4] * m[6])) * k, ((m[1] * m[6]) - (m[0] * m[7])) * k, ((m[0] * m[4]) - (m[1] * m[3])) * k
        ];
    }

    private static int SampleSize(int type) => type == 1 ? 2 : 4;

    private static float ReadSample(ReadOnlySpan<byte> s, int type) => type switch
    {
        1 => (float)BitConverter.Int16BitsToHalf(BinaryPrimitives.ReadInt16LittleEndian(s)),
        2 => BinaryPrimitives.ReadSingleLittleEndian(s),
        _ => BinaryPrimitives.ReadUInt32LittleEndian(s) / (float)uint.MaxValue
    };

    private static byte[]? Decompress(ReadOnlySpan<byte> payload, int compression, int expected)
    {
        byte[] tmp;
        if (compression is 2 or 3)
        {
            using var input = new MemoryStream(payload.ToArray());
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            tmp = new byte[expected];
            var read = 0;
            while (read < expected)
            {
                var n = z.Read(tmp, read, expected - read);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            if (read != expected)
            {
                return null;
            }
        }
        else if (compression == 1)
        {
            tmp = new byte[expected];
            var o = 0;
            var i = 0;
            while (i < payload.Length && o < expected)
            {
                var count = (sbyte)payload[i++];
                if (count < 0)
                {
                    var n = -count;
                    payload.Slice(i, n).CopyTo(tmp.AsSpan(o, n));
                    i += n;
                    o += n;
                }
                else
                {
                    var b = payload[i++];
                    for (var k = 0; k <= count; k++)
                    {
                        tmp[o++] = b;
                    }
                }
            }

            if (o != expected)
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        // Predictor, then de-interleave the two halves.
        for (var i = 1; i < tmp.Length; i++)
        {
            tmp[i] = (byte)(tmp[i - 1] + tmp[i] - 128);
        }

        var outBuf = new byte[tmp.Length];
        var half = (tmp.Length + 1) / 2;
        for (int i = 0, a = 0, b = half; i < tmp.Length; i++)
        {
            outBuf[i] = (i & 1) == 0 ? tmp[a++] : tmp[b++];
        }

        return outBuf;
    }

    private static bool TryReadHeader(ReadOnlySpan<byte> d, out Header h, out int offsetTable)
    {
        h = null!;
        offsetTable = 0;
        if (!IsExr(d) || d.Length < 8)
        {
            return false;
        }

        var flags = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(4, 4));
        if ((flags & 0xFF) != 2 || (flags & (0x200 | 0x800 | 0x1000)) != 0)
        {
            return false; // tiled, deep or multi-part
        }

        var pos = 8;
        var channels = new List<Channel>();
        var compression = -1;
        int? xMin = null, yMin = null, xMax = null, yMax = null;
        try
        {
            while (true)
            {
                var name = ReadCString(d, ref pos);
                if (name.Length == 0)
                {
                    break;
                }

                var type = ReadCString(d, ref pos);
                var size = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(pos, 4));
                pos += 4;
                var value = d.Slice(pos, size);
                pos += size;
                if (name == "channels" && type == "chlist")
                {
                    var p = 0;
                    while (p < value.Length && value[p] != 0)
                    {
                        var cn = ReadCString(value, ref p);
                        var pt = BinaryPrimitives.ReadInt32LittleEndian(value.Slice(p, 4));
                        var xs = BinaryPrimitives.ReadInt32LittleEndian(value.Slice(p + 8, 4));
                        var ys = BinaryPrimitives.ReadInt32LittleEndian(value.Slice(p + 12, 4));
                        p += 16;
                        channels.Add(new Channel(cn, pt, xs, ys));
                    }
                }
                else if (name == "compression")
                {
                    compression = value[0];
                }
                else if (name == "dataWindow" && size >= 16)
                {
                    xMin = BinaryPrimitives.ReadInt32LittleEndian(value);
                    yMin = BinaryPrimitives.ReadInt32LittleEndian(value[4..]);
                    xMax = BinaryPrimitives.ReadInt32LittleEndian(value[8..]);
                    yMax = BinaryPrimitives.ReadInt32LittleEndian(value[12..]);
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        if (channels.Count == 0 || compression < 0 || xMin is null || yMin is null || xMax is null || yMax is null
            || channels.Any(c => c.XSampling != 1 || c.YSampling != 1 || c.Type is < 0 or > 2))
        {
            return false;
        }

        var width = xMax.Value - xMin.Value + 1;
        var height = yMax.Value - yMin.Value + 1;
        if (width is <= 0 or > 32768 || height is <= 0 or > 32768)
        {
            return false;
        }

        h = new Header(channels, compression, xMin.Value, yMin.Value, width, height);
        offsetTable = pos;
        return true;
    }

    private static string ReadCString(ReadOnlySpan<byte> d, ref int pos)
    {
        var end = d[pos..].IndexOf((byte)0);
        if (end < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(d));
        }

        var s = Encoding.ASCII.GetString(d.Slice(pos, end));
        pos += end + 1;
        return s;
    }
}
