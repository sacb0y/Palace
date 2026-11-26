namespace Palace.Helpers;

/// <summary>
/// Read EXIF Orientation (tag 0x0112) from JPEG APP1 / TIFF IFD0 without
/// WinRT. Used by Smooth ≥64MP strip: tiles stay storage-oriented
/// (<c>IgnoreExifOrientation</c>); view applies this tag via
/// <see cref="SmoothViewport.ExifOrient"/>.
/// </summary>
public static class ExifOrientation
{
    public const ushort TagId = 0x0112;

    /// <summary>1–8, or 1 when missing / unreadable.</summary>
    public static int TryRead(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return 1;
        }

        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return TryRead(stream);
        }
        catch (IOException)
        {
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            return 1;
        }
    }

    /// <summary>1–8, or 1 when missing / unreadable.</summary>
    public static int TryRead(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            return 1;
        }

        try
        {
            Span<byte> head = stackalloc byte[4];
            if (ReadExact(stream, head) < 4)
            {
                return 1;
            }

            if (IsTiffHeader(head))
            {
                stream.Position = 0;
                return ReadOrientationFromTiff(stream) ?? 1;
            }

            if (head[0] != 0xFF || head[1] != 0xD8)
            {
                return 1;
            }

            // JPEG: scan markers for APP1 Exif.
            stream.Position = 2;
            while (stream.Position + 4 <= stream.Length)
            {
                var b0 = stream.ReadByte();
                if (b0 < 0)
                {
                    return 1;
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

                if (marker < 0 || marker is 0xD9 or 0xDA)
                {
                    return 1;
                }

                var lenHi = stream.ReadByte();
                var lenLo = stream.ReadByte();
                if (lenHi < 0 || lenLo < 0)
                {
                    return 1;
                }

                var segLen = (lenHi << 8) | lenLo;
                if (segLen < 2 || stream.Position + segLen - 2 > stream.Length)
                {
                    return 1;
                }

                if (marker == 0xE1 && segLen >= 16)
                {
                    var payloadLen = segLen - 2;
                    var payload = new byte[payloadLen];
                    if (ReadExact(stream, payload) < payloadLen)
                    {
                        return 1;
                    }

                    // "Exif\0\0" then TIFF.
                    if (payload.Length >= 14
                        && payload[0] == (byte)'E'
                        && payload[1] == (byte)'x'
                        && payload[2] == (byte)'i'
                        && payload[3] == (byte)'f'
                        && payload[4] == 0
                        && payload[5] == 0
                        && IsTiffHeader(payload.AsSpan(6)))
                    {
                        return ReadOrientationFromTiffBytes(payload.AsSpan(6)) ?? 1;
                    }

                    continue;
                }

                stream.Position += segLen - 2;
            }

            return 1;
        }
        catch (IOException)
        {
            return 1;
        }
    }

    /// <summary>In-memory JPEG/TIFF bytes — unit tests / fixtures.</summary>
    public static int TryRead(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4)
        {
            return 1;
        }

        using var ms = new MemoryStream(file.ToArray(), writable: false);
        return TryRead(ms);
    }

    private static int? ReadOrientationFromTiff(Stream stream)
    {
        var len = (int)Math.Min(stream.Length, 64 * 1024);
        if (len < 8)
        {
            return null;
        }

        var buf = new byte[len];
        stream.Position = 0;
        if (ReadExact(stream, buf) < 8)
        {
            return null;
        }

        return ReadOrientationFromTiffBytes(buf);
    }

    private static int? ReadOrientationFromTiffBytes(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8 || !IsTiffHeader(tiff))
        {
            return null;
        }

        var le = tiff[0] == (byte)'I';
        var ifd0 = ReadU32(tiff, 4, le);
        if (ifd0 + 2 > (uint)tiff.Length)
        {
            return null;
        }

        var count = ReadU16(tiff, (int)ifd0, le);
        var entry0 = (int)ifd0 + 2;
        for (var i = 0; i < count; i++)
        {
            var off = entry0 + (i * 12);
            if (off + 12 > tiff.Length)
            {
                return null;
            }

            var tag = ReadU16(tiff, off, le);
            if (tag != TagId)
            {
                continue;
            }

            var type = ReadU16(tiff, off + 2, le);
            var n = ReadU32(tiff, off + 4, le);
            if (n < 1)
            {
                return null;
            }

            // SHORT (3) or LONG (4) — value in the 4-byte value/offset field when fits.
            int value;
            if (type == 3)
            {
                value = ReadU16(tiff, off + 8, le);
            }
            else if (type == 4)
            {
                value = (int)ReadU32(tiff, off + 8, le);
            }
            else
            {
                return null;
            }

            return value is >= 1 and <= 8 ? value : null;
        }

        return null;
    }

    private static bool IsTiffHeader(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4
        && ((bytes[0] == (byte)'I' && bytes[1] == (byte)'I' && bytes[2] == 0x2A && bytes[3] == 0x00)
            || (bytes[0] == (byte)'M' && bytes[1] == (byte)'M' && bytes[2] == 0x00 && bytes[3] == 0x2A));

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset, bool le) =>
        le
            ? (ushort)(data[offset] | (data[offset + 1] << 8))
            : (ushort)((data[offset] << 8) | data[offset + 1]);

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset, bool le) =>
        le
            ? (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24))
            : (uint)((data[offset] << 24)
                | (data[offset + 1] << 16)
                | (data[offset + 2] << 8)
                | data[offset + 3]);

    private static int ReadExact(Stream stream, Span<byte> dest)
    {
        var total = 0;
        while (total < dest.Length)
        {
            var n = stream.Read(dest[total..]);
            if (n <= 0)
            {
                break;
            }

            total += n;
        }

        return total;
    }
}
