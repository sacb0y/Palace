namespace Palace.Imaging;

/// <summary>
/// Palace SDR tone curve (Isiac Option A). Scene average (P50) sets the knee
/// (0.5–0.7× ref white). Working-space 1.0 maps to a paper white that dips
/// smoothly with Lc (exactly display white at Lc ≤ 1; at most
/// <see cref="MaxPaperDip"/>). The Hermite shoulder rises from paper to
/// <b>display white exactly at Lc</b>. SDR output never exceeds display white;
/// HDR targets clamp at Ld (no reserved headroom above white).
/// </summary>
public static class SdrToneCurve
{
    public const float DefaultKneeFraction = 0.6f;

    /// <summary>
    /// Maximum fractional dip of paper white below display white as Lc → ∞
    /// (10% — mid of the approved 8–12% band).
    /// </summary>
    public const float MaxPaperDip = 0.10f;

    /// <summary>
    /// Fraction of display white for working-space 1.0 as a smooth function of
    /// Lc. Exactly 1 at Lc ≤ 1; approaches <c>1 − MaxPaperDip</c> as Lc → ∞.
    /// Uses <c>t = (1 − 1/L)²</c> so value and first derivative match at Lc = 1
    /// (no kink). <see cref="ToneMapper.MapScalar"/> floors Lc at 1.01 — there
    /// paper is ≈1 (within 1e-5), not a second discrete step.
    /// </summary>
    public static float PaperWhiteFraction(float lc)
    {
        var L = Math.Max(lc, 1f);
        // t = (1 − 1/L)²: t(1)=0, t'(1)=0 (C¹), →1 as L→∞.
        var u = 1f - (1f / L);
        var t = u * u;
        return 1f - (MaxPaperDip * t);
    }

    /// <summary>
    /// Map post-exposure working-space maxRGB to display-referred linear
    /// (1.0 = 203 nits). <paramref name="displayWhite"/> is PeakNormalized.
    /// <paramref name="lc"/> should already include exposure (Lc·2^EV) when EV ≠ 0.
    /// SDR peak output is display white (no headroom).
    /// </summary>
    public static float Map(float x, float lc, float sceneAvg, float displayWhite)
    {
        var w = Math.Max(displayWhite, 1e-6f);
        if (x <= 0f)
        {
            return 0f;
        }

        var kneeIn = sceneAvg > 0f
            ? Math.Clamp(sceneAvg, 0.5f, 0.7f)
            : DefaultKneeFraction;
        var peak = Math.Max(lc, Math.Max(1.01f, kneeIn + 0.05f));
        // Option A: paper dips smoothly with Lc; shoulder ends at display white.
        var whiteOut = w * PaperWhiteFraction(lc);
        var peakOut = w;
        var kneeOut = kneeIn * whiteOut;
        var midSlope = whiteOut;
        var whiteSlope = 0.35f * whiteOut;

        if (x <= kneeIn)
        {
            return x * whiteOut;
        }

        if (x <= 1f)
        {
            return Hermite(x, kneeIn, kneeOut, midSlope, 1f, whiteOut, whiteSlope);
        }

        if (x >= peak)
        {
            return peakOut;
        }

        // Shoulder ref white → Lc: paper → display white. Monotonic slopes.
        var dx = peak - 1f;
        var secant = (peakOut - whiteOut) / Math.Max(dx, 1e-6f);
        var m0 = Math.Min(whiteSlope, 1.5f * secant);
        var m1 = 0.15f * secant;
        return Hermite(x, 1f, whiteOut, m0, peak, peakOut, m1);
    }

    /// <summary>
    /// Cubic Hermite from (x0,y0) with slope m0 to (x1,y1) with slope m1.
    /// Slopes are dy/dx in working-space units.
    /// </summary>
    public static float Hermite(float x, float x0, float y0, float m0, float x1, float y1, float m1)
    {
        var dx = x1 - x0;
        if (dx <= 1e-8f)
        {
            return y1;
        }

        var t = Math.Clamp((x - x0) / dx, 0f, 1f);
        var t2 = t * t;
        var t3 = t2 * t;
        var M0 = m0 * dx;
        var M1 = m1 * dx;
        return ((2f * t3) - (3f * t2) + 1f) * y0
            + ((t3 - (2f * t2) + t) * M0)
            + (((-2f * t3) + (3f * t2)) * y1)
            + ((t3 - t2) * M1);
    }
}
