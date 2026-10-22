namespace Palace.Imaging;

/// <summary>
/// Report ITU-R BT.2390 §5.4.1 EETF (electrical-electrical transfer function)
/// implemented from the published formulas — not from third-party source.
/// <para>
/// <c>KS = 1.5·maxLum − 0.5</c>; Hermite shoulder for E₁ ≥ KS; black lift
/// <c>E₃ = E₂ + b·(1−E₂)⁴</c> with <c>b = minLum</c>. Confirm section numbering
/// against the current BT.2390 PDF before tightening citations.
/// </para>
/// Units: operates in absolute PQ; callers pass working-space linear
/// (1.0 = 203 nits) which is converted to PQ nits at the edges.
/// </summary>
public static class Bt2390Eetf
{
    /// <summary>
    /// Map working-space linear. Identity when content peak <paramref name="lc"/>
    /// ≤ display <paramref name="ld"/> (both in working-space units where
    /// <c>Ld = peakNits / sdrWhiteNits</c> scaled consistently — see
    /// <see cref="DisplayTarget.Ld"/>).
    /// </summary>
    public static float MapLinear(float linear, float lc, float ld, float outputMin = 0f)
    {
        if (lc <= ld + 1e-6f)
        {
            return linear;
        }

        // Mastering peak LW = lc · 203 nits; target Lmax = ld · 203 nits.
        // LB = Lmin = 0 for the common case (outputMin = 0).
        var lb = Math.Max(outputMin, 0f) * ImagingConstants.RefWhiteNits;
        var lw = Math.Max(lc, 1e-6f) * ImagingConstants.RefWhiteNits;
        var lMax = Math.Max(ld, 1e-6f) * ImagingConstants.RefWhiteNits;
        var lMin = lb;

        var pqLb = ColorMath.NitsToPq(lb);
        var pqLw = ColorMath.NitsToPq(lw);
        var span = Math.Max(pqLw - pqLb, 1e-8f);

        var ePrime = ColorMath.NitsToPq(Math.Max(linear, 0f) * ImagingConstants.RefWhiteNits);
        var e1 = (ePrime - pqLb) / span;

        var minLum = (ColorMath.NitsToPq(lMin) - pqLb) / span;
        var maxLum = (ColorMath.NitsToPq(lMax) - pqLb) / span;

        // BT.2390 §5.4.1: KS = 1.5·maxLum − 0.5; b = minLum.
        var ks = (1.5f * maxLum) - 0.5f;
        var b = minLum;

        float e2;
        if (e1 < ks || ks >= 1f)
        {
            e2 = e1;
        }
        else
        {
            var t = (e1 - ks) / (1f - ks);
            var t2 = t * t;
            var t3 = t2 * t;
            e2 = ((2f * t3) - (3f * t2) + 1f) * ks
                + ((t3 - (2f * t2) + t) * (1f - ks))
                + (((-2f * t3) + (3f * t2)) * maxLum);
        }

        var e3 = e2;
        if (e2 >= 0f && e2 <= 1f)
        {
            e3 = e2 + (b * MathF.Pow(1f - e2, 4f));
        }

        var outPq = (e3 * span) + pqLb;
        return ColorMath.PqToNits(Math.Clamp(outPq, 0f, 1f)) / ImagingConstants.RefWhiteNits;
    }
}
