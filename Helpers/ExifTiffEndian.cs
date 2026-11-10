namespace Palace.Helpers;

/// <summary>
/// TIFF/EXIF byte order from the file header (II = LE, MM = BE). Used for
/// UNICODE UserComment when no BOM is present.
/// </summary>
public static class ExifTiffEndian
{
    /// <summary>
    /// True = little-endian (Intel II). False = big-endian (Motorola MM).
    /// Returns false from <paramref name="ok"/> when the header cannot be read;
    /// callers should then default to LE.
    /// </summary>
    public static bool TryReadLittleEndian(string path, out bool littleEndian)
    {
        littleEndian = true;
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 4)
            {
                return false;
            }

            Span<byte> head = stackalloc byte[12];
            var n = stream.Read(head);
            if (n < 4)
            {
                return false;
            }

            // TIFF / DNG / many RAW: II*\0 or MM\0*
            if (IsTiffHeader(head))
            {
                littleEndian = head[0] == (byte)'I';
                return true;
            }

            // JPEG: find APP1 Exif
            if (head[0] != 0xFF || head[1] != 0xD8)
            {
                return false;
            }

            stream.Position = 2;
            Span<byte> exif = stackalloc byte[14];
            while (stream.Position + 4 <= stream.Length)
            {
                var b0 = stream.ReadByte();
                if (b0 < 0)
                {
                    return false;
                }

                if (b0 != 0xFF)
                {
                    continue;
                }

                int marker;
                do
                {
                    marker = stream.ReadByte();
                }
                while (marker == 0xFF);

                if (marker < 0)
                {
                    return false;
                }

                if (marker is 0xD9 or 0xDA)
                {
                    return false;
                }

                var lenHi = stream.ReadByte();
                var lenLo = stream.ReadByte();
                if (lenHi < 0 || lenLo < 0)
                {
                    return false;
                }

                var segLen = (lenHi << 8) | lenLo;
                if (segLen < 2 || stream.Position + segLen - 2 > stream.Length)
                {
                    return false;
                }

                if (marker == 0xE1 && segLen >= 16)
                {
                    if (stream.Read(exif) != 14)
                    {
                        return false;
                    }

                    // "Exif\0\0" then TIFF header
                    if (exif[0] == (byte)'E' && exif[1] == (byte)'x' && exif[2] == (byte)'i' && exif[3] == (byte)'f'
                        && exif[4] == 0 && exif[5] == 0
                        && IsTiffHeader(exif[6..]))
                    {
                        littleEndian = exif[6] == (byte)'I';
                        return true;
                    }

                    stream.Position += segLen - 2 - 14;
                    continue;
                }

                stream.Position += segLen - 2;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTiffHeader(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4
        && ((bytes[0] == (byte)'I' && bytes[1] == (byte)'I' && bytes[2] == 0x2A && bytes[3] == 0x00)
            || (bytes[0] == (byte)'M' && bytes[1] == (byte)'M' && bytes[2] == 0x00 && bytes[3] == 0x2A));
}
