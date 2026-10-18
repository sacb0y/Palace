using System.Runtime.CompilerServices;

namespace Palace.Helpers;

/// <summary>
/// Per-pixel scRGB → output mapping shared by the HDR swapchain present
/// (<see cref="HdrRasterize"/>) and, later, export / conversion: tonemap
/// scale + clip (<see cref="GalleryPresent.PresentMap"/>) or an analysis
/// visualization (<see cref="HdrVizMode"/>). Visualizations measure the
/// <em>source</em> pixel (before tonemap) with <see cref="HdrAnalysis.PixelNits"/>,
/// so heatmap colours match the histogram's HDR section and Info.
/// </summary>
public readonly record struct HdrPixelMap(
    float Scale,
    float ClipScrgb,
    HdrVizMode Viz = HdrVizMode.None,
    float VizWhiteScrgb = 1f,
    int? CicpPrimaries = null)
{
    /// <summary>
    /// Palette white for visualizations: SDR reference white on an HDR
    /// display (203 nits), composition white (scRGB 1.0) on SDR.
    /// </summary>
    public static float VizWhiteFor(bool displayIsHdr) =>
        displayIsHdr ? HistogramScale.SdrWhiteNits / GalleryPresent.ScrgbNits : 1f;

    public static HdrPixelMap From(HdrPresentMap map, HdrVizMode viz = HdrVizMode.None, bool displayIsHdr = false, int? cicpPrimaries = null) =>
        new(map.Scale, map.ClipScrgb, viz, VizWhiteFor(displayIsHdr), cicpPrimaries);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Apply(float r, float g, float b, out float or, out float og, out float ob)
    {
        if (Viz == HdrVizMode.Heatmap)
        {
            var nits = HdrAnalysis.PixelNits(r, g, b, CicpPrimaries);
            HdrHeatmap.ScrgbAt(nits, VizWhiteScrgb, out or, out og, out ob);
            var c = Math.Max(ClipScrgb, 0f);
            or = Math.Min(or, c);
            og = Math.Min(og, c);
            ob = Math.Min(ob, c);
            return;
        }

        or = Math.Clamp(r * Scale, 0, ClipScrgb);
        og = Math.Clamp(g * Scale, 0, ClipScrgb);
        ob = Math.Clamp(b * Scale, 0, ClipScrgb);
    }
}
