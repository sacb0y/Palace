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
        if (probe.CanPresentHdr && presented && peakOverride && displayHdr)
        {
            return $"{HdrKindLabel(probe.Kind)} · clip {ClampPeakNits(peakNits):0} nits (override)";
        }

        if (probe.CanPresentHdr && presented && !displayHdr)
        {
            _ = peakOverride;
            _ = peakNits;
            return $"{HdrKindLabel(probe.Kind)} · tonemap SDR {ScrgbNits:0} nits";
        }

        return probe.Kind switch
        {
            HdrKind.UltraHdrJpeg => "Ultra HDR JPEG — showing the SDR base",
            HdrKind.HdrPng or HdrKind.HdrAvif or HdrKind.HdrHeif or HdrKind.HdrJxr
                or HdrKind.HdrJxl or HdrKind.HdrRadiance when presented =>
                $"{HdrKindLabel(probe.Kind)} · presenting scRGB",
            HdrKind.HdrPng or HdrKind.HdrAvif or HdrKind.HdrHeif or HdrKind.HdrJxr
                or HdrKind.HdrJxl or HdrKind.HdrRadiance =>
                $"{HdrKindLabel(probe.Kind)} — SDR preview",
            HdrKind.WideGamutPng => "Wide-gamut PNG",
            HdrKind.WideGamutAvif => "Wide-gamut AVIF",
            HdrKind.WideGamutHeif => "Wide-gamut HEIF",
            HdrKind.WideGamutJxl => "Wide-gamut JPEG XL",
            _ => null
        };
    }

    public static string HdrKindLabel(HdrKind kind) =>
        kind switch
        {
            HdrKind.HdrPng => "HDR PNG",
            HdrKind.HdrAvif => "HDR AVIF",
            HdrKind.HdrHeif => "HDR HEIF",
            HdrKind.HdrJxr => "HDR JPEG XR",
            HdrKind.HdrJxl => "HDR JPEG XL",
            HdrKind.HdrRadiance => "Radiance HDR",
            HdrKind.WideGamutPng => "Wide-gamut PNG",
            HdrKind.WideGamutAvif => "Wide-gamut AVIF",
            HdrKind.WideGamutHeif => "Wide-gamut HEIF",
            HdrKind.WideGamutJxl => "Wide-gamut JPEG XL",
            HdrKind.UltraHdrJpeg => "Ultra HDR JPEG",
            _ => "HDR"
        };

    public static float ClampPeakNits(float nits) =>
        Math.Clamp(nits, MinPeakNits, MaxPeakNits);

    /// <summary>
    /// 203 nits is BT.2408 reference white, not a display peak. Auto uses
    /// the DXGI luminance when we have one; 0 means do not clip to 203.
    /// Override is opt-in.
    /// </summary>
    public static float EffectivePeakNits(float autoPeakNits, bool overrideEnabled, float overrideNits)
    {
        if (overrideEnabled)
        {
            return ClampPeakNits(overrideNits);
        }

        return autoPeakNits > 0 ? autoPeakNits : 0;
    }

    /// <summary>
    /// Prefer DXGI full-frame luminance (SKIV “display luminance”) when it
    /// looks like a real HDR peak. Dummy 270 is unknown (Windows HDR-on
    /// lie) — return 0 so rasterize uses 10 000, not 203 paper white.
    /// </summary>
    public static float ProbedDisplayLuminance(float maxLuminance, float maxFullFrameLuminance)
    {
        if (IsHdrDisplay(maxFullFrameLuminance)
            && !IsDummySdrLuminance(maxFullFrameLuminance)
            && maxFullFrameLuminance <= 10000)
        {
            return maxFullFrameLuminance;
        }

        if (IsHdrDisplay(maxLuminance)
            && !IsDummySdrLuminance(maxLuminance)
            && maxLuminance <= 10000)
        {
            return maxLuminance;
        }

        return 0;
    }

    /// <summary>
    /// 203 nits is BT.2408 reference / paper white, not a display peak.
    /// </summary>
    public static bool IsHdrDisplay(float displayLuminance) =>
        displayLuminance > 220;

    /// <summary>
    /// DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709 (scRGB), G2084 HDR10, studio
    /// HDR10 / HLG. G22 sRGB (0) is not advanced color by itself — desktop
    /// composition often stays G22 while Windows HDR is on.
    /// </summary>
    public static bool IsAdvancedColor(int dxgiColorSpace) =>
        dxgiColorSpace is 1 or 12 or 13 or 16 or 18 or 20 or 21;

    /// <summary>
    /// Dummy SDR EDID peak DXGI reports as 270 nits (8-bit G22).
    /// </summary>
    public static bool IsDummySdrLuminance(float nits) =>
        nits > 265 && nits < 275;

    /// <summary>
    /// Windows HDR vs SDR for present. <paramref name="windowsHdrEnabled"/>
    /// true is DisplayConfig HDR (<c>AdvancedColorEnabled</c> / INFO_2).
    /// DXGI ColorSpace often stays G22 with dummy 270 while that toggle is
    /// on — that is HDR, not SDR tonemap. Peak &gt;220 (including dummy
    /// 270) is HDR even when CCD parse fails. 203 paper white and 80–220
    /// stay SDR. Unknown dummy peak does not clip to 203.
    /// </summary>
    public static bool IsHdrOutput(
        int dxgiColorSpace,
        float maxLuminance,
        float maxFullFrameLuminance,
        int bitsPerColor,
        bool? windowsHdrEnabled = null)
    {
        _ = bitsPerColor;
        if (windowsHdrEnabled == true)
        {
            return true;
        }

        if (IsAdvancedColor(dxgiColorSpace))
        {
            return true;
        }

        var raw = Math.Max(maxLuminance, maxFullFrameLuminance);
        return IsHdrDisplay(raw);
    }

    public static string FormatDisplayProbe(
        int colorSpace,
        float maxLuminance,
        float maxFullFrameLuminance,
        int bitsPerColor,
        bool? windowsHdrEnabled,
        bool displayHdr,
        string? ccd = null)
    {
        var win = windowsHdrEnabled is null ? "?" : windowsHdrEnabled.Value ? "1" : "0";
        var line =
            $"DXGI cs={colorSpace} max={maxLuminance:0} ff={maxFullFrameLuminance:0} bits={bitsPerColor} winHdr={win} displayHdr={(displayHdr ? 1 : 0)}";
        if (!string.IsNullOrWhiteSpace(ccd))
        {
            line += " · " + ccd.Trim();
        }

        return line;
    }

    /// <summary>
    /// WinRT <c>BitmapPixelFormat</c> has no float. These unorm layouts
    /// clamp HDR JXR (linear scRGB) to 1.0 / 80 nits — do not present
    /// them as scRGB; run native float/half <c>CopyPixels</c> instead.
    /// </summary>
    public static bool JxrWinrtClampsHdr(HdrPackedFormat format) =>
        format is HdrPackedFormat.Rgba16 or HdrPackedFormat.Rgba8 or HdrPackedFormat.Bgra8;

    /// <summary>
    /// When DXGI / CCD cannot be read, PQ / HLG / HDR JPEG XR still present
    /// scRGB at unknown peak (10 000), not 80-nit SDR tonemap.
    /// </summary>
    public static bool UnknownDisplayPresentsHdr(HdrProbe probe)
    {
        if (!probe.CanPresentHdr)
        {
            return false;
        }

        return probe.IsPq
            || probe.IsHlg
            || probe.Transfer is HdrTransfer.Pq or HdrTransfer.Hlg
            || probe.Kind is HdrKind.HdrJxr or HdrKind.HdrRadiance;
    }

    public static string FormatUnknownDisplayProbe(string reason, bool assumeHdr, string? ccd = null)
    {
        var line = $"DXGI {reason} · assumeHdr={(assumeHdr ? 1 : 0)} displayHdr={(assumeHdr ? 1 : 0)}";
        if (!string.IsNullOrWhiteSpace(ccd))
        {
            line += " · " + ccd.Trim();
        }

        return line;
    }

    /// <summary>
    /// IDXGISwapChain3::CheckColorSpaceSupport PRESENT bit.
    /// </summary>
    public static bool ColorSpaceSupportsPresent(uint supportFlags) =>
        (supportFlags & 1) != 0;

    /// <summary>
    /// G22 DWM composition white is scRGB 1.0 (80 nits). DXGI EDID luminance
    /// (dummy 270, 80–220 panel ads, BT.2408 203) is not the map target.
    /// </summary>
    public static float SdrPresentPeakNits(float maxLuminance, float maxFullFrameLuminance)
    {
        _ = maxLuminance;
        _ = maxFullFrameLuminance;
        return ScrgbNits;
    }

    /// <summary>MaxCLL in nits: scRGB channel peak, else CIE Y.</summary>
    public static float ContentMaxNits(float maxScrgb, float cieYNits)
    {
        if (maxScrgb > 0)
        {
            return maxScrgb * ScrgbNits;
        }

        return cieYNits > 0 ? cieYNits : 0;
    }

    /// <summary>
    /// SDR map peak must not follow the current decode size. Viewport Fit
    /// often sees a lower MaxScrgb than native 1:1; keep the highest known
    /// so Fit and 1:1 share one scale. Never shrink after a native peak.
    /// </summary>
    public static float StickyContentMaxNits(float currentNits, float knownNits)
    {
        if (currentNits < 0)
        {
            currentNits = 0;
        }

        if (knownNits < 0)
        {
            knownNits = 0;
        }

        return currentNits > knownNits ? currentNits : knownNits;
    }

    /// <summary>
    /// Pixel CLL, plus header cLLi as a decode-size-independent floor so
    /// the first Fit paint is not weaker than later 1:1. Info MaxCLL stays
    /// the pixel peak (not cLLi).
    /// </summary>
    public static float StickyContentMaxNits(
        float frameMaxScrgb,
        float frameMaxNits,
        int? probeMaxCllNits,
        float knownNits)
    {
        var current = ContentMaxNits(frameMaxScrgb, frameMaxNits);
        var probe = probeMaxCllNits is > 0 ? probeMaxCllNits.Value : 0f;
        return StickyContentMaxNits(current, StickyContentMaxNits(probe, knownNits));
    }

    /// <summary>
    /// Sticky peak is per still. Next/prev must not keep a prior MaxCLL
    /// (that would crush a dimmer HDR still). Same path (Fit ↔ 1:1) keeps
    /// the highest known.
    /// </summary>
    public static float StickyContentMaxNitsForStill(
        string? framePath,
        string? knownPath,
        float frameMaxScrgb,
        float frameMaxNits,
        int? probeMaxCllNits,
        float knownNits)
    {
        var known = !string.IsNullOrEmpty(framePath)
            && string.Equals(framePath, knownPath, StringComparison.OrdinalIgnoreCase)
            ? knownNits
            : 0f;
        return StickyContentMaxNits(frameMaxScrgb, frameMaxNits, probeMaxCllNits, known);
    }

    /// <summary>
    /// HDR panels: clip only (unknown peak → PQ 10 000). SDR: SKIV map
    /// CLL to G22 composition white (scRGB 1.0 / 80 nits) + clip. Never
    /// auto-clip to 203 paper white, and never map to an EDID 80–220 peak
    /// (that would present scRGB above 1 on G22 and look overblown).
    /// </summary>
    public static HdrPresentMap PresentMap(bool displayIsHdr, float contentMaxNits, float displayPeakNits)
    {
        if (displayIsHdr)
        {
            return new HdrPresentMap(1f, RasterizeClipScrgb(displayPeakNits));
        }

        _ = displayPeakNits;
        return new HdrPresentMap(TonemapScale(contentMaxNits, ScrgbNits), 1f);
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

        if (info.MaxScrgb is > 0)
        {
            lines.Add($"MaxCLL (scRGB): {info.MaxScrgb.Value:0.###}");
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

        if (info.DisplayLuminanceNits is > 0)
        {
            lines.Add($"Display luminance: {FormatNits(info.DisplayLuminanceNits.Value)}");
        }

        if (!string.IsNullOrWhiteSpace(info.DisplayProbe))
        {
            lines.Add($"Display: {info.DisplayProbe.Trim()}");
        }

        return string.Join('\n', lines);
    }

    public static string? ColorLabel(HdrProbe probe)
    {
        var primaries = probe.CicpPrimaries switch
        {
            9 => "BT.2020",
            11 => "DCI-P3",
            12 => "Display P3",
            10 => "XYZ",
            5 or 6 => "BT.601",
            1 or 2 => "BT.709",
            _ => null
        };
        var transfer = probe.Transfer switch
        {
            HdrTransfer.Pq => "PQ",
            HdrTransfer.Hlg => "HLG",
            HdrTransfer.Linear => "linear",
            HdrTransfer.Scrgb => "scRGB",
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

    /// <summary>
    /// CIE Y in the source primaries. Do not use max(RGB) after 2020→709 —
    /// that inflates “max luminance” (2572 vs SKIV ~1682 on the same file).
    /// </summary>
    public static float LuminanceY(float rNits, float gNits, float bNits, bool bt2020) =>
        bt2020
            ? (0.2627f * rNits) + (0.6780f * gNits) + (0.0593f * bNits)
            : (0.2126f * rNits) + (0.7152f * gNits) + (0.0722f * bNits);

    /// <summary>
    /// File pixels: oriented decode, then header, then catalog. Never a
    /// 512 thumb when the header is 3840×2160.
    /// </summary>
    public static (int Width, int Height)? FilePixelSize(
        int? decodedWidth,
        int? decodedHeight,
        int? headerWidth,
        int? headerHeight,
        int? catalogWidth,
        int? catalogHeight)
    {
        if (decodedWidth is > 0 && decodedHeight is > 0)
        {
            return (decodedWidth.Value, decodedHeight.Value);
        }

        if (headerWidth is > 0 && headerHeight is > 0)
        {
            return (headerWidth.Value, headerHeight.Value);
        }

        if (catalogWidth is > 0 && catalogHeight is > 0)
        {
            return (catalogWidth.Value, catalogHeight.Value);
        }

        return null;
    }

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
    /// HDR-display present clips highlights to a real peak (or PQ range).
    /// Do not map-CLL on HDR — that crushes BT.2408 203 nits white. SDR
    /// panels use <see cref="PresentMap"/> (map CLL to display + clip).
    /// </summary>
    public static float ClipToPeak(float linearNits, float displayPeakNits)
    {
        if (linearNits < 0)
        {
            return 0;
        }

        if (displayPeakNits <= 0)
        {
            return linearNits;
        }

        return linearNits > displayPeakNits ? displayPeakNits : linearNits;
    }

    /// <summary>
    /// scRGB clip for rasterize. Unknown display peak (0) means do not clip
    /// to paper white — use 10 000 nits (PQ range).
    /// </summary>
    public static float RasterizeClipScrgb(float displayPeakNits)
    {
        var peak = displayPeakNits > 0 ? displayPeakNits : 10000f;
        return peak / ScrgbNits;
    }

    public const int FastPresentLongEdge = 2048;

    /// <summary>
    /// WIC decode size for first paint. Fit/Fill use the viewport, not the
    /// full 4K buffer. Actual is 1:1 native. Unknown layout caps the long
    /// edge so PQ convert does not block Open.
    /// </summary>
    public static (int Width, int Height) PresentDecodeSize(
        int imageW,
        int imageH,
        int viewportW,
        int viewportH,
        ImageScaling scaling)
    {
        if (imageW <= 0 || imageH <= 0)
        {
            return (0, 0);
        }

        if (scaling == ImageScaling.Actual)
        {
            return (imageW, imageH);
        }

        int destW;
        int destH;
        if (viewportW >= 2 && viewportH >= 2)
        {
            var (_, _, dw, dh) = DestRect(scaling, imageW, imageH, viewportW, viewportH);
            destW = Math.Max(1, (int)Math.Ceiling(dw));
            destH = Math.Max(1, (int)Math.Ceiling(dh));
        }
        else
        {
            var longEdge = Math.Max(imageW, imageH);
            if (longEdge <= FastPresentLongEdge)
            {
                return (imageW, imageH);
            }

            var scale = FastPresentLongEdge / (double)longEdge;
            destW = Math.Max(1, (int)Math.Round(imageW * scale));
            destH = Math.Max(1, (int)Math.Round(imageH * scale));
        }

        return (Math.Min(destW, imageW), Math.Min(destH, imageH));
    }

    /// <summary>
    /// Decode size for post-present CIE Y / MaxCLL. Always the viewport /
    /// <see cref="FastPresentLongEdge"/> Fit cap — never a native 16384²
    /// buffer. Math stays CIE Y in source primaries and MaxCLL scRGB.
    /// </summary>
    public static (int Width, int Height) MeasureDecodeSize(
        int imageW,
        int imageH,
        int viewportW,
        int viewportH) =>
        PresentDecodeSize(imageW, imageH, viewportW, viewportH, ImageScaling.Fit);

    public static bool NeedsBetterDecode(int haveW, int haveH, int wantW, int wantH) =>
        wantW > 0 && wantH > 0 && (haveW < wantW || haveH < wantH);

    public static bool IsNativeDecode(int decodedW, int decodedH, int nativeW, int nativeH) =>
        nativeW > 0 && nativeH > 0 && decodedW >= nativeW && decodedH >= nativeH;

    /// <summary>
    /// Authoritative CIE Y / MaxCLL from the measure / native frame must
    /// survive Fit/peak re-present of a viewport <c>HdrFrame</c>.
    /// </summary>
    public static bool ReplaceHdrStats(bool haveNative, bool incomingNative) =>
        !haveNative || incomingNative;

    /// <summary>
    /// Do not reuse a cancelled CTS. RefreshAsync cancels the previous
    /// source, then a cache-hit measure must mint a live one.
    /// </summary>
    public static CancellationTokenSource LiveTokenSource(CancellationTokenSource? current)
    {
        if (current is { IsCancellationRequested: false })
        {
            return current;
        }

        current?.Dispose();
        return new CancellationTokenSource();
    }

    public static float TransferToLinear01(float encoded, HdrTransfer transfer) =>
        transfer switch
        {
            HdrTransfer.Pq => PqEotf(encoded),
            HdrTransfer.Hlg => HlgEotf(encoded),
            HdrTransfer.Linear or HdrTransfer.Scrgb => encoded,
            _ => SrgbEotf(encoded)
        };

    public static float EncodedToNits(float encoded, HdrTransfer transfer) =>
        transfer switch
        {
            HdrTransfer.Pq => PqEotf(encoded) * 10000f,
            HdrTransfer.Hlg => HlgEotf(encoded) * 1000f,
            HdrTransfer.Scrgb => encoded * ScrgbNits,
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

    /// <summary>
    /// Inverse of <see cref="Bt2020ToBt709"/> / SKIV <c>c_Bt2100toscRGB</c>.
    /// </summary>
    public static void Bt709ToBt2020(float r709, float g709, float b709, out float r, out float g, out float b)
    {
        r = (0.627404f * r709) + (0.329283f * g709) + (0.043313f * b709);
        g = (0.069097f * r709) + (0.919540f * g709) + (0.011362f * b709);
        b = (0.016392f * r709) + (0.088013f * g709) + (0.895595f * b709);
    }

    /// <summary>
    /// CIE Y for a presented scRGB pixel. <c>HdrFrame.ScrgbRgba</c> is already
    /// display 709; Info Y is source primaries before 2020→709. Invert that
    /// matrix whenever <see cref="HdrColor.UsesBt2100ToScrgbMatrix"/> (BT.2020,
    /// Display P3 12, unspecified / missing <c>colr</c>) — do not run BT.2020
    /// <see cref="LuminanceY"/> on the display-referred channels. 2020 weights
    /// stay only when CICP primaries are 9, matching Info.
    /// </summary>
    public static float LumaNitsFromPresentedScrgb(float sr, float sg, float sb, int? cicpPrimaries)
    {
        var r = sr * ScrgbNits;
        var g = sg * ScrgbNits;
        var b = sb * ScrgbNits;
        if (HdrColor.UsesBt2100ToScrgbMatrix(cicpPrimaries))
        {
            Bt709ToBt2020(r, g, b, out r, out g, out b);
            return LuminanceY(r, g, b, cicpPrimaries == 9);
        }

        return LuminanceY(r, g, b, false);
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
    float? DisplayLuminanceNits,
    float? MaxScrgb = null,
    string? DisplayProbe = null);

public readonly record struct HdrPresentMap(float Scale, float ClipScrgb);
