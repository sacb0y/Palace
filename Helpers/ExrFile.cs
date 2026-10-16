using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Minimal OpenEXR reader for stills: single-part scanline files with
/// NONE, RLE, ZIPS or ZIP compression and HALF / FLOAT / UINT channels.
/// Returns linear RGBA floats (R/G/B/A, or Y as grey). Tiled, deep,
/// multi-part and PIZ/PXR24/B44/DWA files return null.
/// </summary>
public static class ExrFile
{
    public const int Magic = 20000630;

    public sealed record Frame(float[] Rgba, int Width, int Height, string Compression);

    private sealed record Channel(string Name, int Type, int XSampling, int YSampling);

    public static bool IsExr(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && BinaryPrimitives.ReadInt32LittleEndian(data) == Magic;

    public static (int Width, int Height)? TryReadSize(ReadOnlySpan<byte> data) =>
        TryReadHeader(data, out var h, out _) ? (h.Width, h.Height) : null;

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
        _ => null
    };

    private sealed record Header(List<Channel> Channels, int Compression, int XMin, int YMin, int Width, int Height)
    {
        public int LinesPerChunk => Compression == 3 ? 16 : 1;
    }

    public static Frame? TryDecode(ReadOnlySpan<byte> data)
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
