using System.Buffers.Binary;
using System.Text;

namespace Palace.Imaging;

/// <summary>
/// Minimal OpenEXR header reader for attributes P1 cares about
/// (chromaticities, whiteLuminance). Full scanline decode is P2.
/// </summary>
public static class ExrHeader
{
    /// <summary>
    /// Read <c>chromaticities</c> (8× float) from an OpenEXR file header.
    /// Returns false when the attribute is absent (documented LinearScene
    /// default: BT.709 via <see cref="NormalizeOptions.Primaries"/>).
    /// </summary>
    public static bool TryReadChromaticities(ReadOnlySpan<byte> file, out Chromaticities chrom)
    {
        chrom = default;
        if (!TryFindAttribute(file, "chromaticities", "chromaticities", out var payload) || payload.Length < 32)
        {
            return false;
        }

        chrom = new Chromaticities(
            BinaryPrimitives.ReadSingleLittleEndian(payload),
            BinaryPrimitives.ReadSingleLittleEndian(payload[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[8..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[12..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[16..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[20..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[24..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[28..]));
        return true;
    }

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

        var i = 8;
        while (i < file.Length)
        {
            if (!TryReadCString(file, ref i, out var attrName))
            {
                return false;
            }

            if (attrName.Length == 0)
            {
                return false; // end of header
            }

            if (!TryReadCString(file, ref i, out var attrType) || i + 4 > file.Length)
            {
                return false;
            }

            var size = BinaryPrimitives.ReadInt32LittleEndian(file[i..]);
            i += 4;
            if (size < 0 || i + size > file.Length)
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
