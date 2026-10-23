namespace Palace.Imaging;

/// <summary>
/// Decode-buffer → BT.2020 linear working space (1.0 = 203 nits).
/// Strips NaN/Inf; clamps negative luminance; flags source clipping.
/// </summary>
public static class Normalize
{
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
        var eps = ImagingConstants.EncodedMaxEpsilon;

        // Mastering / encoded peak for clip detection. Linear EXR/JXR without
        // metadata peak: do NOT use buffer max (S1) — that falsely flags every
        // highlight-max frame. Require MetadataPeakNits (or encoded 0..1 max).
        float? clipCeil = options.Transfer switch
        {
            TransferKind.Pq or TransferKind.Hlg or TransferKind.Sdr => 1f,
            TransferKind.Scrgb => options.MetadataPeakNits is > 0f
                ? options.MetadataPeakNits.Value / ImagingConstants.ScrgbNits
                : null,
            TransferKind.LinearScene => options.MetadataPeakNits is > 0f
                ? options.MetadataPeakNits.Value / Math.Max(options.EffectiveWhiteNits, 1e-6f)
                : null,
            _ => null
        };

        // B3: custom chromaticity matrix once per frame (not per pixel).
        Span<float> customM = stackalloc float[9];
        var useCustom = options.UsesCustomChromaticities
            && options.Chromaticities is { } chrom
            && Primaries.TryFillBt2020Matrix(chrom, customM);

        for (var i = 0; i < count; i++)
        {
            var o = i * 4;
            var er = ColorMath.Sanitize(rgba[o]);
            var eg = ColorMath.Sanitize(rgba[o + 1]);
            var eb = ColorMath.Sanitize(rgba[o + 2]);
            var ea = Math.Clamp(ColorMath.Sanitize(rgba[o + 3]), 0f, 1f);

            if (IsSourceClipSample(er, eg, eb, options.Transfer, clipCeil, eps))
            {
                clipHits++;
            }

            ToWorking(
                er, eg, eb, options,
                useCustom ? customM : ReadOnlySpan<float>.Empty,
                out var r, out var g, out var b);
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

    public static void Pixel(
        float er,
        float eg,
        float eb,
        NormalizeOptions options,
        out float r,
        out float g,
        out float b)
    {
        Span<float> customM = stackalloc float[9];
        var useCustom = options.UsesCustomChromaticities
            && options.Chromaticities is { } chrom
            && Primaries.TryFillBt2020Matrix(chrom, customM);
        ToWorking(
            ColorMath.Sanitize(er),
            ColorMath.Sanitize(eg),
            ColorMath.Sanitize(eb),
            options,
            useCustom ? customM : ReadOnlySpan<float>.Empty,
            out r,
            out g,
            out b);
        ClampNegLuminance(ref r, ref g, ref b);
    }

    private static bool IsSourceClipSample(
        float er,
        float eg,
        float eb,
        TransferKind transfer,
        float? clipCeil,
        float eps)
    {
        if (transfer is TransferKind.Pq or TransferKind.Hlg or TransferKind.Sdr)
        {
            return er >= 1f - eps && eg >= 1f - eps && eb >= 1f - eps;
        }

        if (clipCeil is not > 0f)
        {
            return false;
        }

        var c = clipCeil.Value;
        // Near the buffer/mastering max on all channels.
        return er >= c * (1f - eps) && eg >= c * (1f - eps) && eb >= c * (1f - eps);
    }

    private static void ToWorking(
        float er,
        float eg,
        float eb,
        NormalizeOptions options,
        ReadOnlySpan<float> customMatrix,
        out float r,
        out float g,
        out float b)
    {
        var inv203 = 1f / ImagingConstants.RefWhiteNits;
        switch (options.Transfer)
        {
            case TransferKind.Pq:
                r = ColorMath.PqEotf(er) * ColorMath.PqPeakNits * inv203;
                g = ColorMath.PqEotf(eg) * ColorMath.PqPeakNits * inv203;
                b = ColorMath.PqEotf(eb) * ColorMath.PqPeakNits * inv203;
                // A2: honor options.Primaries after EOTF (Bt2020 = no-op).
                ApplyPrimariesAfterEotf(ref r, ref g, ref b, options.EffectivePrimaries);
                return;

            case TransferKind.Hlg:
            {
                var sr = ColorMath.HlgEotf(er);
                var sg = ColorMath.HlgEotf(eg);
                var sb = ColorMath.HlgEotf(eb);
                ColorMath.HlgOotfRgb(sr, sg, sb, out var nr, out var ng, out var nb);
                r = nr * inv203;
                g = ng * inv203;
                b = nb * inv203;
                ApplyPrimariesAfterEotf(ref r, ref g, ref b, options.EffectivePrimaries);
                return;
            }

            case TransferKind.Scrgb:
                // Windows scRGB is BT.709 primaries, 1.0 = 80 nits.
                r = er * ImagingConstants.ScrgbNits * inv203;
                g = eg * ImagingConstants.ScrgbNits * inv203;
                b = eb * ImagingConstants.ScrgbNits * inv203;
                Primaries.ToBt2020(r, g, b, ColorPrimaries.Bt709, out r, out g, out b);
                return;

            case TransferKind.LinearScene:
            {
                // x * whiteLuminance / 203 (whiteLuminance in nits).
                var w = Math.Max(options.EffectiveWhiteNits, 1e-6f);
                r = er * w * inv203;
                g = eg * w * inv203;
                b = eb * w * inv203;
                if (customMatrix.Length >= 9)
                {
                    Primaries.ApplyMatrix(r, g, b, customMatrix, out r, out g, out b);
                    return;
                }

                Primaries.ToBt2020(r, g, b, options.EffectivePrimaries, out r, out g, out b);
                return;
            }

            default:
                // SDR sRGB OETF; A2: convert from options.Primaries (not hard-coded 709).
                r = ColorMath.SrgbEotf(er);
                g = ColorMath.SrgbEotf(eg);
                b = ColorMath.SrgbEotf(eb);
                Primaries.ToBt2020(r, g, b, options.EffectivePrimaries, out r, out g, out b);
                return;
        }
    }

    /// <summary>
    /// After EOTF, convert linear light from <paramref name="source"/> into
    /// BT.2020 when the option primaries are not already BT.2020.
    /// </summary>
    private static void ApplyPrimariesAfterEotf(
        ref float r,
        ref float g,
        ref float b,
        ColorPrimaries source)
    {
        if (source == ColorPrimaries.Bt2020)
        {
            return;
        }

        Primaries.ToBt2020(r, g, b, source, out r, out g, out b);
    }

    private static void ClampNegLuminance(ref float r, ref float g, ref float b)
    {
        var y = ImagingConstants.Bt2020Y(r, g, b);
        if (y >= 0f)
        {
            return;
        }

        var peak = Math.Max(Math.Abs(r), Math.Max(Math.Abs(g), Math.Abs(b)));
        if (peak < 1e-12f)
        {
            r = g = b = 0f;
            return;
        }

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
