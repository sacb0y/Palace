namespace Palace.Helpers;

/// <summary>
/// Actual transfer of the float buffer after libjxl CMS / preferred profile
/// (from <c>JxlDecoderGetColorAsEncodedProfile(TARGET_DATA)</c>). Still-PQ/HLG
/// must not get <see cref="ScrgbScaleFromIntensityTarget"/> — that treats
/// encoded 0..1 as linear relative and ×125 whiteouts HDR JXL on SDR.
/// </summary>
public enum JxlBufferTransfer
{
    Unknown = 0,
    Linear = 1,
    Pq = 2,
    Hlg = 3
}

/// <summary>
/// Result of normalizing libjxl float RGBA to scRGB (1.0 = 80 nits).
/// </summary>
public readonly record struct JxlNormalizeResult(
    float IntensityTarget,
    float ScaleApplied,
    JxlBufferTransfer BufferTransfer);

/// <summary>
/// HDR JPEG XL present helpers (WinUI-free). libjxl decode lives in
/// <c>Services/JxlDecode</c>; this owns scRGB intensity scale, present-frame
/// build (viewport box-scale + native stats), and routing checks so
/// <c>Palace.Tests</c> can cover them without Starward natives.
/// </summary>
public static class HdrJxlPresent
{
    /// <summary>Last normalize result (present / thumb decode). For diagnostics.</summary>
    public static JxlNormalizeResult LastNormalize { get; private set; } = new(0f, 1f, JxlBufferTransfer.Unknown);

    /// <summary>
    /// Cap native float decode for HDR present (~16 bytes/pixel RGBA).
    /// ~64 MP ≈ 1 GB peak; Fit keeps a viewport buffer after box-scale.
    /// </summary>
    public const int PresentMaxPixels = 64_000_000;

    /// <summary>
    /// Mosaic / large-preview thumb decode cap (legacy 120 MP). Separate from
    /// <see cref="PresentMaxPixels"/> so thumbs can still open large JXL that
    /// the swapchain present path refuses.
    /// </summary>
    public const int ThumbMaxPixels = 120_000_000;

    /// <summary>
    /// PQ/HLG JXL should present via bundled libjxl → scRGB swapchain, not
    /// WIC/<c>BitmapImage</c> (those leave PQ as a washed SDR preview).
    /// Gain-map / wide-gamut / SDR JXL stays off this path (<see cref="HdrKind.WideGamutJxl"/>).
    /// </summary>
    public static bool ShouldPresentViaLibjxl(HdrProbe probe) =>
        probe.Kind == HdrKind.HdrJxl && probe.CanPresentHdr;

    /// <summary>
    /// Never open JPEG XL through inbox WIC present/measure. Packaged hosts
    /// often lack the Store JXL codec (<c>WINCODEC_ERR_COMPONENTNOTFOUND</c>
    /// <c>0x88982F50</c>); HDR JXL belongs on libjxl, and other JXL must not
    /// log a false "WIC present" failure after a successful libjxl path.
    /// </summary>
    public static bool SkipWicPresent(HdrProbe probe, string? path) =>
        ShouldPresentViaLibjxl(probe) || PathSafe.IsJxl(path);

    /// <summary>
    /// Media-relative intent for linear float out (ISO 15076-1 Relative).
    /// Pair with BT.2020 / BT.2100 primaries + <see cref="Bt2020LinearToScrgb"/>
    /// so OOG stays negative for Gamut — plain LinearSRGB Relative would map
    /// wide-gamut SDR (P3) into Rec.709 and read "Rec.709 100%".
    /// (Old SDR washout was the present MidGrey boost up to 4×, not intent.)
    /// </summary>
    public const int ScrgbOutputRenderingIntentRelative = 1;

    /// <summary>
    /// PQ/HLG or wide primaries need BT.2100 linear out then
    /// <see cref="Bt2020LinearToScrgb"/> — not default LinearSRGB primaries.
    /// Primaries: 9 = BT.2020, <b>11 = JXL's own P3</b> (libjxl),
    /// <b>12 = CICP Display P3</b>.
    /// </summary>
    public static bool UsesBt2020LinearOutput(HdrTransfer transfer, int? cicpPrimaries) =>
        transfer is HdrTransfer.Pq or HdrTransfer.Hlg
        || cicpPrimaries is 9 or 11 or 12;

    /// <summary>
    /// libjxl Relative <b>linear</b> float has 1.0 = <paramref name="intensityTarget"/>
    /// nits for PQ/HLG sources. Scale so 1.0 = 80 nits (scRGB). Only for
    /// linear buffers — never on still-PQ/HLG codes. Missing target (≤0):
    /// PQ → 10 000 nits, HLG → 1000 nits. Do <b>not</b> treat libjxl's
    /// default <c>255</c> as unset.
    /// </summary>
    public static float ScrgbScaleFromIntensityTarget(float intensityTarget, HdrTransfer transfer)
    {
        if (transfer is not (HdrTransfer.Pq or HdrTransfer.Hlg))
        {
            return 1f;
        }

        var nits = intensityTarget > 0f
            ? intensityTarget
            : transfer == HdrTransfer.Hlg ? 1000f : 10_000f;
        return nits / GalleryPresent.ScrgbNits;
    }

