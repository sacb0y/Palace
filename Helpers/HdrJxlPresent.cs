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
    /// Cap native float decode (~16 bytes/pixel RGBA). ~64 MP ≈ 1 GB peak;
    /// kept present buffers for Fit are viewport-sized after box-scale.
    /// </summary>
    public const int PresentMaxPixels = 64_000_000;

    /// <summary>
    /// PQ/HLG JXL should present via bundled libjxl → scRGB swapchain, not
    /// WIC/<c>BitmapImage</c> (those leave PQ as a washed SDR preview).
    /// Gain-map / wide-gamut / SDR JXL stays off this path (<see cref="HdrKind.WideGamutJxl"/>).
    /// </summary>
    public static bool ShouldPresentViaLibjxl(HdrProbe probe) =>
        probe.Kind == HdrKind.HdrJxl && probe.CanPresentHdr;

    /// <summary>
    /// libjxl LinearSRGB float has 1.0 = <paramref name="intensityTarget"/> nits
    /// for PQ/HLG. Scale so 1.0 = 80 nits (scRGB). Route on the probe transfer
    /// (not an IntensityTarget &gt; 255 heuristic) so SDR / gain-map base stays 0..1.
    /// </summary>
    public static float ScrgbScaleFromIntensityTarget(float intensityTarget, HdrTransfer transfer) =>
        transfer is HdrTransfer.Pq or HdrTransfer.Hlg
            ? (intensityTarget > 0f ? intensityTarget : 10_000f) / GalleryPresent.ScrgbNits
            : 1f;

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
