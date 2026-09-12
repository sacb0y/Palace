namespace Palace.Helpers;

/// <summary>
/// Packed WIC pixel layouts used when converting a local HDR PNG to scRGB.
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
