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
    /// Streamed PQ-histogram accumulator. <see cref="Add"/> rows (or any RGBA
    /// pixel span); <see cref="Finish"/> builds the same
    /// <see cref="ImageStats"/> as <see cref="Run"/>. Used by strip decode so
    /// Lc / P50 match DecodeAsync (mean-P50 / maxRgb-as-Lc muted tone).
    /// </summary>
    public sealed class Accumulator
    {
        private readonly float _refWhite;
        private readonly float? _metadataPeak;
        private readonly bool _sourceClipped;
        private readonly float _sourceClipRatio;
        private float _max;
        private float _maxRgb;
        private float[] _y = new float[4096];
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
        /// pixels×4). Same per-pixel max / Y collection as <see cref="Run"/>.
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

            EnsureYCapacity(_count + pixelCount);
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

                _y[_count++] = y;
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

            return FinishFromSamples(
                _y.AsSpan(0, _count),
                _max,
                _maxRgb,
                _refWhite,
                _metadataPeak,
                _sourceClipped,
                _sourceClipRatio);
        }

        private void EnsureYCapacity(int need)
        {
            if (_y.Length >= need)
            {
                return;
            }

            var n = _y.Length;
            while (n < need)
            {
                n = checked(n * 2);
            }

            Array.Resize(ref _y, n);
        }
    }

    /// <summary>Thin wrapper over <see cref="Accumulator"/>.</summary>
    public static ImageStats Run(NormalizedFrame frame, float refWhite = 1f)
    {
        var acc = new Accumulator(
            refWhite,
            frame.MetadataPeakNits,
            frame.SourceClipped,
            frame.SourceClipRatio);
        var count = frame.PixelCount;
        if (count > 0)
        {
            acc.Add(frame.Rgba.AsSpan(0, count * 4), count);
        }

        return acc.Finish();
    }

    internal static ImageStats FinishFromSamples(
        ReadOnlySpan<float> ySamples,
        float max,
        float maxRgb,
        float refWhite,
        float? metadataPeakNits,
        bool sourceClipped,
        float sourceClipRatio)
    {
        var count = ySamples.Length;
        if (count <= 0)
        {
            return new ImageStats(
                0, 0, 0, 0, Math.Max(1f, refWhite), refWhite,
                metadataPeakNits, sourceClipped, sourceClipRatio);
        }

        // PQ histogram over [0, max] in nits — denser in shadows than linear bins.
        var maxNits = Math.Max(max, 1e-6f) * ImagingConstants.RefWhiteNits;
        var maxPq = ColorMath.NitsToPq(maxNits);
        var hist = new int[HistogramBins];
        for (var i = 0; i < count; i++)
        {
            var y = ySamples[i];
            var pq = ColorMath.NitsToPq(y * ImagingConstants.RefWhiteNits);
            var bin = (int)Math.Clamp(
                (pq / Math.Max(maxPq, 1e-8f)) * (HistogramBins - 1),
                0,
                HistogramBins - 1);
            hist[bin]++;
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
}
