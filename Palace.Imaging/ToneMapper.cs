namespace Palace.Imaging;

/// <summary>
/// Pure HDR→display tonemap. ExposureEV scales normalized linear by
/// <c>2^EV</c> before the curve; Analyze / Lc are untouched.
/// </summary>
public static class ToneMapper
{
    /// <summary>Map one working-space RGB triple (already normalized).</summary>
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

        var ld = p.UseHdrPath
            ? p.Display.Ld
            : p.Display.SdrWhiteNormalized;
        // For SDR, Ld is display white in working units (typically ≤ 1).
        if (!p.UseHdrPath)
        {
            ld = Math.Max(p.Display.PeakNormalized, 1e-6f);
        }

        GamutMap.DesaturateHighlights(ref or, ref og, ref ob, ld);
        GamutMap.MobiusSoftClip(ref or, ref og, ref ob, ld);
    }

    /// <summary>Map a single maxRGB / luminance scalar (post-exposure).</summary>
    public static float MapScalar(float x, in ToneMapParams p)
    {
        var stats = p.Stats;
        var lc = stats.Lc;
        if (p.UseHdrPath)
        {
            var ld = p.Display.Ld;
            // Identity when content peak fits the display (BT.2390 path).
            if (lc <= ld + 1e-6f)
            {
                return x;
            }

            return Bt2390Eetf.MapLinear(x, lc, ld);
        }

        // SDR: libplacebo spline. Ref white → display white; Lc sets the shoulder.
        var inputMax = Math.Max(lc, 1.01f);
        var inputAvg = stats.P50 > 0f ? stats.P50 : 0f;
        var outputMax = Math.Max(p.Display.PeakNormalized, 1e-6f);
        return PlaceboSpline.MapLinear(
            x,
            inputMin: 0f,
            inputMax: inputMax,
            inputAvg: inputAvg,
            outputMin: 0f,
            outputMax: outputMax);
    }

    /// <summary>Tonemap an entire normalized frame into a new RGBA buffer.</summary>
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
}
