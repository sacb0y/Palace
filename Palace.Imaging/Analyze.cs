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

    /// <summary>
    /// Fixed fine PQ histogram over absolute PQ ∈ [0,1] for streamed strip
    /// accumulate — O(1) memory (never stores every Y).
    /// </summary>
    public const int FineHistogramBins = 65536;

    /// <summary>
    /// Streamed PQ-histogram accumulator. <see cref="Add"/> bumps a fixed
    /// <see cref="FineHistogramBins"/> hist + max/maxRgb; <see cref="Finish"/>
    /// rebins to <see cref="HistogramBins"/> over [0,maxPq] then the shared
    /// percentile / Lc tail. Used by strip decode (no float[] of every Y).
    /// </summary>
    public sealed class Accumulator
    {
        private readonly float _refWhite;
        private readonly float? _metadataPeak;
        private readonly bool _sourceClipped;
        private readonly float _sourceClipRatio;
        private readonly int[] _fine = new int[FineHistogramBins];
        private float _max;
        private float _maxRgb;
        private int _count;

        public Accumulator(
            float refWhite = 1f,
            float? metadataPeak = null,
            bool sourceClipped = false,
            float sourceClipRatio = 0f)
        {
            _refWhite = refWhite;
            _metadataPeak = metadataPeak;
            _sourceClipped = sourceClipped;
            _sourceClipRatio = sourceClipRatio;
        }

        public int SampleCount => _count;

        /// <summary>
        /// Add Working-space RGBA pixels (<paramref name="rgba"/> length ≥
        /// pixels×4). Tracks max / maxRgb and fine PQ hist only.
        /// </summary>
        public void Add(ReadOnlySpan<float> rgba, int pixelCount)
        {
            if (pixelCount <= 0)
            {
                return;
            }

            if (rgba.Length < pixelCount * 4)
            {
                throw new ArgumentException(
                    "RGBA span smaller than pixelCount × 4.", nameof(rgba));
            }

            var last = FineHistogramBins - 1;
            for (var i = 0; i < pixelCount; i++)
            {
                var o = i * 4;
                var r = rgba[o];
                var g = rgba[o + 1];
                var b = rgba[o + 2];
                var y = Math.Max(0f, ImagingConstants.Bt2020Y(r, g, b));
                if (y > _max)
                {
                    _max = y;
                }

                var m = Math.Max(r, Math.Max(g, b));
                if (m > _maxRgb)
                {
                    _maxRgb = m;
                }

                var pq = ColorMath.NitsToPq(y * ImagingConstants.RefWhiteNits);
                var bin = (int)Math.Clamp(pq * last, 0, last);
                _fine[bin]++;
                _count++;
            }
        }

        /// <summary>Add a contiguous <c>width × rows</c> RGBA band.</summary>
        public void Add(ReadOnlySpan<float> rgbaRows, int width, int rows)
        {
            if (width < 1 || rows < 1)
            {
                return;
            }

            Add(rgbaRows, checked(width * rows));
        }

        public ImageStats Finish()
        {
            if (_count <= 0)
            {
                return new ImageStats(
                    0, 0, 0, 0, Math.Max(1f, _refWhite), _refWhite,
                    _metadataPeak, _sourceClipped, _sourceClipRatio);
            }

            var maxNits = Math.Max(_max, 1e-6f) * ImagingConstants.RefWhiteNits;
            var maxPq = ColorMath.NitsToPq(maxNits);
            var hist = RebinFineToAnalyze(_fine, maxPq);
            return FinishFromHistogram(
                hist,
                _count,
                _max,
                _maxRgb,
                maxPq,
                _refWhite,
                _metadataPeak,
                _sourceClipped,
                _sourceClipRatio);
        }
    }

    /// <summary>
    /// Exact two-pass Analyze (unchanged vs pre-Accumulator). Shares only
    /// <see cref="FinishFromHistogram"/> with the streamed path.
    /// </summary>
    public static ImageStats Run(NormalizedFrame frame, float refWhite = 1f)
    {
        var count = frame.PixelCount;
        var rgba = frame.Rgba;
        if (count <= 0)
        {
            return new ImageStats(
                0, 0, 0, 0, Math.Max(1f, refWhite), refWhite,
                frame.MetadataPeakNits, false, 0);
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
            var bin = (int)Math.Clamp(
                (pq / Math.Max(maxPq, 1e-8f)) * (HistogramBins - 1),
                0,
                HistogramBins - 1);
            hist[bin]++;
        }

        return FinishFromHistogram(
            hist,
            count,
            max,
            maxRgb,
            maxPq,
            refWhite,
            frame.MetadataPeakNits,
            frame.SourceClipped,
            frame.SourceClipRatio);
    }

    /// <summary>
    /// Rebin absolute-PQ fine hist [0,1] into Analyze hist over [0,maxPq].
    /// Fine index i represents PQ = i/(fineBins-1) — same edge convention as
    /// <see cref="Run"/> so black (bin 0) stays PQ 0.
    /// </summary>
    internal static int[] RebinFineToAnalyze(ReadOnlySpan<int> fine, float maxPq)
    {
        var hist = new int[HistogramBins];
        var fineLast = fine.Length - 1;
        var coarseLast = HistogramBins - 1;
        var denom = Math.Max(maxPq, 1e-8f);
        for (var i = 0; i < fine.Length; i++)
        {
            var c = fine[i];
            if (c == 0)
            {
                continue;
            }

            var pq = i / (float)fineLast;
            var bin = (int)Math.Clamp((pq / denom) * coarseLast, 0, coarseLast);
            hist[bin] += c;
        }

        return hist;
    }

    /// <summary>Shared percentile / Lc / metadata tail for Run and Accumulator.</summary>
    internal static ImageStats FinishFromHistogram(
        ReadOnlySpan<int> hist,
        int count,
        float max,
        float maxRgb,
        float maxPq,
        float refWhite,
        float? metadataPeakNits,
        bool sourceClipped,
        float sourceClipRatio)
    {
        if (count <= 0)
        {
            return new ImageStats(
                0, 0, 0, 0, Math.Max(1f, refWhite), refWhite,
                metadataPeakNits, sourceClipped, sourceClipRatio);
        }

        var p50 = PercentileFromPqHistogram(hist, count, maxPq, PercentileP50);
        var p99995 = PercentileFromPqHistogram(hist, count, maxPq, PercentileP99995);

        var metaNorm = metadataPeakNits is > 0f
            ? metadataPeakNits.Value / ImagingConstants.RefWhiteNits
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
            metadataPeakNits,
            sourceClipped,
            sourceClipRatio);
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

    /// <summary>
    /// Working-space width of one Analyze (<see cref="HistogramBins"/>) PQ bin
    /// at <paramref name="atWorkingY"/> given frame max Y. Uses the larger of
    /// forward/backward PQ steps so the top bin (at max) is non-zero.
    /// </summary>
    public static float OneAnalyzeBinWidthWorking(float maxY, float atWorkingY)
    {
        var maxNits = Math.Max(maxY, 1e-6f) * ImagingConstants.RefWhiteNits;
        var maxPq = ColorMath.NitsToPq(maxNits);
        var dPq = maxPq / (HistogramBins - 1);
        var atPq = ColorMath.NitsToPq(
            Math.Max(0f, atWorkingY) * ImagingConstants.RefWhiteNits);
        atPq = Math.Clamp(atPq, 0f, maxPq);
        var yAt = ColorMath.PqToNits(atPq) / ImagingConstants.RefWhiteNits;
        var yFwd = ColorMath.PqToNits(Math.Min(maxPq, atPq + dPq)) / ImagingConstants.RefWhiteNits;
        var yBack = ColorMath.PqToNits(Math.Max(0f, atPq - dPq)) / ImagingConstants.RefWhiteNits;
        return Math.Max(
            Math.Max(Math.Abs(yFwd - yAt), Math.Abs(yAt - yBack)),
            1e-7f);
    }
}
