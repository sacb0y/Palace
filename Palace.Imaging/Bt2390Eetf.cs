namespace Palace.Imaging;

/// <summary>
/// ITU-R BT.2390 EETF (hermite roll-off) as implemented by libplacebo
/// <c>bt2390</c> at <see cref="ImagingConstants.LibplaceboCommit"/>, with
/// <c>knee_offset</c> = 1.0 (libplacebo default; BT.2390-era notes often cite 0.5).
/// Operates in absolute PQ. Cite Rec. ITU-R BT.2390-10 §5.4 after verifying
/// the published section number against the PDF before tightening comments.
/// </summary>
public static class Bt2390Eetf
{
    public const float KneeOffset = 1.0f;

    /// <summary>
    /// Map working-space linear luminance. Identity when content peak
    /// <paramref name="lc"/> ≤ display <paramref name="ld"/> (both working units
    /// or both nits ratios — caller passes consistent <c>Ld = peak/SDR white</c>
    /// scaled into the same space as <paramref name="lc"/>).
    /// </summary>
    public static float MapLinear(float linear, float lc, float ld, float outputMin = 0f)
    {
        if (lc <= ld + 1e-6f)
        {
            return linear;
        }

        var inMinPq = ColorMath.NitsToPq(0f);
        var inMaxPq = ColorMath.NitsToPq(Math.Max(lc, 1e-6f) * ImagingConstants.RefWhiteNits);
        var outMinPq = ColorMath.NitsToPq(Math.Max(outputMin, 0f) * ImagingConstants.RefWhiteNits);
        var outMaxPq = ColorMath.NitsToPq(Math.Max(ld, 1e-6f) * ImagingConstants.RefWhiteNits);

        var xPq = ColorMath.NitsToPq(Math.Max(linear, 0f) * ImagingConstants.RefWhiteNits);
        xPq = Math.Clamp(xPq, inMinPq, inMaxPq);
        var yPq = MapPq(xPq, inMinPq, inMaxPq, outMinPq, outMaxPq);
        return ColorMath.PqToNits(yPq) / ImagingConstants.RefWhiteNits;
    }

    public static float MapPq(
        float x,
        float inputMin,
        float inputMax,
        float outputMin,
        float outputMax)
    {
        var minLum = RescaleIn(outputMin, inputMin, inputMax);
        var maxLum = RescaleIn(outputMax, inputMin, inputMax);
        var offset = KneeOffset;
        var ks = ((1f + offset) * maxLum) - offset;
        var bp = minLum > 0f ? Math.Min(1f / minLum, 4f) : 4f;
        var gainInv = 1f + (minLum / Math.Max(maxLum, 1e-8f) * MathF.Pow(1f - maxLum, bp));
        var gain = maxLum < 1f ? 1f / gainInv : 1f;

        x = RescaleIn(x, inputMin, inputMax);

        if (ks < 1f)
        {
            var tb = (x - ks) / (1f - ks);
            var tb2 = tb * tb;
            var tb3 = tb2 * tb;
            var pb = ((2f * tb3) - (3f * tb2) + 1f) * ks
                + ((tb3 - (2f * tb2) + tb) * (1f - ks))
                + (((-2f * tb3) + (3f * tb2)) * maxLum);
            x = x < ks ? x : pb;
        }

        if (x < 1f)
        {
            x += minLum * MathF.Pow(1f - x, bp);
            x = (gain * (x - minLum)) + minLum;
        }

        return (x * (inputMax - inputMin)) + inputMin;
    }

    private static float RescaleIn(float x, float inputMin, float inputMax) =>
        (x - inputMin) / Math.Max(inputMax - inputMin, 1e-8f);
}
