namespace Palace.Imaging;

/// <summary>
/// Palace SDR tone curve. Anchors working-space reference white (1.0) to
/// display white. Scene average selects a knee in 0.5–0.7× ref white
/// (brightness-matching idea from SMPTE ST 2094-40 — Palace-owned structure).
/// Lc sets the shoulder limit for post-EV roll-off.
/// </summary>
public static class SdrToneCurve
{
    public const float DefaultKneeFraction = 0.6f;

    /// <summary>
    /// Map post-exposure working-space maxRGB to display-referred linear
    /// (1.0 = 203 nits). <paramref name="displayWhite"/> is PeakNormalized
    /// (e.g. 1.0 for 203-nit SDR, 80/203 for 80-nit SDR).
    /// </summary>
    public static float Map(float x, float lc, float sceneAvg, float displayWhite)
    {
        var w = Math.Max(displayWhite, 1e-6f);
        if (x <= 0f)
        {
            return 0f;
        }

        // Knee from scene average (S4); unused for the linear ref-white segment
        // but reserved for future toe shaping below mid-grey.
        var kneeIn = sceneAvg > 0f
            ? Math.Clamp(sceneAvg, 0.5f, 0.7f)
            : DefaultKneeFraction;
        _ = kneeIn;

        // M1: ref white → display white; mid-grey scales with it.
        if (x <= 1f)
        {
            return x * w;
        }

        // Shoulder above ref white → approach display white (SDR peak == white).
        var peak = Math.Max(lc, 1.01f);
        if (x >= peak)
        {
            return w;
        }

        var t = (x - 1f) / (peak - 1f);
        var t2 = t * t;
        var t3 = t2 * t;
        // Hermite: start at w, end at w (no headroom on peak=white SDR).
        return ((2f * t3) - (3f * t2) + 1f) * w
            + (((-2f * t3) + (3f * t2)) * w);
    }
}
