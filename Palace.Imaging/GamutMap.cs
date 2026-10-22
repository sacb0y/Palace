namespace Palace.Imaging;

/// <summary>
/// Highlight desaturation + Palace rational soft-clip at constant Y
/// (<see cref="HighlightSoftClip"/>).
/// </summary>
public static class GamutMap
{
    /// <summary>
    /// Desaturate toward luminance when channel peak approaches
    /// <paramref name="ld"/>: <c>smoothstep(0.8*Ld, Ld, out)</c>.
    /// </summary>
    public static void DesaturateHighlights(ref float r, ref float g, ref float b, float ld)
    {
        if (ld <= 1e-6f)
        {
            return;
        }

        var peak = Math.Max(r, Math.Max(g, b));
        var t = ColorMath.Smoothstep(0.8f * ld, ld, peak);
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
    /// Soft-clip RGB into [0, <paramref name="peak"/>] with a rational shoulder
    /// (linear below 0.8·peak, then <c>knee + j·t/(t+j)</c>) while restoring
    /// BT.2020 Y. Not a third-party “Mobius tonemapper” port — Palace highlight
    /// gamut compression only.
    /// </summary>
    public static void HighlightSoftClip(ref float r, ref float g, ref float b, float peak)
    {
        if (peak <= 0f)
        {
            r = g = b = 0f;
            return;
        }

        var y0 = ImagingConstants.Bt2020Y(r, g, b);
        r = SoftClipChannel(r, peak);
        g = SoftClipChannel(g, peak);
        b = SoftClipChannel(b, peak);

        var y1 = ImagingConstants.Bt2020Y(r, g, b);
        if (y1 > 1e-8f && Math.Abs(y1 - y0) > 1e-6f && y0 > 0f)
        {
            var k = y0 / y1;
            r *= k;
            g *= k;
            b *= k;
            var m = Math.Max(r, Math.Max(g, b));
            if (m > peak)
            {
                var s = peak / m;
                r *= s;
                g *= s;
                b *= s;
            }
        }
    }

    /// <summary>Per-channel rational shoulder; derivative 1 at the knee.</summary>
    public static float SoftClipChannel(float x, float peak)
    {
        if (x <= 0f)
        {
            return 0f;
        }

        var knee = 0.8f * peak;
        if (x <= knee)
        {
            return x;
        }

        var t = x - knee;
        var j = peak - knee;
        return knee + ((j * t) / (t + j));
    }

    private static float Mix(float a, float b, float t) => a + ((b - a) * t);
}
