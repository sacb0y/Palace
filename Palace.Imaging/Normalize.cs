namespace Palace.Imaging;

/// <summary>
/// Decode-buffer → BT.2020 linear working space (1.0 = 203 nits).
/// Strips NaN/Inf; clamps negative luminance; flags source clipping.
/// </summary>
public static class Normalize
{
    /// <summary>
    /// Convert interleaved RGBA (encoded per <paramref name="options"/>) in place
    /// into a new normalized buffer. Alpha is sanitized and clamped to [0,1].
    /// </summary>
    public static NormalizedFrame Run(
        ReadOnlySpan<float> rgba,
        int width,
        int height,
        NormalizeOptions options)
    {
        var count = checked(width * height);
        if (rgba.Length < count * 4)
        {
            throw new ArgumentException("Pixel buffer is smaller than width × height × 4.", nameof(rgba));
        }

        var output = new float[count * 4];
        var clipHits = 0;
        var encodedMax = options.Transfer is TransferKind.Pq or TransferKind.Hlg or TransferKind.Sdr;
        var eps = ImagingConstants.EncodedMaxEpsilon;

        for (var i = 0; i < count; i++)
        {
            var o = i * 4;
            var er = ColorMath.Sanitize(rgba[o]);
            var eg = ColorMath.Sanitize(rgba[o + 1]);
            var eb = ColorMath.Sanitize(rgba[o + 2]);
            var ea = Math.Clamp(ColorMath.Sanitize(rgba[o + 3]), 0f, 1f);

            if (encodedMax &&
                er >= 1f - eps &&
                eg >= 1f - eps &&
                eb >= 1f - eps)
            {
                clipHits++;
            }

            ToWorking(er, eg, eb, options, out var r, out var g, out var b);
            ClampNegLuminance(ref r, ref g, ref b);

            output[o] = r;
            output[o + 1] = g;
            output[o + 2] = b;
            output[o + 3] = ea;
        }

        var ratio = count > 0 ? (float)clipHits / count : 0f;
        return new NormalizedFrame
        {
            Rgba = output,
            Width = width,
            Height = height,
            SourceClipped = ratio > ImagingConstants.SourceClipFraction,
            SourceClipRatio = ratio,
            MetadataPeakNits = options.MetadataPeakNits,
        };
    }

    /// <summary>Single-pixel normalize (tests / LUT probes).</summary>
    public static void Pixel(
        float er,
        float eg,
        float eb,
        NormalizeOptions options,
        out float r,
        out float g,
        out float b)
    {
        ToWorking(
            ColorMath.Sanitize(er),
            ColorMath.Sanitize(eg),
            ColorMath.Sanitize(eb),
            options,
            out r,
            out g,
            out b);
        ClampNegLuminance(ref r, ref g, ref b);
    }

    private static void ToWorking(
        float er,
        float eg,
        float eb,
        NormalizeOptions options,
        out float r,
        out float g,
        out float b)
    {
        var scale = 1f / ImagingConstants.RefWhiteNits;
        switch (options.Transfer)
        {
            case TransferKind.Pq:
                r = ColorMath.PqEotf(er) * ColorMath.PqPeakNits * scale;
                g = ColorMath.PqEotf(eg) * ColorMath.PqPeakNits * scale;
                b = ColorMath.PqEotf(eb) * ColorMath.PqPeakNits * scale;
                return;

            case TransferKind.Hlg:
            {
                var sr = ColorMath.HlgEotf(er);
                var sg = ColorMath.HlgEotf(eg);
                var sb = ColorMath.HlgEotf(eb);
                ColorMath.HlgOotfRgb(sr, sg, sb, out var nr, out var ng, out var nb);
                r = nr * scale;
                g = ng * scale;
                b = nb * scale;
                return;
            }

            case TransferKind.Scrgb:
                // Windows scRGB: 1.0 = 80 nits → working 1.0 = 203.
                r = er * ImagingConstants.ScrgbNits * scale;
                g = eg * ImagingConstants.ScrgbNits * scale;
                b = eb * ImagingConstants.ScrgbNits * scale;
                return;

            case TransferKind.LinearScene:
            {
                var w = Math.Max(options.EffectiveExrWhite, 1e-6f);
                r = er / w;
                g = eg / w;
                b = eb / w;
                return;
            }

            default:
                // SDR: linearize; encoded 1.0 → working 1.0 (ref white).
                r = ColorMath.SrgbEotf(er);
                g = ColorMath.SrgbEotf(eg);
                b = ColorMath.SrgbEotf(eb);
                return;
        }
    }

    /// <summary>Clamp negative CIE Y toward black while keeping hue ratios.</summary>
    private static void ClampNegLuminance(ref float r, ref float g, ref float b)
    {
        var y = ImagingConstants.Bt2020Y(r, g, b);
        if (y >= 0f)
        {
            return;
        }

        // Scale toward zero so Y becomes 0; if all channels negative, black.
        var peak = Math.Max(Math.Abs(r), Math.Max(Math.Abs(g), Math.Abs(b)));
        if (peak < 1e-12f)
        {
            r = g = b = 0f;
            return;
        }

        // Push negative-Y RGB onto the Y=0 plane by adding a white lift.
        var lift = -y;
        r += lift;
        g += lift;
        b += lift;
        if (ImagingConstants.Bt2020Y(r, g, b) < 0f)
        {
            r = g = b = 0f;
        }
    }
}
