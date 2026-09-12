using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Overlay details, HDR status, dest-rect, and tonemap math. Off WinUI.
/// </summary>
public static class GalleryPresent
{
    public const float SdrReferenceNits = 203f;
    public const float ScrgbNits = 80f;
    public const float MinPeakNits = 80f;
    public const float MaxPeakNits = 4000f;

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

    public static string? StatusLine(
        HdrProbe probe,
        bool presented,
        bool displayHdr,
        bool peakOverride = false,
        float peakNits = 0)
    {
        if (probe.Kind == HdrKind.HdrPng && presented && peakOverride)
        {
            return $"HDR PNG · clip {ClampPeakNits(peakNits):0} nits (override)";
        }

        return probe.Kind switch
        {
            HdrKind.UltraHdrJpeg => "Ultra HDR JPEG — showing the SDR base",
            HdrKind.HdrPng when presented && displayHdr => "HDR PNG · presenting scRGB",
            HdrKind.HdrPng when presented => "HDR PNG · tonemapped to the display (clip peak)",
            HdrKind.HdrPng => "HDR PNG — SDR preview",
            HdrKind.WideGamutPng => "Wide-gamut PNG",
            _ => null
        };
    }

    public static float ClampPeakNits(float nits) =>
        Math.Clamp(nits, MinPeakNits, MaxPeakNits);

    /// <summary>
    /// DXGI MaxLuminance is often wrong. Override is opt-in; auto stays the
    /// clip-peak path that matches the SDR twin.
    /// </summary>
    public static float EffectivePeakNits(float autoPeakNits, bool overrideEnabled, float overrideNits)
    {
        if (overrideEnabled)
        {
            return ClampPeakNits(overrideNits);
        }

        return autoPeakNits > 0 ? autoPeakNits : SdrReferenceNits;
    }

    public static string PeakNitsLabel(float nits) =>
        $"{ClampPeakNits(nits):0} nits";

    /// <summary>
    /// SKIV-style info box: file / size / resolution / color / luminance.
    /// No Save As, Export, Copy, gamut triangle, or output-format radios.
    /// </summary>
    public static string ImageInfoText(GalleryImageInfo info)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(info.FileName))
        {
            lines.Add($"Image: {info.FileName.Trim()}");
        }

        if (info.FileSize is >= 0)
        {
            lines.Add($"File size: {FormatSize(info.FileSize)}");
        }

        if (info.Width is > 0 && info.Height is > 0)
        {
            lines.Add($"Resolution: {info.Width}×{info.Height}");
        }

        var color = ColorLabel(info.Probe);
        if (color is not null)
        {
            lines.Add($"Color: {color}");
        }

        if (!string.IsNullOrWhiteSpace(info.HdrStatus))
        {
            lines.Add($"HDR: {info.HdrStatus.Trim()}");
        }

        if (info.Probe.MaxCllNits is > 0)
        {
            lines.Add($"MaxCLL: {FormatNits(info.Probe.MaxCllNits.Value)}");
        }

        if (info.MaxLuminanceNits is > 0)
        {
            lines.Add($"Max luminance: {FormatNits(info.MaxLuminanceNits.Value)}");
        }

        if (info.AvgLuminanceNits is >= 0 && info.MaxLuminanceNits is > 0)
        {
            lines.Add($"Avg luminance: {FormatNits(info.AvgLuminanceNits.Value)}");
        }

        if (info.MinLuminanceNits is >= 0 && info.MaxLuminanceNits is > 0)
        {
            lines.Add($"Min luminance: {FormatNits(info.MinLuminanceNits.Value)}");
        }

        if (info.DisplayPeakNits is > 0)
        {
            lines.Add($"Display peak: {FormatNits(info.DisplayPeakNits.Value)}");
        }

        return string.Join('\n', lines);
    }

    public static string? ColorLabel(HdrProbe probe)
    {
        var primaries = probe.CicpPrimaries switch
        {
            9 => "BT.2020",
            12 => "Display P3",
            1 or 6 => "BT.709",
            _ => null
        };
        var transfer = probe.Transfer switch
        {
            HdrTransfer.Pq => "PQ",
            HdrTransfer.Hlg => "HLG",
            HdrTransfer.Linear => "linear",
            _ when probe.CicpTransfer is 13 => "sRGB",
            _ when probe.IsHdr => "sRGB",
            _ => null
        };
        if (primaries is null && transfer is null)
        {
            return null;
        }

        if (primaries is null)
        {
            return transfer;
        }

        return transfer is null ? primaries : $"{primaries} · {transfer}";
    }

    public static string FormatNits(float nits) =>
        $"{nits:0.#} nits";

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

    /// <summary>
    /// Overlay present clips highlights to the display peak. Do not scale
    /// midtones by MaxCLL — that crushes SDR-reference white (203 nits)
    /// versus the BitmapImage / SDR export of the same scene.
    /// </summary>
    public static float ClipToPeak(float linearNits, float displayPeakNits)
    {
        if (linearNits < 0)
        {
            return 0;
        }

        return linearNits > displayPeakNits ? displayPeakNits : linearNits;
    }

    public static float EncodedToNits(float encoded, HdrTransfer transfer) =>
        transfer switch
        {
            HdrTransfer.Pq => PqEotf(encoded) * 10000f,
            HdrTransfer.Hlg => HlgEotf(encoded) * 1000f,
            HdrTransfer.Linear => encoded * SdrReferenceNits,
            _ => SrgbEotf(encoded) * SdrReferenceNits
        };

    public static float SrgbEotf(float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        if (v <= 0.04045f)
        {
            return v / 12.92f;
        }

        return (float)Math.Pow((v + 0.055) / 1.055, 2.4);
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

        // Actual is 1 device pixel per image pixel. The swapchain is sized
        // to that (DIPs = pixels / raster). Do not stretch-fill a leftover
        // viewport-sized buffer — that is what made 1:1 look stretched.
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

public readonly record struct GalleryImageInfo(
    string? FileName,
    long? FileSize,
    int? Width,
    int? Height,
    HdrProbe Probe,
    string? HdrStatus,
    float? MaxLuminanceNits,
    float? AvgLuminanceNits,
    float? MinLuminanceNits,
    float? DisplayPeakNits);
