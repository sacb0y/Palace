namespace Palace.Imaging;

/// <summary>
/// Palace SDR tone curve. Ref white (working 1.0) maps to display white for
/// every Lc. Scene average (P50) sets the knee (0.5–0.7× ref white).
/// <para>
/// <b>Held (Isiac A/B):</b> SDR <see cref="Map"/> ends at display white
/// (output ≤ 1.0 × display white). The 1.12× <see cref="HighlightHeadroom"/>
/// is reserved for <b>HDR display targets</b> only — not applied on the SDR
/// path. Shoulder policy above white (dip paper vs clip) awaits Isiac.
/// </para>
/// </summary>
public static class SdrToneCurve
{
    public const float DefaultKneeFraction = 0.6f;

    /// <summary>
    /// Shoulder headroom above display white for <b>HDR targets only</b>.
    /// SDR <see cref="Map"/> uses 1.0 (output ≤ display white).
    /// </summary>
    public const float HighlightHeadroom = 1.12f;

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
        // Ref white → display white for every Lc. SDR: peakOut = w (≤ display white).
        var whiteOut = w;
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

        // 1..Lc: held flat at display white on SDR pending Isiac A/B.
        // (HDR targets use BT.2390 / headroom elsewhere — not this path.)
        if (x >= peak)
        {
            return peakOut;
        }

        // Degenerate Hermite white→white (identity plateau). Kept as a single
        // branch so A (dip paper / rise to white) or B (explicit clip) can land here.
        return whiteOut;
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