    /// <summary>
    /// Intensity scale only when the float buffer is already linear.
    /// Still-PQ/HLG → 0 (caller must EOTF → nits → scRGB instead).
    /// </summary>
    public static float IntensityScaleForBuffer(
        float intensityTarget,
        HdrTransfer probeTransfer,
        JxlBufferTransfer actualBuffer)
    {
        if (actualBuffer is JxlBufferTransfer.Pq or JxlBufferTransfer.Hlg)
        {
            return 0f;
        }

        // Unknown after a PQ/HLG probe: treat as still-encoded (do not IT-scale).
        if (actualBuffer == JxlBufferTransfer.Unknown
            && probeTransfer is HdrTransfer.Pq or HdrTransfer.Hlg)
        {
            return 0f;
        }

        if (actualBuffer != JxlBufferTransfer.Linear
            && actualBuffer != JxlBufferTransfer.Unknown)
        {
            return 0f;
        }

        return ScrgbScaleFromIntensityTarget(intensityTarget, probeTransfer);
    }

    /// <summary>
    /// Map libjxl <c>JxlTransferFunction</c> numeric (CICP-aligned: 8 linear,
    /// 16 PQ, 18 HLG) to buffer kind. Anything else → Unknown so a PQ probe
    /// takes the encoded path instead of IT-scaling gamma codes.
    /// </summary>
    public static JxlBufferTransfer BufferTransferFromJxlTf(int transferFunction) =>
        transferFunction switch
        {
            16 => JxlBufferTransfer.Pq,
            18 => JxlBufferTransfer.Hlg,
            8 => JxlBufferTransfer.Linear,
            _ => JxlBufferTransfer.Unknown
        };

    public static string BufferTransferLabel(JxlBufferTransfer t) =>
        t switch
        {
            JxlBufferTransfer.Linear => "linear",
            JxlBufferTransfer.Pq => "pq",
            JxlBufferTransfer.Hlg => "hlg",
            _ => "unknown"
        };

    /// <summary>
    /// Normalize float RGBA from libjxl to scRGB. If CMS left PQ/HLG codes,
    /// EOTF → nits → /80 (no intensity_target multiply). If linear, apply
    /// <c>intensity_target/80</c> for PQ/HLG probes, then BT.2020→709 when needed.
    /// </summary>
    public static JxlNormalizeResult NormalizeToScrgb(
        Span<float> rgba,
        float intensityTarget,
        HdrTransfer probeTransfer,
        JxlBufferTransfer actualBuffer,
        int? cicpPrimaries)
    {
        // Probe PQ/HLG + unknown/still-PQ → encoded path (Kalan whiteout fix).
        var stillEncoded = actualBuffer is JxlBufferTransfer.Pq or JxlBufferTransfer.Hlg
            || (actualBuffer == JxlBufferTransfer.Unknown
                && probeTransfer is HdrTransfer.Pq or HdrTransfer.Hlg);

        if (stillEncoded)
        {
            var encodedTf = actualBuffer == JxlBufferTransfer.Hlg
                || (actualBuffer == JxlBufferTransfer.Unknown && probeTransfer == HdrTransfer.Hlg)
                ? HdrTransfer.Hlg
                : HdrTransfer.Pq;
            EncodedPqHlgToScrgb(rgba, encodedTf, cicpPrimaries);
            var result = new JxlNormalizeResult(intensityTarget, 0f, encodedTf == HdrTransfer.Hlg
                ? JxlBufferTransfer.Hlg
                : JxlBufferTransfer.Pq);
            LastNormalize = result;
            return result;
        }

        var scale = IntensityScaleForBuffer(intensityTarget, probeTransfer, JxlBufferTransfer.Linear);
        ApplyIntensityScale(rgba, scale);
        if (UsesBt2020LinearOutput(probeTransfer, cicpPrimaries))
        {
            Bt2020LinearToScrgb(rgba);
        }

        var linear = new JxlNormalizeResult(intensityTarget, scale, JxlBufferTransfer.Linear);
        LastNormalize = linear;
        return linear;
    }

