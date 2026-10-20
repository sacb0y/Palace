namespace Palace.Helpers;

/// <summary>
/// Linear scRGB (1.0 = SDR white, 80 nits) → 8-bit sRGB for thumbnails and
/// SDR previews of HDR stills. SDR-range content passes through unchanged;
/// HDR content gets a log-average exposure plus extended Reinhard on
/// luminance (hue-preserving), so highlights roll off instead of clipping
/// or turning into a single saturated channel.
/// </summary>
public static class ToneMap
{
    public const float MiddleGrey = 0.18f;

    /// <summary>Returns BGRA8 (straight alpha) for an RGBA float buffer.</summary>
    public static byte[] ScrgbToBgra8(ReadOnlySpan<float> rgba, int width, int height, CancellationToken cancellationToken = default)
    {
        var count = checked(width * height);
        if (rgba.Length < count * 4)
        {
            throw new ArgumentException("Pixel buffer is smaller than width × height × 4.", nameof(rgba));
        }

        var (exposure, white) = Analyze(rgba, count, cancellationToken);
        var output = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            if ((i & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var r = Sanitize(rgba[i * 4]) * exposure;
            var g = Sanitize(rgba[i * 4 + 1]) * exposure;
            var b = Sanitize(rgba[i * 4 + 2]) * exposure;
            var a = Math.Clamp(Sanitize(rgba[i * 4 + 3]), 0f, 1f);
            if (white > 1f)
            {
                var l = Luminance(r, g, b);
                if (l > 0f)
                {
                    var ld = l * (1f + l / (white * white)) / (1f + l);
                    var k = ld / l;
                    r *= k;
                    g *= k;
                    b *= k;
                }
            }

            // Keep hue when a channel still exceeds 1 after the curve.
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

    /// <summary>
    /// Exposure scale and white point (in scaled units). Content whose
    /// luminance stays within SDR range gets (1, 1): no tone mapping.
    /// </summary>
    public static (float Exposure, float White) Analyze(ReadOnlySpan<float> rgba, int count, CancellationToken cancellationToken = default)
    {
        var step = Math.Max(1, count / 65536);
        var samples = new List<float>(Math.Min(count, 65536) + 1);
        var logSum = 0.0;
        for (var i = 0; i < count; i += step)
        {
            if ((samples.Count & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var l = Luminance(Sanitize(rgba[i * 4]), Sanitize(rgba[i * 4 + 1]), Sanitize(rgba[i * 4 + 2]));
            samples.Add(l);
            logSum += Math.Log(1e-4 + l);
        }

        if (samples.Count == 0)
        {
            return (1f, 1f);
        }

        samples.Sort();
        var p995 = samples[Math.Min(samples.Count - 1, (int)(samples.Count * 0.995))];
        if (p995 <= 1.0f)
        {
            return (1f, 1f);
        }

        var logAvg = (float)Math.Exp(logSum / samples.Count);
        var exposure = Math.Clamp(MiddleGrey / Math.Max(logAvg, 1e-4f), 0.05f, 4f);
        var white = Math.Max(1f, p995 * exposure);
        return (exposure, white);
    }

    public static float Luminance(float r, float g, float b) =>
        0.2126f * r + 0.7152f * g + 0.0722f * b;

    public static byte ToSrgb8(float linear)
    {
        var v = Math.Clamp(linear, 0f, 1f);
        var s = v <= 0.0031308f ? 12.92f * v : 1.055f * MathF.Pow(v, 1f / 2.4f) - 0.055f;
        return (byte)MathF.Round(Math.Clamp(s, 0f, 1f) * 255f);
    }

    private static float Sanitize(float v) =>
        float.IsFinite(v) && v > 0f ? v : 0f;
}
