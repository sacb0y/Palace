using System.Buffers.Binary;
using System.IO.Compression;

namespace Palace.Helpers;

/// <summary>
/// Minimal ICC reader for PNG <c>iCCP</c>: recognises Display P3 and BT.2020 RGB
/// profiles with an sRGB-like tone curve, so ICC-only wide-gamut PNGs are
/// converted like their cICP-tagged twins (cICP primaries 12 / 9, sRGB transfer).
/// Matches the rXYZ / gXYZ / bXYZ colorants on xy, either D50-adapted (ICC v4 /
/// v2 with <c>chad</c>, e.g. Apple's Display P3) or the nominal D65 primaries.
/// Anything else (other primaries, linear or unusual curves, CMYK / gray,
/// LUT-only profiles) is not recognised and the PNG stays plain sRGB.
/// </summary>
public static class IccProfile
{
    /// <summary>Largest decompressed profile Palace will inspect.</summary>
    public const int MaxProfileBytes = 4 * 1024 * 1024;

    /// <summary>xy tolerance per colorant.</summary>
    public const double ColorantTolerance = 0.004;

    private static readonly (int Cicp, string Name, double[][] Sets)[] Known =
    [
        (12, "Display P3", [
            [0.6820, 0.3193, 0.2846, 0.6746, 0.1559, 0.0661], // Bradford D65 → D50
            [0.680, 0.320, 0.265, 0.690, 0.150, 0.060]]),     // nominal D65
        (9, "BT.2020", [
            [0.7085, 0.2935, 0.1902, 0.7754, 0.1292, 0.0471],
            [0.708, 0.292, 0.170, 0.797, 0.131, 0.046]]),
    ];

    /// <summary>Profile bytes from a PNG <c>iCCP</c> payload (name, NUL, method 0, zlib), or null.</summary>
    public static byte[]? FromPngIccp(ReadOnlySpan<byte> payload)
    {
        var nul = payload[..Math.Min(payload.Length, 80)].IndexOf((byte)0);
        if (nul < 1 || nul + 2 > payload.Length || payload[nul + 1] != 0)
        {
            return null;
        }

        try
        {
            using var input = new MemoryStream(payload[(nul + 2)..].ToArray(), writable: false);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = z.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxProfileBytes)
                {
                    return null;
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// cICP-style primaries code (12 Display P3, 9 BT.2020) and a display name for a
    /// recognised RGB profile with an sRGB-like curve, or null.
    /// </summary>
    public static (int CicpPrimaries, string Name)? MatchWideGamut(ReadOnlySpan<byte> icc)
    {
        if (icc.Length < 132
            || !icc.Slice(16, 4).SequenceEqual("RGB "u8)
            || !icc.Slice(20, 4).SequenceEqual("XYZ "u8))
        {
            return null;
        }

        if (Colorant(icc, "rXYZ"u8) is not { } r
            || Colorant(icc, "gXYZ"u8) is not { } g
            || Colorant(icc, "bXYZ"u8) is not { } b)
        {
            return null;
        }

        if (!SrgbLikeCurve(icc, "rTRC"u8) || !SrgbLikeCurve(icc, "gTRC"u8) || !SrgbLikeCurve(icc, "bTRC"u8))
        {
            return null;
        }

        double[] xy = [r.X, r.Y, g.X, g.Y, b.X, b.Y];
        foreach (var (cicp, name, sets) in Known)
        {
            foreach (var set in sets)
            {
                var match = true;
                for (var i = 0; i < 6 && match; i++)
                {
                    match = Math.Abs(xy[i] - set[i]) <= ColorantTolerance;
                }

                if (match)
                {
                    return (cicp, name);
                }
            }
        }

        return null;
    }

    private static ReadOnlySpan<byte> Tag(ReadOnlySpan<byte> icc, ReadOnlySpan<byte> signature)
    {
        var count = BinaryPrimitives.ReadUInt32BigEndian(icc.Slice(128, 4));
        if (count > (uint)((icc.Length - 132) / 12))
        {
            return default;
        }

        for (var t = 0; t < (int)count; t++)
        {
            var e = 132 + (t * 12);
            if (!icc.Slice(e, 4).SequenceEqual(signature))
            {
                continue;
            }

            var offset = BinaryPrimitives.ReadUInt32BigEndian(icc.Slice(e + 4, 4));
            var size = BinaryPrimitives.ReadUInt32BigEndian(icc.Slice(e + 8, 4));
            return offset <= (uint)icc.Length && size <= (uint)icc.Length - offset
                ? icc.Slice((int)offset, (int)size)
                : default;
        }

        return default;
    }

    private static (double X, double Y)? Colorant(ReadOnlySpan<byte> icc, ReadOnlySpan<byte> signature)
    {
        var tag = Tag(icc, signature);
        if (tag.Length < 20 || !tag[..4].SequenceEqual("XYZ "u8))
        {
            return null;
        }

        var x = S15Fixed16(tag, 8);
        var y = S15Fixed16(tag, 12);
        var z = S15Fixed16(tag, 16);
        var sum = x + y + z;
        return sum > 1e-6 ? (x / sum, y / sum) : null;
    }

    /// <summary>
    /// True for the curves Display P3 / sRGB-style profiles use: <c>para</c> with gamma
    /// 2.1–2.5 (sRGB is type 3 with g = 2.4), <c>curv</c> with one gamma in that range,
    /// or a <c>curv</c> table whose midpoint is near sRGB's (0.214).
    /// </summary>
    private static bool SrgbLikeCurve(ReadOnlySpan<byte> icc, ReadOnlySpan<byte> signature)
    {
        var tag = Tag(icc, signature);
        if (tag.Length < 12)
        {
            return false;
        }

        if (tag[..4].SequenceEqual("para"u8))
        {
            // ICC parametricCurveType: function types 0–4 carry 1, 3, 4, 5 or 7
            // s15Fixed16 parameters after the 12-byte header; g is always first.
            var function = BinaryPrimitives.ReadUInt16BigEndian(tag.Slice(8, 2));
            int[] paramCounts = [1, 3, 4, 5, 7];
            if (function >= paramCounts.Length || tag.Length < 12 + (4 * paramCounts[function]))
            {
                return false;
            }

            var gamma = S15Fixed16(tag, 12);
            return gamma is >= 2.1 and <= 2.5;
        }

        if (!tag[..4].SequenceEqual("curv"u8))
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(tag.Slice(8, 4));
        if (count == 1 && tag.Length >= 14)
        {
            var gamma = BinaryPrimitives.ReadUInt16BigEndian(tag.Slice(12, 2)) / 256.0;
            return gamma is >= 2.1 and <= 2.5;
        }

        // Bound count by the bytes present before multiplying (a crafted count must not wrap).
        if (count < 16 || count > (uint)((tag.Length - 12) / 2))
        {
            return false;
        }

        // Value at input 0.5, interpolated between the two nearest entries.
        var pos = (count - 1) * 0.5;
        var lo = (int)Math.Floor(pos);
        var hi = Math.Min(lo + 1, (int)count - 1);
        var vlo = BinaryPrimitives.ReadUInt16BigEndian(tag.Slice(12 + (lo * 2), 2)) / 65535.0;
        var vhi = BinaryPrimitives.ReadUInt16BigEndian(tag.Slice(12 + (hi * 2), 2)) / 65535.0;
        var mid = vlo + ((vhi - vlo) * (pos - lo));
        return mid is >= 0.19 and <= 0.24;
    }

    private static double S15Fixed16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4)) / 65536.0;
}
