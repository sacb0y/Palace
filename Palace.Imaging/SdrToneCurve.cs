namespace Palace.Imaging;

/// <summary>
/// Palace SDR tone curve (v2). Ref white (working 1.0) maps to
/// <b>display white for every Lc</b>. Scene average (P50) sets the knee
/// (0.5–0.7× ref white). The Hermite shoulder covers <b>1..Lc only</b>, rising
/// from display white to a small headroom at Lc so highlights stay ordered;
/// soft-clip after MapPixel is only a safety net above that headroom.
/// </summary>
public static class SdrToneCurve
{
    public const float DefaultKneeFraction = 0.6f;

    /// <summary>
    /// Output at Lc as a multiple of display white (shoulder headroom above
    /// white — not a paper-white dim). Soft-clip safety uses the same factor.
    /// </summary>
    public const float HighlightHeadroom = 1.12f;

    /// <summary>
    /// Map post-exposure working-space maxRGB to display-referred linear
    /// (1.0 = 203 nits). <paramref name="displayWhite"/> is PeakNormalized.
    /// <paramref name="lc"/> should already include exposure (Lc·2^EV) when EV ≠ 0.
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
        // v2 / Q1: ref white → display white for every Lc (no paper dim step).
        var whiteOut = w;
        var peakOut = w * HighlightHeadroom;
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

        // Shoulder 1..Lc only: display white → headroom (strictly increasing).
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
