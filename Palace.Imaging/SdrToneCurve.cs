namespace Palace.Imaging;

/// <summary>
/// Palace SDR tone curve. Scene average (P50) sets the knee (0.5–0.7× ref white).
/// When content fits (Lc ≈ 1), working 1.0 anchors to display white. When Lc &gt; 1,
/// paper white sits slightly below display white so a real shoulder can rise and
/// reach <b>display white exactly at Lc</b> (P1). Soft-clip after MapPixel is only
/// a safety net — the curve never relies on headroom above display white.
/// </summary>
public static class SdrToneCurve
{
    public const float DefaultKneeFraction = 0.6f;

    /// <summary>
    /// When Lc &gt; 1, fraction of display white used for working-space 1.0 so the
    /// shoulder has room to reach display white at Lc.
    /// </summary>
    public const float PaperWhiteWhenHdr = 0.90f;

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
        // P1: display white exactly at Lc — no headroom above w.
        var peakOut = w;
        var whiteOut = peak > 1.01f + 1e-4f ? w * PaperWhiteWhenHdr : w;
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

        // Shoulder ref white → Lc: whiteOut → display white. Monotonic slopes.
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
