namespace Palace.Imaging;

/// <summary>
/// Report ITU-R BT.2390-8 §5.4.1 EETF from the published formulas
/// (not third-party source). Verified against BT.2390-8 (2020) PDF:
/// KS = 1.5·maxLum − 0.5; Hermite P[B] for E₁ ≥ KS; E₃ = E₂ + b·(1−E₂)⁴.
/// BT.2408 paper white 203 cd/m² cited from Report ITU-R BT.2408.
/// <para>
/// P5 owns wiring the HDR-path display Ld into the app present stack; this
/// library takes <paramref name="ld"/> as a pure input.
/// </para>
/// </summary>
public static class Bt2390Eetf
{
    /// <summary>
    /// Map working-space linear. Input is clamped to <paramref name="lc"/> so
    /// values above the content peak cannot blow the E₁ normalization (N3).
    /// Identity when <paramref name="lc"/> ≤ <paramref name="ld"/>.
    /// </summary>
    public static float MapLinear(float linear, float lc, float ld, float outputMin = 0f)
    {
        if (lc <= ld + 1e-6f)
        {
            return Math.Min(linear, lc);
        }

        // N3: never feed the EETF with x > Lc (e1 would exceed 1 and explode).
        linear = Math.Clamp(linear, 0f, lc);

        var lb = Math.Max(outputMin, 0f) * ImagingConstants.RefWhiteNits;
        var lw = Math.Max(lc, 1e-6f) * ImagingConstants.RefWhiteNits;
        var lMax = Math.Max(ld, 1e-6f) * ImagingConstants.RefWhiteNits;
        var lMin = lb;

        var pqLb = ColorMath.NitsToPq(lb);
        var pqLw = ColorMath.NitsToPq(lw);
        var span = Math.Max(pqLw - pqLb, 1e-8f);

        var ePrime = ColorMath.NitsToPq(linear * ImagingConstants.RefWhiteNits);
        var e1 = Math.Clamp((ePrime - pqLb) / span, 0f, 1f);

        var minLum = Math.Clamp((ColorMath.NitsToPq(lMin) - pqLb) / span, 0f, 1f);
        var maxLum = Math.Clamp((ColorMath.NitsToPq(lMax) - pqLb) / span, 0f, 1f);

        // BT.2390-8 §5.4.1.
        var ks = (1.5f * maxLum) - 0.5f;
        var b = minLum;

        float e2;
        if (ks >= 1f - 1e-6f || e1 < ks)
        {
            e2 = e1;
        }
        else
        {
            var t = (e1 - ks) / (1f - ks);
            t = Math.Clamp(t, 0f, 1f);
            var t2 = t * t;
            var t3 = t2 * t;
            e2 = ((2f * t3) - (3f * t2) + 1f) * ks
                + ((t3 - (2f * t2) + t) * (1f - ks))
                + (((-2f * t3) + (3f * t2)) * maxLum);
        }

        e2 = Math.Clamp(e2, 0f, 1f);
        var e3 = e2 + (b * MathF.Pow(1f - e2, 4f));
        e3 = Math.Clamp(e3, 0f, 1f);

        var outPq = (e3 * span) + pqLb;
        var outLinear = ColorMath.PqToNits(Math.Clamp(outPq, 0f, 1f)) / ImagingConstants.RefWhiteNits;
        // Bound to display peak in working units.
        return Math.Min(outLinear, ld);
    }
}
