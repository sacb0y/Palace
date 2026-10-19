namespace Palace.Helpers;

/// <summary>
/// Histogram plumbing for the presented still (SDR or HDR). Bins come from
/// <see cref="HdrAnalysis"/> (one shared pass with the heatmap and HDR
/// stats) on the presented frame — never a mosaic JPEG, never online-only.
/// The axis is <see cref="HistogramScale"/>: SDR section (sRGB-encoded,
/// 0 → 203-nit reference white) then an HDR section in stops.
/// Luma is CIE Y in the <em>source</em> primaries (same as Info).
/// </summary>
public static class GalleryHistogram
{
    public const int DefaultMaxSamples = HdrAnalysis.DefaultMaxSamples;

    /// <summary>Default view: grey luminance area under R/G/B (Lightroom).</summary>
    public const bool DefaultShowLuma = true;
    public const bool DefaultShowRgb = true;

    /// <summary>
    /// Unspecified CICP on an SDR still must not take the BT.2100→scRGB
    /// matrix (that path is for missing HDR <c>colr</c>). Stamp BT.709 so
    /// JPEG/PNG/GIF channels stay sRGB.
    /// </summary>
    public static HdrProbe SdrDecodeProbe(HdrProbe probe) =>
        probe.CanPresentHdr
            ? probe
            : probe with { CicpPrimaries = probe.CicpPrimaries ?? 1 };

    public static bool ShouldBuild(
        bool isImage,
        bool isOnlineOnly,
        bool apiOnly,
        bool isOrphan,
        bool frameIsPresentedOriginal) =>
        isImage
        && !isOnlineOnly
        && !apiOnly
        && !isOrphan
        && frameIsPresentedOriginal;

    public static int SampleStride(int pixelCount, int maxSamples = DefaultMaxSamples)
    {
        if (pixelCount <= 0 || maxSamples <= 0)
        {
            return 1;
        }

        if (pixelCount <= maxSamples)
        {
            return 1;
        }

        return (int)Math.Ceiling(pixelCount / (double)maxSamples);
    }

    /// <summary>
    /// Luma and RGB toggle independently, but at least one stays on:
    /// clicking off the last active layer keeps it on.
    /// </summary>
    public static (bool Luma, bool Rgb) ToggleLayer(bool luma, bool rgb, bool clickedLuma)
    {
        if (clickedLuma)
        {
            var next = !luma;
            return !next && !rgb ? (true, rgb) : (next, rgb);
        }

        var nextRgb = !rgb;
        return !nextRgb && !luma ? (luma, true) : (luma, nextRgb);
    }

    public static void FillHeights(ReadOnlySpan<int> counts, Span<float> heights01, bool logCounts = true)
    {
        if (heights01.Length < counts.Length)
        {
            throw new ArgumentException("Height buffer too small.", nameof(heights01));
        }

        var max = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] > max)
            {
                max = counts[i];
            }
        }

        if (max <= 0)
        {
            heights01[..counts.Length].Clear();
            return;
        }

        if (logCounts)
        {
            var denom = MathF.Log(1 + max);
            for (var i = 0; i < counts.Length; i++)
            {
                heights01[i] = MathF.Log(1 + counts[i]) / denom;
            }

            return;
        }

        var scale = 1f / max;
        for (var i = 0; i < counts.Length; i++)
        {
            heights01[i] = counts[i] * scale;
        }
    }

    /// <summary>
    /// Closed area polygon (baseline → curve → baseline) for one channel, as
    /// (x, y) in plot DIPs. Bins with equal x collapse; ≤ BinCount + 2 points.
    /// </summary>
    public static (double X, double Y)[] AreaPoints(
        ReadOnlySpan<int> counts,
        int stopCount,
        double width,
        double height,
        bool closed = true)
    {
        if (counts.Length == 0 || width <= 0 || height <= 0)
        {
            return [];
        }

        var heights = new float[counts.Length];
        FillHeights(counts, heights);
        var points = new List<(double, double)>(counts.Length + 2);
        if (closed)
        {
            points.Add((HistogramScale.BinCenterX01(0, stopCount) * width, height));
        }

        for (var i = 0; i < counts.Length; i++)
        {
            var x = HistogramScale.BinCenterX01(i, stopCount) * width;
            var y = height - (Math.Clamp(heights[i], 0, 1) * height);
            points.Add((x, y));
        }

        if (closed)
        {
            points.Add((HistogramScale.BinCenterX01(counts.Length - 1, stopCount) * width, height));
        }

        return [.. points];
    }

    public static string Caption(GalleryHistogramBins? bins)
    {
        if (bins is null || !bins.HasSamples)
        {
            return "";
        }

        return bins.StopCount > 0
            ? $"SDR + {bins.StopCount} HDR stops · presented frame"
            : "SDR · presented frame";
    }
}

public sealed class GalleryHistogramBins
{
    public static GalleryHistogramBins Empty { get; } = new();

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GalleryHistogramBins()
    {
        var bins = HistogramScale.BinCount(0);
        Luma = new int[bins];
        Red = new int[bins];
        Green = new int[bins];
        Blue = new int[bins];
        BinCount = bins;
    }

    public required int[] Luma { get; init; }
    public required int[] Red { get; init; }
    public required int[] Green { get; init; }
    public required int[] Blue { get; init; }
    public int SampleCount { get; init; }
    public int BinCount { get; init; }

    /// <summary>Whole HDR stops above SDR white (0 = SDR-only chart).</summary>
    public int StopCount { get; init; }

    public float SdrWhiteNits { get; init; } = HistogramScale.SdrWhiteNits;

    public bool HasSamples => SampleCount > 0;
}