    /// <summary>
    /// Still-PQ/HLG float codes → scRGB (1.0 = 80 nits). Never multiply by
    /// intensity_target — that assumes linear relative units.
    /// </summary>
    public static void EncodedPqHlgToScrgb(
        Span<float> rgba,
        HdrTransfer encodedTransfer,
        int? cicpPrimaries)
    {
        if (encodedTransfer is not (HdrTransfer.Pq or HdrTransfer.Hlg))
        {
            return;
        }

        var end = rgba.Length - (rgba.Length & 3);
        var primaries = cicpPrimaries ?? 9;
        for (var i = 0; i < end; i += 4)
        {
            GalleryPresent.PngEncodedToScrgb(
                FiniteOrZero(rgba[i]),
                FiniteOrZero(rgba[i + 1]),
                FiniteOrZero(rgba[i + 2]),
                encodedTransfer,
                primaries,
                out var sr,
                out var sg,
                out var sb,
                out _);
            rgba[i] = sr;
            rgba[i + 1] = sg;
            rgba[i + 2] = sb;
        }
    }

    private static float FiniteOrZero(float v) => float.IsFinite(v) ? v : 0f;

    /// <summary>
    /// BT.2020 linear RGB (1.0 = 80 nits) → scRGB BT.709, keeping negatives.
    /// </summary>
    public static void Bt2020LinearToScrgb(Span<float> rgba)
    {
        var end = rgba.Length - (rgba.Length & 3);
        for (var i = 0; i < end; i += 4)
        {
            GalleryPresent.Bt2020ToBt709(rgba[i], rgba[i + 1], rgba[i + 2], out var r, out var g, out var b);
            rgba[i] = r;
            rgba[i + 1] = g;
            rgba[i + 2] = b;
        }
    }

    /// <summary>
    /// Mean CIE Y after SDR Reinhard present map — for tests that PQ/HLG JXL
    /// is readable (not crushed, not almost-white).
    /// </summary>
    public static float ToneMappedMeanLuminance(
        ReadOnlySpan<float> scrgbRgba,
        int pixelCount,
        float sdrWhiteLevelNits = 0f)
    {
        if (pixelCount <= 0 || scrgbRgba.Length < pixelCount * 4)
        {
            return 0f;
        }

        var (exposure, white) = ToneMap.AnalyzeHdrPresent(
            scrgbRgba, pixelCount, sdrWhiteLevelNits);
        double sum = 0;
        for (var i = 0; i < pixelCount; i++)
        {
            ToneMap.MapPresentRgb(
                scrgbRgba[i * 4],
                scrgbRgba[i * 4 + 1],
                scrgbRgba[i * 4 + 2],
                exposure,
                1f,
                white,
                softNegatives: false,
                out var r,
                out var g,
                out var b);
            sum += ToneMap.Luminance(r, g, b);
        }

        return (float)(sum / pixelCount);
    }

    /// <summary>Multiply RGB channels by <paramref name="scale"/>; leave alpha.</summary>
    public static void ApplyIntensityScale(Span<float> rgba, float scale)
    {
        if (scale == 1f || rgba.Length < 4)
        {
            return;
        }

        var end = rgba.Length - (rgba.Length & 3);
        for (var i = 0; i < end; i += 4)
        {
            rgba[i] *= scale;
            rgba[i + 1] *= scale;
            rgba[i + 2] *= scale;
        }
    }

    /// <summary>
    /// Build a present-sized scRGB frame from a native libjxl LinearSRGB buffer
    /// already scaled to scRGB (1.0 = 80 nits). Stats stay native; pixels may
    /// be box-scaled to the viewport like EXR. Callers must not run a second
    /// full native decode just to measure — reuse these stats.
    /// </summary>
    public static RadianceFrame? BuildPresentFrame(
        float[] scrgbRgba,
        int nativeWidth,
        int nativeHeight,
        int viewportPixelWidth,
        int viewportPixelHeight,
        ImageScaling scaling,
        CancellationToken cancellation = default)
    {
        if (scrgbRgba is null
            || nativeWidth <= 0
            || nativeHeight <= 0
            || scrgbRgba.Length < nativeWidth * nativeHeight * 4)
        {
            return null;
        }

        cancellation.ThrowIfCancellationRequested();
        var nativeStats = RadianceFile.FrameFromScrgb(scrgbRgba, nativeWidth, nativeHeight);
        var (dw, dh) = GalleryPresent.PresentDecodeSize(
            nativeWidth, nativeHeight, viewportPixelWidth, viewportPixelHeight, scaling);
        var pixels = scrgbRgba;
        var w = nativeWidth;
        var h = nativeHeight;
        if (dw > 0 && dh > 0 && (dw < nativeWidth || dh < nativeHeight))
        {
            cancellation.ThrowIfCancellationRequested();
            pixels = HdrPixels.BoxScaleScrgb(scrgbRgba, nativeWidth, nativeHeight, dw, dh, cancellation);
            w = dw;
            h = dh;
        }

        return new RadianceFrame
        {
            ScrgbRgba = pixels,
            Width = w,
            Height = h,
            MaxNits = nativeStats.MaxNits,
            AvgNits = nativeStats.AvgNits,
            MinNits = nativeStats.MinNits,
            MaxScrgb = nativeStats.MaxScrgb,
            MaxScrgbChannel = nativeStats.MaxScrgbChannel
        };
    }
}
