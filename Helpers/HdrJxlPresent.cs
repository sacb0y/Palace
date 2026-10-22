namespace Palace.Helpers;

/// <summary>
/// HDR JPEG XL present helpers (WinUI-free). libjxl decode lives in
/// <c>Services/JxlDecode</c>; this owns scRGB intensity scale, present-frame
/// build (viewport box-scale + native stats), and routing checks so
/// <c>Palace.Tests</c> can cover them without Starward natives.
/// </summary>
public static class HdrJxlPresent
{
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
    /// libjxl <c>LinearSRGB</c> defaults to Perceptual intent, which maps
    /// BT.2020 into Rec.709 and makes Gamut read "Rec.709 100%" even when the
    /// probe is BT.2020. Absolute keeps out-of-gamut as negative scRGB so
    /// <see cref="CieGamut"/> can classify Rec.2020 / P3 coverage.
    /// Matches <c>JxlRenderingIntent.Absolute</c> (ISO 15076-1).
    /// </summary>
    public const int ScrgbOutputRenderingIntentAbsolute = 3;

    /// <summary>
    /// libjxl LinearSRGB float has 1.0 = <paramref name="intensityTarget"/> nits
    /// for PQ/HLG. Scale so 1.0 = 80 nits (scRGB). Route on the probe transfer
    /// (not an IntensityTarget &gt; 255 heuristic) so SDR / gain-map base stays 0..1.
    /// Missing target: PQ → 10 000 nits, HLG → 1000 nits (HLG nominal peak).
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
