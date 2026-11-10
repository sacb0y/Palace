namespace Palace.Imaging;

/// <summary>
/// Highlight desaturation + soft-clip safety net.
/// </summary>
public static class GamutMap
{
    /// <summary>
    /// Desaturate toward luminance when <b>tone-mapped</b> peak exceeds
    /// display Ld (N2). Uses <c>smoothstep(Ld, 1.25·Ld, peak)</c>.
    /// <para>
    /// <b>Structurally dead under Option A / BT.2390 (P7 B0):</b> maxRGB scale
    /// maps peak to <c>MapScalar(max) ≤ Ld</c> (SDR display white / HDR Ld), so
    /// post-map peak never enters the smoothstep band. Kept as a no-op safety
    /// hook; engagement (A1–A4) needs Isiac A/B — not invented here. Gain maps
    /// stay detect + SDR base (hdr-skiv), not reconstruct.
    /// </para>
    /// </summary>
    public static void DesaturateHighlights(ref float r, ref float g, ref float b, float ld)
    {
        if (ld <= 1e-6f)
        {
            return;
        }

        var peak = Math.Max(r, Math.Max(g, b));
        var t = ColorMath.Smoothstep(ld, 1.25f * ld, peak);
        if (t <= 0f)
        {
            return;
        }

        var y = ImagingConstants.Bt2020Y(r, g, b);
        r = Mix(r, y, t);
        g = Mix(g, y, t);
        b = Mix(b, y, t);
    }

    /// <summary>
    /// Safety net (P1): identity when all channels ≤ peak. Above peak, clamp.
    /// Does not reinflate Y — that previously forced neutrals ≥ white to 1.0.
    /// </summary>
    public static void HighlightSoftClip(ref float r, ref float g, ref float b, float peak)
    {
        if (peak <= 0f)
        {
            r = g = b = 0f;
            return;
        }

        if (Math.Max(r, Math.Max(g, b)) <= peak + 1e-6f)
        {
            return;
        }

        r = SoftClipChannel(r, peak);
        g = SoftClipChannel(g, peak);
        b = SoftClipChannel(b, peak);
    }

    /// <summary>Identity at or below peak; clamp above (curve owns the shoulder).</summary>
    public static float SoftClipChannel(float x, float peak)
    {
        if (x <= 0f)
        {
            return 0f;
        }

        return x <= peak ? x : peak;
    }

    private static float Mix(float a, float b, float t) => a + ((b - a) * t);
}
