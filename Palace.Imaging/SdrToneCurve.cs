namespace Palace.Imaging;

/// <summary>
/// Palace SDR tone curve. Anchors working-space reference white (1.0) to
/// display white. Scene average (P50) sets the knee (0.5–0.7× ref white).
/// A real shoulder runs from the knee through ref white up to Lc with
/// headroom above white so the curve stays strictly increasing (soft-clip
/// after MapPixel brings peaks back to the display limit).
/// </summary>
public static class SdrToneCurve
{
    public const float DefaultKneeFraction = 0.6f;

    /// <summary>
    /// Output at Lc as a multiple of display white (MapScalar headroom before soft-clip).
    /// </summary>
    public const float HighlightHeadroom = 1.12f;

    /// <summary>
    /// Map post-exposure working-space maxRGB to display-referred linear
    /// (1.0 = 203 nits). <paramref name="displayWhite"/> is PeakNormalized.
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
        var kneeOut = kneeIn * w;
        var whiteOut = w;
        var peakOut = w * HighlightHeadroom;
        // Soften into white so the knee position changes mid-tone shape (S4).
        var whiteSlope = 0.35f * w;

        if (x <= kneeIn)
        {
            return x * w;
        }

        if (x <= 1f)
        {
            // Hermite knee → ref white: end slope softens (not flat identity).
            return Hermite(x, kneeIn, kneeOut, w, 1f, whiteOut, whiteSlope);
        }

        if (x >= peak)
        {
            return peakOut;
        }

        // Real shoulder ref white → Lc. Slopes clamped to the secant so the
        // cubic stays monotonic (no Hermite overshoot then dip).
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
        // Scale slopes into t-domain: M = m * dx.
        var M0 = m0 * dx;
        var M1 = m1 * dx;
        return ((2f * t3) - (3f * t2) + 1f) * y0
            + ((t3 - (2f * t2) + t) * M0)
            + (((-2f * t3) + (3f * t2)) * y1)
            + ((t3 - t2) * M1);
    }
}
