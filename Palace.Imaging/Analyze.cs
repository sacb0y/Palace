namespace Palace.Imaging;

/// <summary>
/// Full-resolution luminance / maxRGB stats. Run once on normalized data.
/// ExposureEV must not re-run this or move <see cref="ImageStats.Lc"/>.
/// </summary>
public static class Analyze
{
    public const float PercentileP50 = 0.50f;
    public const float PercentileP99995 = 0.99995f;

    public static ImageStats Run(NormalizedFrame frame, float refWhite = 1f)
    {
        var count = frame.PixelCount;
        var rgba = frame.Rgba;
        if (count <= 0)
        {
            return new ImageStats(0, 0, 0, 0, Math.Max(1f, refWhite), refWhite, frame.MetadataPeakNits, false, 0);
        }

        var luma = new float[count];
        var max = 0f;
        var maxRgb = 0f;
        for (var i = 0; i < count; i++)
        {
            var o = i * 4;
            var r = rgba[o];
            var g = rgba[o + 1];
            var b = rgba[o + 2];
            var y = Math.Max(0f, ImagingConstants.Bt2020Y(r, g, b));
            luma[i] = y;
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

        Array.Sort(luma);
        var p50 = PercentileSorted(luma, PercentileP50);
        var p99995 = PercentileSorted(luma, PercentileP99995);

        var metaNorm = frame.MetadataPeakNits is > 0f
            ? frame.MetadataPeakNits.Value / ImagingConstants.RefWhiteNits
            : (float?)null;

        var lo = Math.Max(1f, Math.Max(2f * p50, refWhite));
        var hi = metaNorm ?? Math.Max(lo, Math.Max(p99995, max));
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

    public static float PercentileSorted(ReadOnlySpan<float> sortedAscending, float fraction)
    {
        var n = sortedAscending.Length;
        if (n == 0)
        {
            return 0f;
        }

        fraction = Math.Clamp(fraction, 0f, 1f);
        if (n == 1)
        {
            return sortedAscending[0];
        }

        var idx = fraction * (n - 1);
        var i0 = (int)MathF.Floor(idx);
        var i1 = Math.Min(n - 1, i0 + 1);
        var t = idx - i0;
        return (sortedAscending[i0] * (1f - t)) + (sortedAscending[i1] * t);
    }
}
