namespace Palace.Helpers;

internal static class ImageDimensions
{
    public static (int Width, int Height)? TryRead(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !CloudFile.Exists(path) || CloudFile.IsOnlineOnly(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return TryRead(stream);
        }
        catch
        {
            return null;
        }
    }

    public static (int Width, int Height)? TryRead(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 16)
        {
            return null;
        }

        Span<byte> buf = stackalloc byte[32];
        stream.Position = 0;
        var n = stream.Read(buf);
        if (n < 10)
        {
            return null;
        }

        if (n >= 24 && buf[0] == 0x89 && buf[1] == (byte)'P' && buf[2] == (byte)'N' && buf[3] == (byte)'G')
        {
            return Positive(ReadBigEndian32(buf, 16), ReadBigEndian32(buf, 20));
        }

        if (buf[0] == (byte)'G' && buf[1] == (byte)'I' && buf[2] == (byte)'F')
        {
            return Positive(buf[6] | (buf[7] << 8), buf[8] | (buf[9] << 8));
        }

        if (buf[0] == (byte)'B' && buf[1] == (byte)'M' && n >= 26)
        {
            var headerSize = ReadLittleEndian32(buf, 14);
            if (headerSize == 12)
            {
                return Positive(buf[18] | (buf[19] << 8), buf[20] | (buf[21] << 8));
            }

            return Positive(ReadLittleEndian32(buf, 18), Math.Abs(ReadLittleEndian32(buf, 22)));
        }

        if (n >= 16
            && buf[0] == (byte)'R' && buf[1] == (byte)'I' && buf[2] == (byte)'F' && buf[3] == (byte)'F'
            && buf[8] == (byte)'W' && buf[9] == (byte)'E' && buf[10] == (byte)'B' && buf[11] == (byte)'P')
        {
            return TryReadWebp(stream);
        }

        if (buf[0] == 0xFF && buf[1] == 0xD8)
        {
            return TryReadJpeg(stream);
        }

        if (AvifFile.IsHeif(buf[..n]))
        {
            return AvifFile.TryReadSize(stream);
        }

        if (RadianceFile.IsRadiance(buf[..n]))
        {
            return RadianceFile.TryReadSize(stream);
        }

        if (StillFormats.IsPsd(buf[..n]))
        {
            return StillFormats.TryReadPsdSize(buf[..n]);
        }

        if (StillFormats.IsDds(buf[..n]))
        {
            return StillFormats.TryReadDdsSize(buf[..n]);
        }

        if (StillFormats.IsJxr(buf[..n]))
        {
            stream.Position = 0;
            var take = (int)Math.Min(stream.Length, 64 * 1024);
            var jxr = new byte[take];
            var read = stream.Read(jxr, 0, take);
            return read > 0 ? StillFormats.TryReadJxrSize(jxr.AsSpan(0, read)) : null;
        }

        if (StillFormats.IsJxl(buf[..n]))
        {
            stream.Position = 0;
            var take = (int)Math.Min(stream.Length, 64 * 1024);
            var jxl = new byte[take];
            var read = stream.Read(jxl, 0, take);
            return read > 0 ? StillFormats.TryReadJxlSize(jxl.AsSpan(0, read)) : null;
        }

        return null;
    }

    private static (int Width, int Height)? TryReadJpeg(Stream stream)
    {
        stream.Position = 2;
        var lenBuf = new byte[2];
        var sof = new byte[5];
        while (stream.Position < stream.Length)
        {
            var b = stream.ReadByte();
            if (b < 0)
            {
                return null;
            }

            if (b != 0xFF)
            {
                continue;
            }

            int marker;
            do
            {
                marker = stream.ReadByte();
            } while (marker == 0xFF);

            if (marker < 0)
            {
                return null;
            }

            if (marker is 0xD0 or 0xD1 or 0xD2 or 0xD3 or 0xD4 or 0xD5 or 0xD6 or 0xD7 or 0xD8 or 0xD9 or 0x01)
            {
                if (marker == 0xD9)
                {
                    return null;
                }

                continue;
            }

            if (stream.Read(lenBuf, 0, 2) != 2)
            {
                return null;
            }

            var length = (lenBuf[0] << 8) | lenBuf[1];
            var isSof = marker is (>= 0xC0 and <= 0xC3) or (>= 0xC5 and <= 0xC7) or (>= 0xC9 and <= 0xCB) or (>= 0xCD and <= 0xCF);
            if (isSof)
            {
                if (length < 7 || stream.Read(sof, 0, 5) != 5)
                {
                    return null;
                }

                var height = (sof[1] << 8) | sof[2];
                var width = (sof[3] << 8) | sof[4];
                return Positive(width, height);
            }

            if (marker == 0xDA)
            {
                return null;
            }

            var skip = length - 2;
            if (skip < 0 || stream.Position + skip > stream.Length)
            {
                return null;
            }

            stream.Position += skip;
        }

        return null;
    }

    private static (int Width, int Height)? TryReadWebp(Stream stream)
    {
        stream.Position = 12;
        var header = new byte[16];
        if (stream.Read(header, 0, 8) < 8)
        {
            return null;
        }

        var fourcc = System.Text.Encoding.ASCII.GetString(header, 0, 4);
        var chunkSize = header[4] | (header[5] << 8) | (header[6] << 16) | (header[7] << 24);
        if (fourcc == "VP8X")
        {
            if (stream.Read(header, 0, 10) < 10)
            {
                return null;
            }

            var width = 1 + (header[4] | (header[5] << 8) | (header[6] << 16));
            var height = 1 + (header[7] | (header[8] << 8) | (header[9] << 16));
            return Positive(width, height);
        }

        if (fourcc == "VP8 " && chunkSize >= 10)
        {
            if (stream.Read(header, 0, 10) < 10)
            {
                return null;
            }

            if (header[3] != 0x9D || header[4] != 0x01 || header[5] != 0x2A)
            {
                return null;
            }

            var width = header[6] | (header[7] << 8);
            var height = header[8] | (header[9] << 8);
            return Positive(width & 0x3FFF, height & 0x3FFF);
        }

        if (fourcc == "VP8L" && chunkSize >= 5)
        {
            if (stream.Read(header, 0, 5) < 5)
            {
                return null;
            }

            if (header[0] != 0x2F)
            {
                return null;
            }

            var bits = header[1] | (header[2] << 8) | (header[3] << 16) | (header[4] << 24);
            var width = (bits & 0x3FFF) + 1;
            var height = ((bits >> 14) & 0x3FFF) + 1;
            return Positive(width, height);
        }

        return null;
    }

    private static (int Width, int Height)? Positive(int width, int height) =>
        width > 0 && height > 0 ? (width, height) : null;

    private static int ReadBigEndian32(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static int ReadLittleEndian32(ReadOnlySpan<byte> data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
}
