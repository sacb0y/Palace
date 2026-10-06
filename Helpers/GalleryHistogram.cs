namespace Palace.Helpers;

/// <summary>
/// CIE Y + scRGB channel histograms from a presented scRGB frame.
/// Off WinUI. Never a mosaic JPEG. Online-only stays out (no original).
/// Luma bins are log nits in the <em>source</em> primaries (Info CIE Y),
/// not BT.2020 weights on display-referred scRGB. Channels stay linear
/// presented scRGB (WIC-style 0…PQ-peak, last-bin clip).
/// </summary>
public static class GalleryHistogram
{
    public const int BinCount = 64;
    public const int DefaultMaxSamples = 65_536;
    public const float LumaMinNits = 0.01f;
    public const float LumaMaxNits = 10_000f;
    public const float ChannelMaxScrgb = 125f;

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
    /// Y / RGB is exclusive. Re-clicking the active mode must keep that
    /// check on — <c>ToggleButton</c> would otherwise uncheck it.
    /// </summary>
    public static (bool LumaChecked, bool RgbChecked) ExclusiveModeChecks(bool showRgb) =>
        (!showRgb, showRgb);

    public static int LogNitsBin(float nits, int binCount = BinCount)
    {
        if (binCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(binCount));
        }

        if (nits <= 0)
        {
            return 0;
        }

        var lo = MathF.Log2(LumaMinNits);
        var hi = MathF.Log2(LumaMaxNits);
        var t = (MathF.Log2(Math.Clamp(nits, LumaMinNits, LumaMaxNits)) - lo) / (hi - lo);
        return Math.Clamp((int)(t * binCount), 0, binCount - 1);
    }

    public static int LinearScrgbBin(float channel, int binCount = BinCount, float maxScrgb = ChannelMaxScrgb)
    {
        if (binCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(binCount));
        }

        if (maxScrgb <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxScrgb));
        }

        if (channel <= 0)
        {
            return 0;
        }

        var t = channel / maxScrgb;
        if (t >= 1)
        {
            return binCount - 1;
        }

        return Math.Clamp((int)(t * binCount), 0, binCount - 1);
    }

    public static GalleryHistogramBins FromScrgb(
        float[] rgba,
        int width,
        int height,
        int? cicpPrimaries,
        int binCount = BinCount,
        int maxSamples = DefaultMaxSamples)
    {
        if (width <= 0 || height <= 0 || rgba is null || binCount <= 0)
        {
            return GalleryHistogramBins.Empty;
        }

        var pixels = width * height;
        if (rgba.Length < pixels * 4)
        {
            return GalleryHistogramBins.Empty;
        }

        var luma = new int[binCount];
        var red = new int[binCount];
        var green = new int[binCount];
        var blue = new int[binCount];
        var stride = SampleStride(pixels, maxSamples);
        var samples = 0;
        for (var i = 0; i < pixels; i += stride)
        {
            var o = i * 4;
            var r = rgba[o];
            var g = rgba[o + 1];
            var b = rgba[o + 2];
            var yNits = GalleryPresent.LumaNitsFromPresentedScrgb(r, g, b, cicpPrimaries);
            luma[LogNitsBin(yNits, binCount)]++;
            red[LinearScrgbBin(r, binCount)]++;
            green[LinearScrgbBin(g, binCount)]++;
            blue[LinearScrgbBin(b, binCount)]++;
            samples++;
        }

        return new GalleryHistogramBins
        {
            Luma = luma,
            Red = red,
            Green = green,
            Blue = blue,
            SampleCount = samples,
            BinCount = binCount
        };
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

    public static void LayoutBars(
        int binCount,
        double width,
        double height,
        ReadOnlySpan<float> heights01,
        Span<(double X, double Width, double Height)> dest)
    {
        if (binCount <= 0 || dest.Length < binCount || heights01.Length < binCount)
        {
            throw new ArgumentOutOfRangeException(nameof(binCount));
        }

        var barW = width / binCount;
        for (var i = 0; i < binCount; i++)
        {
            var h = Math.Clamp(heights01[i], 0, 1) * height;
            dest[i] = (i * barW, barW, h);
        }
    }

    public static string Caption(bool rgb, int binCount, int samples)
    {
        if (samples <= 0 || binCount <= 0)
        {
            return "";
        }

        return rgb
            ? $"scRGB R/G/B · {binCount} bins · presented frame"
            : $"CIE Y · {binCount} bins · presented frame";
    }
}

public sealed class GalleryHistogramBins
{
    public static GalleryHistogramBins Empty { get; } = new()
    {
        Luma = new int[GalleryHistogram.BinCount],
        Red = new int[GalleryHistogram.BinCount],
        Green = new int[GalleryHistogram.BinCount],
        Blue = new int[GalleryHistogram.BinCount],
        SampleCount = 0,
        BinCount = GalleryHistogram.BinCount
    };

    public required int[] Luma { get; init; }
    public required int[] Red { get; init; }
    public required int[] Green { get; init; }
    public required int[] Blue { get; init; }
    public int SampleCount { get; init; }
    public int BinCount { get; init; }

    public bool HasSamples => SampleCount > 0;
}
