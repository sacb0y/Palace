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

    /// <summary>
    /// Stills that must never open via <c>BitmapImage</c> of the original
    /// (WIC Store codec / blank). Show Palace's cached large preview or mosaic
    /// thumb instead — includes JPEG XL so PQ/HLG is not washed or ~50% clipped.
    /// </summary>
    public static bool RequiresCachedStillPreview(string? path) =>
        StillDecode.HandlesPath(path) || PathSafe.IsJxl(path);

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
                or HdrKind.HdrJxl or HdrKind.HdrRadiance or HdrKind.HdrExr when presented =>
                $"{HdrKindLabel(probe.Kind)} · presenting scRGB",
            HdrKind.HdrPng or HdrKind.HdrAvif or HdrKind.HdrHeif or HdrKind.HdrJxr
                or HdrKind.HdrJxl or HdrKind.HdrRadiance or HdrKind.HdrExr =>
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
            HdrKind.HdrExr => "OpenEXR",
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
    /// on — that is HDR, not SDR tonemap. Explicit Windows HDR <b>off</b>:
    /// dummy 270 alone is SDR (Reinhard shoulder) — otherwise SDR laptops
    /// skip the shoulder and HDR JXL looks washed. When CCD is unknown
    /// (<c>null</c>), peak &gt;220 including dummy 270 stays HDR so Fenrir-
    /// class panels are not false-tonemapped. 203 paper white and 80–220
    /// stay SDR.
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
        // Explicit ACE=0: DXGI dummy 270 is an SDR EDID lie — apply Reinhard.
        if (windowsHdrEnabled == false && IsDummySdrLuminance(raw))
        {
            return false;
        }

        return IsHdrDisplay(raw);
    }

    /// <summary>DXGI_FORMAT_R16G16B16A16_FLOAT — HDR still swapchain.</summary>
    public const int ScrgbSwapChainFormat = 10;

    /// <summary>DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709 — linear scRGB.</summary>
    public const int ScrgbColorSpace = 1;

    /// <summary>
    /// <c>IDXGISwapChain2::SetMatrixTransform</c> vtable slot (IUnknown=0).
    /// Layout: …31 GetMaximumFrameLatency, 32–33 Set/GetSourceSize,
    /// 34 SetMatrixTransform, 35 GetMatrixTransform; SwapChain3
    /// SetColorSpace1 is 38.
    /// </summary>
    public const int DxgiSwapChain2SetMatrixTransformVtbl = 34;

    public static string FormatDisplayProbe(
        int colorSpace,
        float maxLuminance,
        float maxFullFrameLuminance,
        int bitsPerColor,
        bool? windowsHdrEnabled,
        bool displayHdr,
        string? ccd = null,
        int? swapChainFormat = null,
        int? swapChainColorSpace = null,
        float sdrWhiteLevelNits = 0f,
        float compositionScaleX = 0f,
        float compositionScaleY = 0f)
    {
        var win = windowsHdrEnabled is null ? "?" : windowsHdrEnabled.Value ? "1" : "0";
        var line =
            $"DXGI cs={colorSpace} max={maxLuminance:0} ff={maxFullFrameLuminance:0} bits={bitsPerColor} winHdr={win} displayHdr={(displayHdr ? 1 : 0)}";
        if (sdrWhiteLevelNits > 0 && float.IsFinite(sdrWhiteLevelNits))
        {
            line += $" sdrWhite={sdrWhiteLevelNits:0.#}";
        }

        if (compositionScaleX > 0 && compositionScaleY > 0)
        {
            line += $" compScale={compositionScaleX:0.###}x{compositionScaleY:0.###}";
        }

        if (swapChainFormat.HasValue || swapChainColorSpace.HasValue)
        {
            var f = swapChainFormat ?? ScrgbSwapChainFormat;
            var c = swapChainColorSpace ?? ScrgbColorSpace;
            line += $" · swap={FormatSwapChainLabel(f, c)}";
        }

        if (!string.IsNullOrWhiteSpace(ccd))
        {
            line += " · " + ccd.Trim();
        }

        return line;
    }

    /// <summary>
    /// DXGI swapchain buffer size in <b>physical</b> pixels for a
    /// <c>SwapChainPanel</c>. Panel Width/Height are DIPs; Windows
    /// composition also applies <paramref name="compositionScaleX"/> /
    /// <paramref name="compositionScaleY"/>. Buffer = round(DIP × scale)
    /// and pair with <c>IDXGISwapChain2::SetMatrixTransform(1/scale)</c>
    /// so the panel does not double-scale (classic zoomed/cropped HDR Fit).
    /// Prefer <c>CompositionScaleX/Y</c> over <c>XamlRoot.RasterizationScale</c>
    /// alone — they can differ under DPI or RenderTransform.
    /// </summary>
    public static (int Width, int Height) SwapchainPhysicalSize(
        double dipWidth,
        double dipHeight,
        double compositionScaleX,
        double compositionScaleY)
    {
        var sx = compositionScaleX > 0 && double.IsFinite(compositionScaleX)
            ? compositionScaleX
            : 1.0;
        var sy = compositionScaleY > 0 && double.IsFinite(compositionScaleY)
            ? compositionScaleY
            : 1.0;
        var w = Math.Max(1, (int)Math.Round(Math.Max(dipWidth, 0) * sx));
        var h = Math.Max(1, (int)Math.Round(Math.Max(dipHeight, 0) * sy));
        return (w, h);
    }

    /// <summary>
    /// True when Fit rasterization into a Fit-sized swapchain buffer maps the
    /// full source frame (no zoom crop). Used for SDR present of HDR stills
    /// (EXR / AVIF / JXL share this path).
    /// </summary>
    public static bool FitShowsFullFrame(
        int imageW,
        int imageH,
        double viewportDipW,
        double viewportDipH,
        double compositionScaleX = 1.0,
        double compositionScaleY = 1.0)
    {
        if (imageW <= 0 || imageH <= 0 || viewportDipW <= 1 || viewportDipH <= 1)
        {
            return false;
        }

        var (dipW, dipH) = GalleryScale.FitContainDipSize(
            imageW, imageH, viewportDipW, viewportDipH);
        if (double.IsNaN(dipW) || double.IsNaN(dipH) || dipW <= 1 || dipH <= 1)
        {
            return false;
        }

        var (bw, bh) = SwapchainPhysicalSize(dipW, dipH, compositionScaleX, compositionScaleY);
        var (dx, dy, dw, dh) = DestRect(ImageScaling.Fit, imageW, imageH, bw, bh);
        // Same-aspect Fit panel → dest fills the buffer (full UV coverage).
        return Math.Abs(dx) < 1.5f
            && Math.Abs(dy) < 1.5f
            && Math.Abs(dw - bw) < 1.5f
            && Math.Abs(dh - bh) < 1.5f;
    }

    /// <summary>
    /// Human label for the composition swapchain used by HDR still present
    /// (format 10 = R16G16B16A16_FLOAT, color space 1 = scRGB G10 P709).
    /// </summary>
    public static string FormatSwapChainLabel(int format, int colorSpace)
    {
        var fmt = format == ScrgbSwapChainFormat ? "R16G16B16A16_FLOAT" : $"fmt={format}";
        var cs = colorSpace == ScrgbColorSpace ? "scRGB(G10/P709)" : $"cs={colorSpace}";
        return $"{fmt}/{cs}";
    }

    /// <summary>
    /// WinRT <c>BitmapPixelFormat</c> has no float (only Rgba16/Rgba8/Bgra8
    /// unorm, etc.). Those clamp HDR JXR (linear scRGB) to 1.0 / 80 nits —
    /// do not present them as scRGB. Packaged apps also cannot activate
    /// <c>CLSID_WICWmpDecoder</c> for native float CopyPixels
    /// (<c>wmp</c>/<c>qi</c> <c>E_NOINTERFACE</c>, <c>rasDecoder</c>
    /// <c>E_FAIL</c>) — that is a packaging wall, not another CLSID retry.
    /// <c>BitmapImage</c> UriSource is the same SDR pipeline.
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
            || probe.Kind is HdrKind.HdrJxr or HdrKind.HdrJxl
                or HdrKind.HdrRadiance or HdrKind.HdrExr;
    }

    /// <summary>
    /// When <c>IDXGIOutput6</c> / luminance is missing: Windows HDR (CCD) or
    /// PQ/HLG/HDR-still content still presents scRGB — never false-tonemap to
    /// 80 nits while Advanced Color is on (FenrirDesktop / SAM713F class).
    /// </summary>
    public static bool DisplayHdrWithoutOutput6(bool? windowsHdrEnabled, HdrProbe content) =>
        windowsHdrEnabled == true || UnknownDisplayPresentsHdr(content);

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
    /// <summary>Headroom above the P99 max-RGB that the SDR map keeps before clipping.</summary>
    public const float SdrMapP99Headroom = 2f;

    /// <summary>
    /// SDR map peak: MaxCLL, capped at <see cref="SdrMapP99Headroom"/> × the
    /// P99 max-RGB so a tiny extreme highlight (the sun in a 130 000-nit
    /// panorama) does not scale the whole frame to black. Typical HDR stills
    /// (P99 within half of MaxCLL) keep the SKIV MaxCLL map unchanged.
    /// </summary>
    public static float SdrMapPeakNits(float contentMaxNits, float p99MaxRgbNits)
    {
        if (contentMaxNits <= 0 || p99MaxRgbNits <= 0 || !float.IsFinite(p99MaxRgbNits))
        {
            return contentMaxNits;
        }

        return Math.Min(contentMaxNits, Math.Max(ScrgbNits, p99MaxRgbNits * SdrMapP99Headroom));
    }

    /// <summary>Sampled P99 of max(R,G,B) in nits over a linear scRGB RGBA buffer.</summary>
    public static float P99MaxRgbNits(float[] rgba, int width, int height, int maxSamples = 65_536)
    {
        if (rgba is null || width <= 0 || height <= 0 || maxSamples <= 0)
        {
            return 0;
        }

        long pixelsL = (long)width * height;
        if (pixelsL > int.MaxValue / 4 || rgba.Length < pixelsL * 4)
        {
            return 0;
        }

        var pixels = (int)pixelsL;
        var stride = Math.Max(1, pixels / maxSamples);
        var values = new float[(pixels + stride - 1) / stride];
        var n = 0;
        for (var i = 0; i < pixels && n < values.Length; i += stride)
        {
            var o = i * 4;
            // Math.Max propagates NaN, so sanitize each channel first.
            static float F(float v) => float.IsFinite(v) && v > 0 ? v : 0f;
            values[n++] = Math.Max(F(rgba[o]), Math.Max(F(rgba[o + 1]), F(rgba[o + 2])));
        }

        return HdrAnalysis.Percentile(values, n, HdrAnalysis.P99Fraction) * ScrgbNits;
    }

    /// <summary>
    /// Effective SDR white in nits for HDR→SDR present.
    /// <c>AdvancedColorInfo.SdrWhiteLevelInNits</c> / CCD when &gt; 0; else
    /// <see cref="ScrgbNits"/> (80).
    /// </summary>
    public static float EffectiveSdrWhiteNits(float sdrWhiteLevelNits) =>
        sdrWhiteLevelNits > 0 && float.IsFinite(sdrWhiteLevelNits)
            ? sdrWhiteLevelNits
            : ScrgbNits;

    /// <summary>
    /// HDR→SDR exposure: <b>1.0</b> by default (scene-referred 1.0 → SDR
    /// white). Absolute scRGB is nits/80 while Windows composition 1.0 =
    /// <see cref="EffectiveSdrWhiteNits"/> — attenuate by 80/SdrWhite only
    /// (never boost above 1).
    /// </summary>
    public static float PresentSdrExposure(float sdrWhiteLevelNits)
    {
        var white = EffectiveSdrWhiteNits(sdrWhiteLevelNits);
        return Math.Min(1f, ScrgbNits / white);
    }

    /// <summary>
    /// Opt-in paper-white factor only (<c>80 / SdrWhiteLevel</c>). The FP16
    /// G10_P709 swapchain is absolute (1.0 = 80 nits) and decoders already
    /// emit nits/80 — default <b>HDR</b> present must <b>not</b> apply this.
    /// SDR Reinhard present uses <see cref="PresentSdrExposure"/> instead.
    /// </summary>
    public static float HdrCompositionScale(float sdrWhiteLevelNits)
    {
        if (sdrWhiteLevelNits <= 0 || !float.IsFinite(sdrWhiteLevelNits))
        {
            return 1f;
        }

        return ScrgbNits / sdrWhiteLevelNits;
    }

    /// <summary>
    /// Opt-in clip in composition units after <see cref="HdrCompositionScale"/>.
    /// Default HDR present uses <see cref="RasterizeClipScrgb"/> instead.
    /// </summary>
    public static float HdrCompositionClip(float sdrWhiteLevelNits, float displayPeakNits)
    {
        var white = sdrWhiteLevelNits > 0 && float.IsFinite(sdrWhiteLevelNits)
            ? sdrWhiteLevelNits
            : ScrgbNits;
        var peak = displayPeakNits > 0 && float.IsFinite(displayPeakNits)
            ? displayPeakNits
            : 10_000f;
        return Math.Max(1f, peak / white);
    }

    /// <summary>
    /// HDR still present: absolute scRGB passthrough (scale 1) + clip at
    /// peak/80 (<see cref="RasterizeClipScrgb"/>). Negatives pass through for
    /// OOG. SDR stills use <see cref="PresentMapSdrReinhard"/> in the gallery
    /// (legacy linear MaxCLL map kept for callers / tests).
    /// <paramref name="sdrWhiteLevelNits"/> is ignored for the default map —
    /// see <see cref="HdrCompositionScale"/>.
    /// </summary>
    public static HdrPresentMap PresentMap(
        bool displayIsHdr,
        float contentMaxNits,
        float displayPeakNits,
        float sdrWhiteLevelNits = 0f)
    {
        _ = sdrWhiteLevelNits;
        if (displayIsHdr)
        {
            return new HdrPresentMap(
                1f,
                RasterizeClipScrgb(displayPeakNits),
                ReinhardWhite: 0f,
                SoftNegatives: true);
        }

        _ = displayPeakNits;
        // Legacy linear MaxCLL→80 for callers that have not run ToneMap.Analyze.
        // Gallery present uses PresentMapSdrReinhard so midtones are not crushed.
        return new HdrPresentMap(TonemapScale(contentMaxNits, ScrgbNits), 1f, SoftNegatives: false);
    }

    /// <summary>
    /// SDR / HDR-off present: extended Reinhard from
    /// <see cref="ToneMap.AnalyzeHdrPresent"/> (exposure ≤ 1 from SdrWhiteLevel,
    /// white from content peak; always shoulder). OOG negatives are
    /// plain-clamped (no SoftNegChannel luminance darkening). Clip is scRGB 1.0.
    /// </summary>
    public static HdrPresentMap PresentMapSdrReinhard(float exposure, float reinhardWhite) =>
        new(
            exposure > 0 && float.IsFinite(exposure) ? Math.Min(exposure, 1f) : 1f,
            1f,
            ReinhardWhite: reinhardWhite > 1f && float.IsFinite(reinhardWhite) ? reinhardWhite : 0f,
            SoftNegatives: false);

    /// <summary>
    /// Same stats as mosaic thumbs (<see cref="ToneMap.Analyze"/> + Reinhard):
    /// exposure may exceed 1 (MidGrey boost). Use for full-view ↔ thumb match
    /// (room.jxl <c>hdr-display</c>+linear). Does not change the curve shape.
    /// On an HDR panel, multiply scale and clip by
    /// <c>SdrWhiteLevel/80</c> so composition white matches the thumb (absolute
    /// scRGB 1.0 = 80 nits; Windows HDR white is SdrWhiteLevel).
    /// </summary>
    public static HdrPresentMap PresentMapThumbMatch(
        float exposure,
        float reinhardWhite,
        bool displayIsHdr = false,
        float sdrWhiteLevelNits = 0f)
    {
        var scale = exposure > 0 && float.IsFinite(exposure) ? exposure : 1f;
        var clip = 1f;
        var white = reinhardWhite > 1f && float.IsFinite(reinhardWhite) ? reinhardWhite : 0f;
        if (displayIsHdr)
        {
            // M1: thumb JPEG is SDR (1.0 = 80 nits). HDR swapchain absolute
            // scRGB needs ×(SdrWhite/80) or the full view is 2.5–3.5× darker.
            var k = EffectiveSdrWhiteNits(sdrWhiteLevelNits) / ScrgbNits;
            if (k > 0 && float.IsFinite(k))
            {
                scale *= k;
                clip *= k;
                if (white > 1f)
                {
                    white *= k;
                }
            }
        }

        return new(scale, clip, ReinhardWhite: white, SoftNegatives: false);
    }

    /// <summary>
    /// Full view must share thumb ToneMap when the panel is SDR, or when
    /// libjxl left <see cref="JxlBufferTransfer.Linear"/> (room.jxl blowout
    /// on clip-only <c>hdr-display</c>). PQ/HLG on HDR stays passthrough.
    /// </summary>
    public static bool PresentMatchesThumbToneMap(
        bool displayIsHdr,
        JxlBufferTransfer bufferTransfer) =>
        !displayIsHdr || bufferTransfer == JxlBufferTransfer.Linear;

    public static string PeakNitsLabel(float nits) =>
        $"{ClampPeakNits(nits):0} nits";

    /// <summary>
    /// SKIV-style info box: file / size / resolution / color / luminance,
    /// plus the <see cref="HdrAnalysis"/> figures (MaxCLL channel, P99,
    /// Palace's FALL, share above SDR white, gamut coverage).
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
            lines.Add(info.MaxCllChannel is 'R' or 'G' or 'B'
                ? $"MaxCLL (scRGB): {info.MaxScrgb.Value:0.###} ({info.MaxCllChannel})"
                : $"MaxCLL (scRGB): {info.MaxScrgb.Value:0.###}");
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

        if (info.P99Nits is > 0)
        {
            lines.Add($"P99 luminance: {FormatNits(info.P99Nits.Value)}");
        }

        if (info.FallNits is > 0)
        {
            // Not a SKIV figure: Palace's frame-average of max(R,G,B).
            lines.Add($"FALL (Palace, avg of max RGB): {FormatNits(info.FallNits.Value)}");
        }

        if (info.AboveSdrWhitePercent is > 0)
        {
            lines.Add($"Above SDR white: {info.AboveSdrWhitePercent.Value:0.#}%");
        }

        if (!string.IsNullOrWhiteSpace(info.Gamut))
        {
            lines.Add($"Gamut: {info.Gamut.Trim()}");
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

    /// <summary>
    /// Info card colour line. EXRs whose chromaticities are not BT.709 read as
    /// "&lt;source&gt; → scRGB" (e.g. "ACEScg → scRGB"): Palace converts them on decode.
    /// <see cref="HdrProbe.SourcePrimaries"/> otherwise overrides the cICP name
    /// (e.g. "Display P3 (ICC) · sRGB" for an ICC-only P3 PNG).
    /// </summary>
    public static string? ColorLabel(HdrProbe probe)
    {
        if (probe.Kind is HdrKind.HdrExr
            && !string.IsNullOrWhiteSpace(probe.SourcePrimaries)
            && probe.SourcePrimaries != "BT.709")
        {
            return $"{probe.SourcePrimaries} → scRGB";
        }

        var primaries = !string.IsNullOrWhiteSpace(probe.SourcePrimaries) ? probe.SourcePrimaries : probe.CicpPrimaries switch
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
    /// Channel letter of the per-channel peaks ('\0' when all ≤ 0). Ties go R, then G,
    /// then B, like <see cref="HdrAnalysis"/>.
    /// </summary>
    public static char PeakChannel(float maxR, float maxG, float maxB) =>
        maxR <= 0 && maxG <= 0 && maxB <= 0 ? '\0'
        : maxR >= maxG && maxR >= maxB ? 'R'
        : maxG >= maxB ? 'G'
        : 'B';

    /// <summary>True when <paramref name="shownPath"/> is the original file, not a thumb or preview.</summary>
    public static bool IsOriginalStill(string? shownPath, string? originalPath)
    {
        if (string.IsNullOrWhiteSpace(shownPath) || string.IsNullOrWhiteSpace(originalPath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                System.IO.Path.GetFullPath(shownPath),
                System.IO.Path.GetFullPath(originalPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

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
        int? catalogHeight,
        bool undecodable = false)
    {
        if (decodedWidth is > 0 && decodedHeight is > 0)
        {
            return (decodedWidth.Value, decodedHeight.Value);
        }

        // Files the scan flagged undecodable (e.g. an unsupported BMP variant) show no
        // resolution unless the gallery itself decoded them: a header or catalog size
        // there is unverified and suggests the image loaded.
        if (undecodable)
        {
            return null;
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
            // HLG: EOTF only here. OOTF is luminance-based — use
            // <see cref="HlgOotfRgb"/> / <see cref="HlgOotfNits"/> (grey).
            HdrTransfer.Hlg => HlgEotf(encoded) * 1000f,
            HdrTransfer.Scrgb => encoded * ScrgbNits,
            HdrTransfer.Linear => encoded * SdrReferenceNits,
            _ => SrgbEotf(encoded) * SdrReferenceNits
        };

    /// <summary>BT.2100 HLG OOTF at 1000-nit peak: <c>γ = 1.2</c>.</summary>
    public const float HlgOotfGamma = 1.2f;

    /// <summary>
    /// Grey / scalar HLG OOTF: scene-linear → display nits with
    /// <c>Y^(γ−1)</c> (γ=1.2, Lw=1000). Prefer <see cref="HlgOotfRgb"/> for RGB.
    /// </summary>
    public static float HlgOotfNits(float sceneLinear)
    {
        var y = Math.Max(sceneLinear, 0f);
        if (y <= 0f)
        {
            return 0f;
        }

        // Rd = Y * Y^(γ−1) * Lw = Y^γ * 1000, Lw = 1000 nits.
        return y * MathF.Pow(y, HlgOotfGamma - 1f) * 1000f;
    }

    /// <summary>
    /// BT.2100 HLG OOTF on luminance (not per channel): scene-linear RGB →
    /// display nits. <paramref name="bt2020"/> selects BT.2020 vs BT.709 Y.
    /// </summary>
    public static void HlgOotfRgb(
        float sceneR,
        float sceneG,
        float sceneB,
        bool bt2020,
        out float nitsR,
        out float nitsG,
        out float nitsB)
    {
        var y = Math.Max(LuminanceY(sceneR, sceneG, sceneB, bt2020), 0f);
        if (y <= 0f)
        {
            nitsR = nitsG = nitsB = 0f;
            return;
        }

        var k = MathF.Pow(y, HlgOotfGamma - 1f) * 1000f;
        nitsR = sceneR * k;
        nitsG = sceneG * k;
        nitsB = sceneB * k;
    }

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

    /// <summary>
    /// PQ OETF on absolute luminance fraction (nits/10000 → encoded 0..1).
    /// Inverse of <see cref="PqEotf"/>.
    /// </summary>
    public static float PqOetf(float linear01)
    {
        const double m1 = 2610.0 / 16384.0;
        const double m2 = 2523.0 / 32.0;
        const double c1 = 3424.0 / 4096.0;
        const double c2 = 2413.0 / 128.0;
        const double c3 = 2392.0 / 128.0;
        var y = Math.Clamp(linear01, 0.0, 1.0);
        var ym = Math.Pow(y, m1);
        return (float)Math.Pow((c1 + c2 * ym) / (1.0 + c3 * ym), m2);
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
    /// The MaxCLL channel letter for Info, or null. Prefer <paramref name="nativeChannel"/>:
    /// the decoder's channel for the displayed (native) peak, e.g. EXR / Radiance stats taken
    /// before the box filter. Otherwise the channel comes from the (possibly box-filtered)
    /// analysis buffer, whose peak is lower than the native one on downscaled files and can
    /// come from another channel, so it is only named when both peaks agree within 0.5%.
    /// </summary>
    public static char? MaxCllChannelFor(
        char channel, float analysisMaxScrgb, float displayedMaxScrgb, char nativeChannel = '\0')
    {
        if (displayedMaxScrgb > 0 && nativeChannel is 'R' or 'G' or 'B')
        {
            return nativeChannel;
        }

        if (channel is not ('R' or 'G' or 'B') || analysisMaxScrgb <= 0 || displayedMaxScrgb <= 0)
        {
            return null;
        }

        return Math.Abs(analysisMaxScrgb - displayedMaxScrgb) <= 0.005f * displayedMaxScrgb ? channel : null;
    }

    /// <summary>Linear Display P3 (D65) → linear BT.709; out-of-709 colours go negative.</summary>
    public static void DisplayP3ToBt709(float r, float g, float b, out float r709, out float g709, out float b709)
    {
        r709 = (1.224940f * r) + (-0.224940f * g);
        g709 = (-0.042057f * r) + (1.042057f * g);
        b709 = (-0.019638f * r) + (-0.078636f * g) + (1.098274f * b);
    }

    /// <summary>CIE Y of linear Display P3 (D65) channels.</summary>
    public static float DisplayP3LuminanceY(float r, float g, float b) =>
        (0.228975f * r) + (0.691739f * g) + (0.079287f * b);

    /// <summary>
    /// Primaries to hand histogram / heatmap / gamut analysis for a presented frame.
    /// The PNG path converts Display P3 (12) exactly to BT.709, unlike the SKIV path
    /// that runs P3 through the BT.2020 matrix, so its luma must invert the exact
    /// conversion: report it as BT.2020 (9), whose inversion returns the same CIE Y.
    /// </summary>
    public static int? AnalysisPrimaries(HdrProbe probe) =>
        (probe.Kind is HdrKind.HdrPng or HdrKind.WideGamutPng) && probe.CicpPrimaries == 12
            ? 9
            : probe.CicpPrimaries;

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
    /// <see cref="LuminanceY"/> on the display-referred channels. After invert,
    /// use <see cref="HdrColor.UsesBt2020Luma"/> so Display P3 matches Info
    /// (same 9||12 rule as HLG OOTF / <see cref="HdrColor.SourcePrimaryNitsY"/>).
    /// </summary>
    public static float LumaNitsFromPresentedScrgb(float sr, float sg, float sb, int? cicpPrimaries)
    {
        var r = sr * ScrgbNits;
        var g = sg * ScrgbNits;
        var b = sb * ScrgbNits;
        if (HdrColor.UsesBt2100ToScrgbMatrix(cicpPrimaries))
        {
            Bt709ToBt2020(r, g, b, out r, out g, out b);
            return LuminanceY(r, g, b, HdrColor.UsesBt2020Luma(cicpPrimaries));
        }

        return LuminanceY(r, g, b, false);
    }

    public static float NitsToScrgb(float nits) => nits / ScrgbNits;

    /// <summary>
    /// HDR / wide-gamut PNG pixel → presented scRGB (linear BT.709, negatives
    /// kept so out-of-709 colours stay out of 709 for gamut coverage).
    /// <paramref name="nitsY"/> is source-primary luminance.
    /// </summary>
    public static void PngEncodedToScrgb(
        float r,
        float g,
        float b,
        HdrTransfer transfer,
        int? cicpPrimaries,
        out float sr,
        out float sg,
        out float sb,
        out float nitsY)
    {
        float nitsR;
        float nitsG;
        float nitsB;
        var bt2020 = cicpPrimaries == 9;
        if (transfer == HdrTransfer.Hlg)
        {
            // EOTF → scene, then luminance OOTF (not per-channel EncodedToNits).
            HlgOotfRgb(HlgEotf(r), HlgEotf(g), HlgEotf(b), bt2020 || cicpPrimaries == 12, out nitsR, out nitsG, out nitsB);
        }
        else
        {
            nitsR = EncodedToNits(r, transfer);
            nitsG = EncodedToNits(g, transfer);
            nitsB = EncodedToNits(b, transfer);
        }

        if (cicpPrimaries == 12)
        {
            // Display P3 (D65): WIC hands over the raw P3 channels, so convert here.
            nitsY = DisplayP3LuminanceY(nitsR, nitsG, nitsB);
            DisplayP3ToBt709(nitsR, nitsG, nitsB, out nitsR, out nitsG, out nitsB);
        }
        else
        {
            nitsY = LuminanceY(nitsR, nitsG, nitsB, bt2020);
            if (bt2020)
            {
                Bt2020ToBt709(nitsR, nitsG, nitsB, out nitsR, out nitsG, out nitsB);
            }
        }

        sr = NitsToScrgb(nitsR);
        sg = NitsToScrgb(nitsG);
        sb = NitsToScrgb(nitsB);
    }

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
    string? DisplayProbe = null,
    char? MaxCllChannel = null,
    float? P99Nits = null,
    float? FallNits = null,
    double? AboveSdrWhitePercent = null,
    string? Gamut = null);

/// <summary>
/// Present raster map. <see cref="Scale"/> is exposure (SDR Reinhard) or 1
/// for absolute HDR scRGB. When <see cref="ReinhardWhite"/> &gt; 1, extended
/// Reinhard rolls highlights (same curve as <see cref="ToneMap"/> thumbs).
/// <see cref="SoftNegatives"/> true = pass OOG negatives through (HDR);
/// false = plain clamp to 0 (SDR).
/// </summary>
public readonly record struct HdrPresentMap(
    float Scale,
    float ClipScrgb,
    float ReinhardWhite = 0f,
    bool SoftNegatives = false);
