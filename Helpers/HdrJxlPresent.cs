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
    /// PQ/HLG JXL should present via bundled libjxl → scRGB swapchain, not
    /// WIC/<c>BitmapImage</c> (those leave PQ as a washed SDR preview).
    /// </summary>
    public static bool ShouldPresentViaLibjxl(HdrProbe probe) =>
        probe.Kind == HdrKind.HdrJxl && probe.CanPresentHdr;

    /// <summary>
    /// libjxl LinearSRGB float has 1.0 = <paramref name="intensityTarget"/> nits
    /// for HDR images (target &gt; 255). Scale so 1.0 = 80 nits (scRGB).
    /// SDR images (target ≤ 255) stay 0..1.
    /// </summary>
    public static float ScrgbScaleFromIntensityTarget(float intensityTarget) =>
        intensityTarget > 255f ? intensityTarget / GalleryPresent.ScrgbNits : 1f;

    /// <summary>Multiply RGB channels by <paramref name="scale"/>; leave alpha.</summary>
    public static void ApplyIntensityScale(Span<float> rgba, float scale)
    {
        if (scale == 1f || rgba.Length == 0)
        {
            return;
        }

        for (var i = 0; i < rgba.Length; i++)
        {
            if ((i & 3) != 3)
            {
                rgba[i] *= scale;
            }
        }
    }

    /// <summary>
    /// Build a present-sized scRGB frame from a native libjxl LinearSRGB buffer
    /// already scaled to scRGB (1.0 = 80 nits). Stats stay native; pixels may
    /// be box-scaled to the viewport like EXR.
    /// </summary>
    public static RadianceFrame? BuildPresentFrame(
        float[] scrgbRgba,
        int nativeWidth,
        int nativeHeight,
        int viewportPixelWidth,
        int viewportPixelHeight,
        ImageScaling scaling)
    {
        if (scrgbRgba is null
            || nativeWidth <= 0
            || nativeHeight <= 0
            || scrgbRgba.Length < nativeWidth * nativeHeight * 4)
        {
            return null;
        }

        var nativeStats = RadianceFile.FrameFromScrgb(scrgbRgba, nativeWidth, nativeHeight);
        var (dw, dh) = GalleryPresent.PresentDecodeSize(
            nativeWidth, nativeHeight, viewportPixelWidth, viewportPixelHeight, scaling);
        var pixels = scrgbRgba;
        var w = nativeWidth;
        var h = nativeHeight;
        if (dw > 0 && dh > 0 && (dw < nativeWidth || dh < nativeHeight))
        {
            pixels = HdrPixels.BoxScaleScrgb(scrgbRgba, nativeWidth, nativeHeight, dw, dh);
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
