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
    /// True YUV WIC buffers, or packed RGB that still holds YUV/GBR
    /// planes. When <paramref name="identityMatrix"/> is false, WIC
    /// <c>Rgba16</c> for HDR AVIF is usually already RGB — do not
    /// run CICP YUV→RGB again (magenta / MaxCLL≈207 false present).
    /// Identity (matrix 0) still needs GBR remap on packed RGB.
    /// </summary>
    public static bool TreatAsYuv(
        HdrPackedFormat format,
        bool needsYuvConvert,
        bool identityMatrix = false) =>
        IsYuv(format)
        || (needsYuvConvert
            && identityMatrix
            && format is HdrPackedFormat.Rgba16 or HdrPackedFormat.Rgba8 or HdrPackedFormat.Bgra8);

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
    /// HEIF <c>irot</c>/<c>imir</c> → EXIF Orientation 1–8. Matches libavif
    /// 1.2.2+ <c>avifImageExtractExifOrientationToIrotImir</c> /
    /// <c>avifImageIrotImirToExifOrientation</c>: irot angle is 90°
    /// anti-clockwise turns; imir 0 = top↔bottom, 1 = left↔right.
    /// Angle 1 is EXIF 8, angle 3 is EXIF 6 (not the reverse).
    /// </summary>
    public static uint ExifOrientationFromIrotImir(bool hasIrot, byte angle, bool hasImir, byte mode)
    {
        angle = (byte)(angle & 3);
        mode = (byte)(mode & 1);
        if (hasIrot && angle == 1)
        {
            if (hasImir)
            {
                return mode != 0 ? 7u : 5u;
            }

            return 8u;
        }

        if (hasIrot && angle == 2)
        {
            if (hasImir)
            {
                return mode != 0 ? 4u : 2u;
            }

            return 3u;
        }

        if (hasIrot && angle == 3)
        {
            if (hasImir)
            {
                return mode != 0 ? 5u : 7u;
            }

            return 6u;
        }

        if (hasImir)
        {
            return mode != 0 ? 2u : 4u;
        }

        return 1u;
    }

    /// <summary>
    /// libavif <c>avifGetExifOrientationOffset</c> returns OK with
    /// <paramref name="offset"/> equal to payload size when the tag is
    /// missing — do not read that byte.
    /// </summary>
    public static bool TryExifOrientationAt(ReadOnlySpan<byte> exif, nuint offset, out byte value)
    {
        if (offset >= (nuint)exif.Length)
        {
            value = 1;
            return false;
        }

        value = exif[(int)offset];
        return value is >= 1 and <= 8;
    }

    /// <summary>
    /// Map an upright (oriented) pixel to the stored buffer coordinate for
    /// the given EXIF Orientation tag.
    /// </summary>
    public static void OrientedToSource(
        int orientedX,
        int orientedY,
        int sourceWidth,
        int sourceHeight,
        uint orientation,
        out int sourceX,
        out int sourceY)
    {
        switch (orientation)
        {
            case 2:
                sourceX = sourceWidth - 1 - orientedX;
                sourceY = orientedY;
                return;
            case 3:
                sourceX = sourceWidth - 1 - orientedX;
                sourceY = sourceHeight - 1 - orientedY;
                return;
            case 4:
                sourceX = orientedX;
                sourceY = sourceHeight - 1 - orientedY;
                return;
            case 5: // Transpose (mirror of 6)
                sourceX = orientedY;
                sourceY = orientedX;
                return;
            case 6: // Rotate 90° CW to upright (WIC / Pillow ROTATE_270)
                sourceX = sourceWidth - 1 - orientedY;
                sourceY = orientedX;
                return;
            case 7: // Transverse (mirror of 8)
                sourceX = sourceWidth - 1 - orientedY;
                sourceY = sourceHeight - 1 - orientedX;
                return;
            case 8: // Rotate 90° CCW to upright (WIC / Pillow ROTATE_90)
                sourceX = orientedY;
                sourceY = sourceHeight - 1 - orientedX;
                return;
            default:
                sourceX = orientedX;
                sourceY = orientedY;
                return;
        }
    }

    /// <summary>
    /// Copy stored scRGB RGBA into an upright buffer matching WIC
    /// <c>RespectExifOrientation</c>. Orientation 1 returns <paramref name="source"/>.
    /// </summary>
    public static (float[] Rgba, int Width, int Height) OrientScrgbRgba(
        float[] source,
        int sourceWidth,
        int sourceHeight,
        uint orientation)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || source.Length < sourceWidth * sourceHeight * 4)
        {
            return (source, sourceWidth, sourceHeight);
        }

        if (orientation is < 2 or > 8)
        {
            return (source, sourceWidth, sourceHeight);
        }

        var (ow, oh) = OrientedSize(sourceWidth, sourceHeight, orientation);
        var dest = new float[ow * oh * 4];
        for (var y = 0; y < oh; y++)
        {
            for (var x = 0; x < ow; x++)
            {
                OrientedToSource(x, y, sourceWidth, sourceHeight, orientation, out var sx, out var sy);
                if ((uint)sx >= (uint)sourceWidth || (uint)sy >= (uint)sourceHeight)
                {
                    continue;
                }

                var si = ((sy * sourceWidth) + sx) * 4;
                var di = ((y * ow) + x) * 4;
                dest[di] = source[si];
                dest[di + 1] = source[si + 1];
                dest[di + 2] = source[si + 2];
                dest[di + 3] = source[si + 3];
            }
        }

        return (dest, ow, oh);
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
