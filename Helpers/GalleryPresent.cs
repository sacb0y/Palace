using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Overlay details, HDR status, dest-rect, and tonemap math. Off WinUI.
/// </summary>
public static class GalleryPresent
{
    public const float SdrReferenceNits = 203f;
    public const float ScrgbNits = 80f;

    public static bool ShouldAttemptHdrPresent(
        AssetKind kind,
        bool isOrphan,
        bool isOnlineOnly,
        bool apiOnly,
        bool onDisk,
        HdrProbe probe) =>
        kind == AssetKind.Image
        && !isOrphan
        && !isOnlineOnly
        && !apiOnly
        && onDisk
        && probe.CanPresentHdr;

    public static string DetailsLine(
        string? fileName,
        int? width,
        int? height,
        AssetKind kind,
        long? fileSize)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "Untitled" : fileName.Trim();
        var dims = width is > 0 && height is > 0 ? $"{width}×{height}" : "size unknown";
        return $"{name} · {dims} · {kind} · {FormatSize(fileSize)}";
    }

    public static string FormatSize(long? bytes)
    {
        if (bytes is null or < 0)
        {
            return "size unknown";
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes.Value / 1024.0:0.#} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes.Value / (1024.0 * 1024.0):0.#} MB";
        }

        return $"{bytes.Value / (1024.0 * 1024.0 * 1024.0):0.#} GB";
    }

    public static string? StatusLine(HdrProbe probe, bool presented, bool displayHdr) =>
        probe.Kind switch
        {
            HdrKind.UltraHdrJpeg => "Ultra HDR JPEG — showing the SDR base",
            HdrKind.HdrPng when presented && displayHdr => "HDR PNG · presenting scRGB",
            HdrKind.HdrPng when presented => "HDR PNG · tonemapped to the display (map CLL)",
            HdrKind.HdrPng => "HDR PNG — SDR preview",
            HdrKind.WideGamutPng => "Wide-gamut PNG",
            _ => null
        };

    public static float TonemapScale(float contentMaxNits, float displayPeakNits)
    {
        if (contentMaxNits <= 0 || displayPeakNits <= 0 || contentMaxNits <= displayPeakNits)
        {
            return 1f;
        }

        return displayPeakNits / contentMaxNits;
    }

    public static float MapCllAndClip(float linearNits, float contentMaxNits, float displayPeakNits)
    {
        var scaled = linearNits * TonemapScale(contentMaxNits, displayPeakNits);
        if (scaled < 0)
        {
            return 0;
        }

        return scaled > displayPeakNits ? displayPeakNits : scaled;
    }

    public static float PqEotf(float v)
    {
        const double m1 = 2610.0 / 16384.0;
        const double m2 = 2523.0 / 32.0;
        const double c1 = 3424.0 / 4096.0;
        const double c2 = 2413.0 / 128.0;
        const double c3 = 2392.0 / 128.0;
        v = Math.Clamp(v, 0f, 1f);
        var vp = Math.Pow(v, 1.0 / m2);
        var den = c2 - (c3 * vp);
        if (den <= 0)
        {
            return 0;
        }

        return (float)Math.Pow(Math.Max(vp - c1, 0) / den, 1.0 / m1);
    }

    public static float HlgEotf(float v)
    {
        const double a = 0.17883277;
        const double b = 0.28466892;
        const double c = 0.55991073;
        v = Math.Clamp(v, 0f, 1f);
        if (v <= 0.5f)
        {
            return (float)((v * v) / 3.0);
        }

        return (float)((Math.Exp((v - c) / a) + b) / 12.0);
    }

    public static void Bt2020ToBt709(float r, float g, float b, out float r709, out float g709, out float b709)
    {
        r709 = (1.660491f * r) + (-0.587641f * g) + (-0.072850f * b);
        g709 = (-0.124550f * r) + (1.132900f * g) + (-0.008349f * b);
        b709 = (-0.018151f * r) + (-0.100579f * g) + (1.118730f * b);
    }

    public static float NitsToScrgb(float nits) => nits / ScrgbNits;

    public static (float X, float Y, float W, float H) DestRect(
        ImageScaling scaling,
        float imageW,
        float imageH,
        float viewportW,
        float viewportH)
    {
        if (imageW <= 0 || imageH <= 0 || viewportW <= 0 || viewportH <= 0)
        {
            return (0, 0, Math.Max(viewportW, 0), Math.Max(viewportH, 0));
        }

        if (scaling == ImageScaling.Actual)
        {
            return (0, 0, imageW, imageH);
        }

        var imageAspect = imageW / imageH;
        var viewAspect = viewportW / viewportH;
        float w;
        float h;
        if (scaling == ImageScaling.Fill)
        {
            if (imageAspect > viewAspect)
            {
                h = viewportH;
                w = h * imageAspect;
            }
            else
            {
                w = viewportW;
                h = w / imageAspect;
            }
        }
        else if (imageAspect > viewAspect)
        {
            w = viewportW;
            h = w / imageAspect;
        }
        else
        {
            h = viewportH;
            w = h * imageAspect;
        }

        return ((viewportW - w) / 2f, (viewportH - h) / 2f, w, h);
    }

    public static ushort FloatToHalf(float value)
    {
        var bits = BitConverter.SingleToInt32Bits(value);
        var sign = (bits >> 16) & 0x8000;
        var exp = (bits >> 23) & 0xFF;
        var mant = bits & 0x007FFFFF;

        if (exp == 255)
        {
            return (ushort)(sign | 0x7C00 | (mant != 0 ? 0x200 : 0));
        }

        var unbiased = exp - 127 + 15;
        if (unbiased >= 31)
        {
            return (ushort)(sign | 0x7C00);
        }

        if (unbiased <= 0)
        {
            if (unbiased < -10)
            {
                return (ushort)sign;
            }

            mant |= 0x00800000;
            var halfMant = mant >> (1 - unbiased + 13);
            return (ushort)(sign | halfMant);
        }

        return (ushort)(sign | (unbiased << 10) | (mant >> 13));
    }
}
