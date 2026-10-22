namespace Palace.Imaging;

/// <summary>
/// Full-resolution luminance / maxRGB stats via a <b>PQ-spaced</b> histogram
/// (S5 — not a linear-light or full-buffer sort). ExposureEV must not re-run
/// this or move Lc.
/// </summary>
public static class Analyze
{
    public const float PercentileP50 = 0.50f;
    public const float PercentileP99995 = 0.99995f;
    public const int HistogramBins = 4096;

    public static ImageStats Run(NormalizedFrame frame, float refWhite = 1f)
    {
        var count = frame.PixelCount;
        var rgba = frame.Rgba;
        if (count <= 0)
        {
            return new ImageStats(0, 0, 0, 0, Math.Max(1f, refWhite), refWhite, frame.MetadataPeakNits, false, 0);
        }

        var max = 0f;
        var maxRgb = 0f;
        for (var i = 0; i < count; i++)
        {
            var o = i * 4;
            var r = rgba[o];
            var g = rgba[o + 1];
            var b = rgba[o + 2];
            var y = Math.Max(0f, ImagingConstants.Bt2020Y(r, g, b));
            if (y > max)
            {
                max = y;
            }

            var m = Math.Max(r, Math.Max(g, b));
            if (m > maxRgb)
            {
                maxRgb = m;
            }
        }

        // PQ histogram over [0, max] in nits — denser in shadows than linear bins.
        var maxNits = Math.Max(max, 1e-6f) * ImagingConstants.RefWhiteNits;
        var maxPq = ColorMath.NitsToPq(maxNits);
        var hist = new int[HistogramBins];
        for (var i = 0; i < count; i++)
        {
            var o = i * 4;
            var y = Math.Max(0f, ImagingConstants.Bt2020Y(rgba[o], rgba[o + 1], rgba[o + 2]));
            var pq = ColorMath.NitsToPq(y * ImagingConstants.RefWhiteNits);
            var bin = (int)Math.Clamp((pq / Math.Max(maxPq, 1e-8f)) * (HistogramBins - 1), 0, HistogramBins - 1);
            hist[bin]++;
        }

        var p50 = PercentileFromPqHistogram(hist, count, maxPq, PercentileP50);
        var p99995 = PercentileFromPqHistogram(hist, count, maxPq, PercentileP99995);

        var metaNorm = frame.MetadataPeakNits is > 0f
            ? frame.MetadataPeakNits.Value / ImagingConstants.RefWhiteNits
            : (float?)null;

        var lo = Math.Max(1f, Math.Max(2f * p50, refWhite));
        var hi = metaNorm ?? Math.Max(lo, Math.Max(p99995, max));
        if (hi < lo)
        {
            hi = lo;
        }

        var lc = Math.Clamp(p99995, lo, hi);

        return new ImageStats(
            p50,
            p99995,
            max,
            maxRgb,
            lc,
            refWhite,
            frame.MetadataPeakNits,
            frame.SourceClipped,
            frame.SourceClipRatio);
    }

    public static float PercentileFromPqHistogram(ReadOnlySpan<int> hist, int total, float maxPq, float fraction)
    {
        if (total <= 0)
        {
            return 0f;
        }

        fraction = Math.Clamp(fraction, 0f, 1f);
        var target = fraction * (total - 1);
        var accum = 0;
        for (var i = 0; i < hist.Length; i++)
        {
            accum += hist[i];
            if (accum > target)
            {
                var pq = (i / (float)(hist.Length - 1)) * maxPq;
                return ColorMath.PqToNits(pq) / ImagingConstants.RefWhiteNits;
            }
        }

        return ColorMath.PqToNits(maxPq) / ImagingConstants.RefWhiteNits;
    }
}
