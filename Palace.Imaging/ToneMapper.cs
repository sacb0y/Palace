namespace Palace.Imaging;

/// <summary>
/// Pure HDR→display tonemap. ExposureEV scales normalized linear by
/// <c>2^EV</c> before the curve; Analyze / stored Lc are untouched. The curve
/// uses <c>Lc·2^EV</c> so positive EV rolls off instead of hard-clipping (P2).
/// </summary>
public static class ToneMapper
{
    public static void MapPixel(
        float r,
        float g,
        float b,
        in ToneMapParams p,
        out float or,
        out float og,
        out float ob)
    {
        var gain = p.ExposureGain;
        r *= gain;
        g *= gain;
        b *= gain;

        var maxRgb = Math.Max(r, Math.Max(g, b));
        if (maxRgb <= 1e-12f)
        {
            or = og = ob = 0f;
            return;
        }

        var mapped = MapScalar(maxRgb, in p);
        var scale = mapped / maxRgb;
        or = r * scale;
        og = g * scale;
        ob = b * scale;

        var ld = OutputLimit(in p);
        // N2: desat when tone-mapped peak exceeds Ld (smoothstep Ld..1.25·Ld).
        // Engages whenever any channel sits above Ld after the curve (e.g. HDR
        // headroom or a future SDR shoulder above white) — not a no-op.
        GamutMap.DesaturateHighlights(ref or, ref og, ref ob, ld);
        // SDR: clip at display white (≤ 1.0 × PeakNormalized). HDR: at Ld.
        // HighlightHeadroom is HDR-target-only — do not multiply SDR clip peak.
        GamutMap.HighlightSoftClip(ref or, ref og, ref ob, ld);
    }

    /// <summary>
    /// Map a single maxRGB / luminance scalar (post-exposure). Uses
    /// <c>Lc·ExposureGain</c> as the curve peak so +EV extends the shoulder.
    /// </summary>
    public static float MapScalar(float x, in ToneMapParams p)
    {
        var stats = p.Stats;
        // P2: roll off the boosted range; Analyze Lc itself stays unchanged.
        var lcEff = Math.Max(stats.Lc * Math.Max(p.ExposureGain, 1e-6f), 1.01f);

        if (p.UseHdrPath)
        {
            var ld = p.Display.Ld;
            if (lcEff <= ld + 1e-6f)
            {
                return Math.Min(x, lcEff);
            }

            return Bt2390Eetf.MapLinear(x, lcEff, ld);
        }

        var displayWhite = Math.Max(p.Display.PeakNormalized, 1e-6f);
        return SdrToneCurve.Map(x, lcEff, stats.P50, displayWhite);
    }

    public static float[] MapFrame(NormalizedFrame frame, in ToneMapParams p)
    {
        var count = frame.PixelCount;
        var src = frame.Rgba;
        var dst = new float[count * 4];
        for (var i = 0; i < count; i++)
        {
            var o = i * 4;
            MapPixel(src[o], src[o + 1], src[o + 2], in p, out var r, out var g, out var b);
            dst[o] = r;
            dst[o + 1] = g;
            dst[o + 2] = b;
            dst[o + 3] = src[o + 3];
        }

        return dst;
    }

    private static float OutputLimit(in ToneMapParams p) =>
        p.UseHdrPath
            ? p.Display.Ld
            : Math.Max(p.Display.PeakNormalized, 1e-6f);
}
