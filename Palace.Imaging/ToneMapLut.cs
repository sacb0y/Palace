namespace Palace.Imaging;

/// <summary>
/// 1D maxRGB LUT baked from full-image stats. ExposureEV is <b>not</b> baked into
/// sample values — callers (CPU or future HLSL) multiply by <c>2^EV</c> before
/// sampling so a live slider can rebake cheaply or skip rebake entirely.
/// Cache keys still include ExposureEV so per-EV CPU snapshots can be stored.
/// </summary>
public sealed class ToneMapLut
{
    public required float[] Samples { get; init; }
    public required float InputMax { get; init; }
    public required ToneMapParams Params { get; init; }

    public int Size => Samples.Length;

    /// <summary>
    /// Bake the curve with <see cref="ToneMapParams.ExposureEv"/> forced to 0
    /// for the scalar map; domain covers <c>Lc</c> (and a little headroom).
    /// </summary>
    public static ToneMapLut Bake(in ToneMapParams p, int size = 256)
    {
        if (size < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        // Curve itself ignores EV; domain uses Lc. Extra headroom so +EV samples
        // still interpolate before Mobius soft-clip.
        var domain = Math.Max(p.Stats.Lc * 4f, 4f);
        var curveParams = p with { ExposureEv = 0f };
        var samples = new float[size];
        for (var i = 0; i < size; i++)
        {
            var t = i / (float)(size - 1);
            var x = t * domain;
            samples[i] = ToneMapper.MapScalar(x, in curveParams);
        }

        return new ToneMapLut
        {
            Samples = samples,
            InputMax = domain,
            Params = p,
        };
    }

    /// <summary>Sample LUT for a post-exposure maxRGB value.</summary>
    public float Sample(float postExposureMaxRgb)
    {
        if (postExposureMaxRgb <= 0f)
        {
            return 0f;
        }

        var t = Math.Clamp(postExposureMaxRgb / InputMax, 0f, 1f);
        var idx = t * (Size - 1);
        var i0 = (int)MathF.Floor(idx);
        var i1 = Math.Min(Size - 1, i0 + 1);
        var f = idx - i0;
        return (Samples[i0] * (1f - f)) + (Samples[i1] * f);
    }

    /// <summary>
    /// Apply <c>2^EV</c> then LUT (maxRGB + ratio preserve + gamut).
    /// </summary>
    public void MapPixel(float r, float g, float b, out float or, out float og, out float ob)
    {
        var gain = Params.ExposureGain;
        r *= gain;
        g *= gain;
        b *= gain;
        var maxRgb = Math.Max(r, Math.Max(g, b));
        if (maxRgb <= 1e-12f)
        {
            or = og = ob = 0f;
            return;
        }

        var mapped = Sample(maxRgb);
        var scale = mapped / maxRgb;
        or = r * scale;
        og = g * scale;
        ob = b * scale;

        var ld = Params.UseHdrPath
            ? Params.Display.Ld
            : Math.Max(Params.Display.PeakNormalized, 1e-6f);
        GamutMap.DesaturateHighlights(ref or, ref og, ref ob, ld);
        GamutMap.MobiusSoftClip(ref or, ref og, ref ob, ld);
    }
}
