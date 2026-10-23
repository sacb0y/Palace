using System.Buffers.Binary;
using System.Text;

namespace Palace.Imaging;

/// <summary>
/// Minimal OpenEXR header reader for attributes used by Normalize
/// (chromaticities, whiteLuminance). Pixel decode stays in <c>ExrFile</c> (P2).
/// </summary>
public static class ExrHeader
{
    /// <summary>
    /// Read <c>chromaticities</c> (8× float) from an OpenEXR file header
    /// (first matching part in multi-part files). Returns false when absent
    /// or out of the AP0-friendly finite range (documented LinearScene
    /// default: BT.709 via <see cref="NormalizeOptions.Primaries"/>).
    /// Shared with the legacy present path via <c>ExrFile.TryReadChromaticities</c>.
    /// </summary>
    public static bool TryReadChromaticities(ReadOnlySpan<byte> file, out Chromaticities chrom)
    {
        chrom = default;
        if (!TryFindAttribute(file, "chromaticities", "chromaticities", out var payload) || payload.Length < 32)
        {
            return false;
        }

        var c = new Chromaticities(
            BinaryPrimitives.ReadSingleLittleEndian(payload),
            BinaryPrimitives.ReadSingleLittleEndian(payload[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[8..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[12..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[16..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[20..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[24..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[28..]));
        // ACES2065-1 (AP0) puts green at x=0 and blue at y≈−0.077 — allow [-1,1];
        // reject non-finite / degenerate white or zero primary y (matrix divides by y).
        if (!IsValidChromaticities(c))
        {
            return false;
        }

        chrom = c;
        return true;
    }

    /// <summary>
    /// Same acceptance window as the legacy EXR present parser (AP0-friendly).
    /// </summary>
    public static bool IsValidChromaticities(Chromaticities c) =>
        IsFiniteInRange(c.Rx) && IsFiniteInRange(c.Ry)
        && IsFiniteInRange(c.Gx) && IsFiniteInRange(c.Gy)
        && IsFiniteInRange(c.Bx) && IsFiniteInRange(c.By)
        && IsFiniteInRange(c.Wx) && IsFiniteInRange(c.Wy)
        && c.Ry != 0f && c.Gy != 0f && c.By != 0f
        && c.Wx > 0f && c.Wy > 0f;

    private static bool IsFiniteInRange(float v) =>
        float.IsFinite(v) && v is >= -1f and <= 1f;

    public static bool TryReadWhiteLuminance(ReadOnlySpan<byte> file, out float nits)
    {
        nits = 0f;
        if (!TryFindAttribute(file, "whiteLuminance", "float", out var payload) || payload.Length < 4)
        {
            return false;
        }

        nits = BinaryPrimitives.ReadSingleLittleEndian(payload);
        return nits > 0f;
    }

    private static bool TryFindAttribute(
        ReadOnlySpan<byte> file,
        string name,
        string expectedType,
        out ReadOnlySpan<byte> payload)
    {
        payload = default;
        // magic "\x76\x2f\x31\x01" + version int32
        if (file.Length < 12
            || file[0] != 0x76 || file[1] != 0x2f || file[2] != 0x31 || file[3] != 0x01)
        {
            return false;
        }

        var multipart = (BinaryPrimitives.ReadInt32LittleEndian(file.Slice(4, 4)) & 0x1000) != 0;
        var i = 8;
        var parts = 0;
        while (i < file.Length && parts < 1024)
        {
            if (!TryReadCString(file, ref i, out var attrName))
            {
                return false;
            }

            if (attrName.Length == 0)
            {
                // End of this part header. Multi-part: another header may follow
                // unless the next byte is the empty terminator.
                parts++;
                if (!multipart || i >= file.Length || file[i] == 0)
                {
                    return false;
                }

                continue;
            }

            if (!TryReadCString(file, ref i, out var attrType) || i + 4 > file.Length)
            {
                return false;
            }

            var size = BinaryPrimitives.ReadInt32LittleEndian(file[i..]);
            i += 4;
            // N1: `i + size` overflows for size near int.MaxValue — compare remaining.
            if (size < 0 || size > file.Length - i)
            {
                return false;
            }

            var value = file.Slice(i, size);
            i += size;
            if (attrName == name && attrType == expectedType)
            {
                payload = value;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadCString(ReadOnlySpan<byte> file, ref int i, out string s)
    {
        var start = i;
        while (i < file.Length && file[i] != 0)
        {
            i++;
        }

        if (i >= file.Length)
        {
            s = "";
            return false;
        }

        s = Encoding.ASCII.GetString(file[start..i]);
        i++; // NUL
        return true;
    }
}
