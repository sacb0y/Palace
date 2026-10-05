namespace Palace.Helpers;

/// <summary>
/// Packed WIC pixel layouts used when converting a local HDR still to scRGB.
/// Stays off WinUI so tests can lock channel order.
/// </summary>
public enum HdrPackedFormat
{
    Rgba16,
    Rgba8,
    Bgra8,
    P010,
    Nv12,
    Yuy2
}

public static class HdrPixels
{
    public static int BytesPerPixel(HdrPackedFormat format) =>
        format switch
        {
            HdrPackedFormat.Rgba16 => 8,
            HdrPackedFormat.P010 => 0,
            HdrPackedFormat.Nv12 => 0,
            HdrPackedFormat.Yuy2 => 2,
            _ => 4
        };

    public static bool IsYuv(HdrPackedFormat format) =>
        format is HdrPackedFormat.P010 or HdrPackedFormat.Nv12 or HdrPackedFormat.Yuy2;

    /// <summary>
    /// AVIF/HEIF 4:4:4 often cannot copy as P010. WIC may hand back
    /// <c>Rgba16</c>/<c>Yuy2</c> with Y/U/V still in the channels.
    /// </summary>
    public static bool TreatAsYuv(HdrPackedFormat format, bool needsYuvConvert) =>
        IsYuv(format) || (needsYuvConvert && format is HdrPackedFormat.Rgba16 or HdrPackedFormat.Rgba8 or HdrPackedFormat.Bgra8);

    public static bool HasPackedData(ReadOnlySpan<byte> data, HdrPackedFormat format, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (format == HdrPackedFormat.P010)
        {
            return data.Length >= width * height * 3;
        }

        if (format == HdrPackedFormat.Nv12)
        {
            return data.Length >= width * height * 3 / 2;
        }

        if (format == HdrPackedFormat.Yuy2)
        {
            return data.Length >= width * height * 2;
        }

        var bpp = BytesPerPixel(format);
        return bpp > 0 && data.Length >= width * height * bpp;
    }

    /// <summary>
    /// Tight-packed P010 / NV12: Y plane then interleaved UV (4:2:0).
    /// P010 samples are MSB-aligned 10-bit in 16-bit words.
    /// </summary>
    public static void ReadYuv(
        ReadOnlySpan<byte> data,
        int width,
        int height,
        int x,
        int y,
        HdrPackedFormat format,
        out float luma,
        out float u,
        out float v)
    {
        luma = u = v = 0;
        if (x < 0 || y < 0 || x >= width || y >= height || !HasPackedData(data, format, width, height))
        {
            return;
        }

        if (format == HdrPackedFormat.P010)
        {
            var yStride = width * 2;
            var uvStride = width * 2;
            var yOff = (y * yStride) + (x * 2);
            var uvOff = (yStride * height) + ((y / 2) * uvStride) + ((x / 2) * 4);
            luma = (ReadU16(data, yOff) >> 6) / 1023f;
            u = (ReadU16(data, uvOff) >> 6) / 1023f;
            v = (ReadU16(data, uvOff + 2) >> 6) / 1023f;
            return;
        }

        if (format == HdrPackedFormat.Nv12)
        {
            var yOff = (y * width) + x;
            var uvOff = (width * height) + ((y / 2) * width) + ((x / 2) * 2);
            luma = data[yOff] / 255f;
            u = data[uvOff] / 255f;
            v = data[uvOff + 1] / 255f;
            return;
        }

        if (format == HdrPackedFormat.Yuy2)
        {
            var pair = (y * width * 2) + ((x / 2) * 4);
            luma = data[pair + ((x & 1) == 0 ? 0 : 2)] / 255f;
            u = data[pair + 1] / 255f;
            v = data[pair + 3] / 255f;
        }
    }

    /// <summary>
    /// 4:4:4 identity/YCbCr in an RGB WIC buffer: R=Y, G=Cb, B=Cr.
    /// </summary>
    public static void ReadYuvPackedRgb(
        ReadOnlySpan<byte> data,
        int width,
        int height,
        int x,
        int y,
        HdrPackedFormat format,
        out float luma,
        out float u,
        out float v)
    {
        luma = u = v = 0;
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }

        if (IsYuv(format))
        {
            ReadYuv(data, width, height, x, y, format, out luma, out u, out v);
            return;
        }

        Read(data, (y * width) + x, format, out luma, out u, out v, out _);
    }

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

    /// <summary>
    /// WIC <c>BitmapTransform</c> scales in source (unoriented) space, then
    /// <c>RespectExifOrientation</c> swaps 90/270. Dest is the oriented size.
    /// </summary>
    public static (int Width, int Height) SourceScaleSize(
        int sourceWidth,
        int sourceHeight,
        int orientedWidth,
        int orientedHeight,
        int destWidth,
        int destHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || destWidth <= 0 || destHeight <= 0)
        {
            return (destWidth, destHeight);
        }

        var swap = orientedWidth == sourceHeight
            && orientedHeight == sourceWidth
            && sourceWidth != sourceHeight;
        return swap ? (destHeight, destWidth) : (destWidth, destHeight);
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
