namespace Palace.Helpers;

/// <summary>
/// Packed WIC pixel layouts used when converting a local HDR still to scRGB.
/// Stays off WinUI so tests can lock channel order.
/// </summary>
public enum HdrPackedFormat
{
    Rgba16,
    Rgba8,
    Bgra8
}

public static class HdrPixels
{
    public static int BytesPerPixel(HdrPackedFormat format) =>
        format == HdrPackedFormat.Rgba16 ? 8 : 4;

    /// <summary>
    /// EXIF orientations 5–8 are 90°/270° (and mirrors of those), so the
    /// oriented buffer stride is the swapped stored size.
    /// </summary>
    public static (int Width, int Height) OrientedSize(int pixelWidth, int pixelHeight, uint orientation)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return (0, 0);
        }

        return orientation is 5 or 6 or 7 or 8
            ? (pixelHeight, pixelWidth)
            : (pixelWidth, pixelHeight);
    }

    public static void Read(
        ReadOnlySpan<byte> data,
        int pixelIndex,
        HdrPackedFormat format,
        out float r,
        out float g,
        out float b,
        out float a)
    {
        var bpp = BytesPerPixel(format);
        var offset = pixelIndex * bpp;
        if (pixelIndex < 0 || offset < 0 || offset + bpp > data.Length)
        {
            r = g = b = a = 0;
            return;
        }

        switch (format)
        {
            case HdrPackedFormat.Rgba16:
                r = ReadU16(data, offset) / 65535f;
                g = ReadU16(data, offset + 2) / 65535f;
                b = ReadU16(data, offset + 4) / 65535f;
                a = ReadU16(data, offset + 6) / 65535f;
                break;
            case HdrPackedFormat.Bgra8:
                b = data[offset] / 255f;
                g = data[offset + 1] / 255f;
                r = data[offset + 2] / 255f;
                a = data[offset + 3] / 255f;
                break;
            default:
                r = data[offset] / 255f;
                g = data[offset + 1] / 255f;
                b = data[offset + 2] / 255f;
                a = data[offset + 3] / 255f;
                break;
        }
    }

    private static int ReadU16(ReadOnlySpan<byte> data, int offset) =>
        data[offset] | (data[offset + 1] << 8);
}
