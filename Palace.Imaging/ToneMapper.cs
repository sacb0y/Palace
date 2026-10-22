namespace Palace.Imaging;

/// <summary>
/// Pure HDR→display tonemap. ExposureEV scales normalized linear by
/// <c>2^EV</c> before the curve; Analyze / Lc are untouched.
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
        GamutMap.DesaturateHighlights(ref or, ref og, ref ob, ld);
        GamutMap.HighlightSoftClip(ref or, ref og, ref ob, ld);
    }

    /// <summary>Map a single maxRGB / luminance scalar (post-exposure).</summary>
    public static float MapScalar(float x, in ToneMapParams p)
    {
        var stats = p.Stats;
        var lc = stats.Lc;

        // Roll off past Lc after positive EV (S2) — never hard-clip at Lc.
        var shoulder = Math.Max(lc, 1.01f);

        if (p.UseHdrPath)
        {
            var ld = p.Display.Ld;
            if (lc <= ld + 1e-6f)
            {
                // Content fits display; still soft-limit extreme post-EV values
                // via the caller's HighlightSoftClip. Scalar identity below Ld*4.
                return x;
            }

            return Bt2390Eetf.MapLinear(x, shoulder, ld);
        }

        // SDR: anchor ref white → display white; scene average sets knee.
        var displayWhite = Math.Max(p.Display.PeakNormalized, 1e-6f);
        return SdrToneCurve.Map(x, shoulder, stats.P50, displayWhite);
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
