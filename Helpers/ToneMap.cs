namespace Palace.Helpers;

/// <summary>
/// Residual scRGB helpers after Imaging owns thumbs / full / HDR present (P6).
/// <see cref="ScrgbToBgra8"/> is hard-clip + hue-preserving peak fold only —
/// no Reinhard / MidGrey Analyze. Gallery present uses
/// <see cref="MapPresentRgb"/> for scale / clip / SoftNegatives (ReinhardWhite
/// ignored). Prefer <see cref="ImagingThumbToneMap"/> /
/// <see cref="ImagingFullToneMap"/> / <see cref="ImagingHdrPresentToneMap"/>.
/// </summary>
public static class ToneMap
{
    /// <summary>
    /// Returns BGRA8 (straight alpha) for an RGBA float buffer. Hard residual
    /// when Imaging thumb encode fails: clamp via hue-preserving peak fold,
    /// then sRGB OETF. No exposure or Reinhard shoulder.
    /// </summary>
    public static byte[] ScrgbToBgra8(ReadOnlySpan<float> rgba, int width, int height, CancellationToken cancellationToken = default)
    {
        var count = checked(width * height);
        if (rgba.Length < count * 4)
        {
            throw new ArgumentException("Pixel buffer is smaller than width × height × 4.", nameof(rgba));
        }

        var output = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            if ((i & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var r = Sanitize(rgba[i * 4]);
            var g = Sanitize(rgba[i * 4 + 1]);
            var b = Sanitize(rgba[i * 4 + 2]);
            var a = Math.Clamp(Sanitize(rgba[i * 4 + 3]), 0f, 1f);

            // Keep hue when a channel exceeds 1 (no Reinhard).
            var peak = Math.Max(r, Math.Max(g, b));
            if (peak > 1f)
            {
                r /= peak;
                g /= peak;
                b /= peak;
            }

            output[i * 4] = ToSrgb8(b);
            output[i * 4 + 1] = ToSrgb8(g);
            output[i * 4 + 2] = ToSrgb8(r);
            output[i * 4 + 3] = (byte)MathF.Round(a * 255f);
        }

        return output;
    }

    public static float Luminance(float r, float g, float b) =>
        0.2126f * r + 0.7152f * g + 0.0722f * b;

    public static byte ToSrgb8(float linear)
    {
        var v = Math.Clamp(linear, 0f, 1f);
        var s = v <= 0.0031308f ? 12.92f * v : 1.055f * MathF.Pow(v, 1f / 2.4f) - 0.055f;
        return (byte)MathF.Round(Math.Clamp(s, 0f, 1f) * 255f);
    }

    /// <summary>
    /// Gallery swapchain present map. Scale → optional OutputScale lift → clip.
    /// <paramref name="reinhardWhite"/> is ignored (P6; Imaging owns the curve).
    /// HDR passthrough: scale 1, clip peak/80, SoftNegatives passes OOG through.
    /// SDR: plain-clamp negatives (no SoftNegChannel — that darkened luma).
    /// </summary>
    public static void MapPresentRgb(
        float r,
        float g,
        float b,
        float scale,
        float clipScrgb,
        float reinhardWhite,
        bool softNegatives,
        out float or,
        out float og,
        out float ob,
        float outputScale = 1f)
    {
        _ = reinhardWhite;
        r = FiniteOrZero(r);
        g = FiniteOrZero(g);
        b = FiniteOrZero(b);
        if (!softNegatives)
        {
            // SDR: plain clamp before scale (no SoftNegChannel luma darken).
            r = Math.Max(0f, r);
            g = Math.Max(0f, g);
            b = Math.Max(0f, b);
        }

        r *= scale;
        g *= scale;
        b *= scale;

        // M1: Imaging / absolute map → ×SdrWhite/80; fold/clip target is ClipScrgb.
        if (outputScale > 0f && float.IsFinite(outputScale) && outputScale != 1f)
        {
            r *= outputScale;
            g *= outputScale;
            b *= outputScale;
        }

        var hi = clipScrgb > 0 && float.IsFinite(clipScrgb) ? clipScrgb : 1f;
        if (softNegatives)
        {
            // HDR FP16: keep BT.2020→709 OOG negatives; only clip the high end.
            or = Math.Min(r, hi);
            og = Math.Min(g, hi);
            ob = Math.Min(b, hi);
        }
        else
        {
            or = Math.Clamp(r, 0f, hi);
            og = Math.Clamp(g, 0f, hi);
            ob = Math.Clamp(b, 0f, hi);
        }
    }

    private static float FiniteOrZero(float v) =>
        float.IsFinite(v) ? v : 0f;

    private static float Sanitize(float v) =>
        float.IsFinite(v) && v > 0f ? v : 0f;
}
