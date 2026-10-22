namespace Palace.Imaging;

/// <summary>
/// 1D maxRGB LUT baked in <b>PQ space</b> (M7). ExposureEV is applied at sample
/// time (<c>2^EV</c>), not baked — live sliders can reuse one LUT (S2).
/// </summary>
public sealed class ToneMapLut
{
    public required float[] Samples { get; init; }
    public required float InputMaxLinear { get; init; }
    public required float InputMinPq { get; init; }
    public required float InputMaxPq { get; init; }
    public required ToneMapParams Params { get; init; }

    public int Size => Samples.Length;

    public static ToneMapLut Bake(in ToneMapParams p, int size = 256)
    {
        if (size < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        // Domain past Lc so +EV still interpolates (roll-off, not hard clip).
        var domainLinear = Math.Max(p.Stats.Lc * 16f, 16f);
        var minPq = ColorMath.NitsToPq(0f);
        var maxPq = ColorMath.NitsToPq(domainLinear * ImagingConstants.RefWhiteNits);
        var curveParams = p with { ExposureEv = 0f };
        var samples = new float[size];
        for (var i = 0; i < size; i++)
        {
            var t = i / (float)(size - 1);
            var pq = minPq + ((maxPq - minPq) * t);
            var linear = ColorMath.PqToNits(pq) / ImagingConstants.RefWhiteNits;
            samples[i] = ToneMapper.MapScalar(linear, in curveParams);
        }

        return new ToneMapLut
        {
            Samples = samples,
            InputMaxLinear = domainLinear,
            InputMinPq = minPq,
            InputMaxPq = maxPq,
            Params = p,
        };
    }

    /// <summary>Sample LUT for a post-exposure maxRGB value (PQ interpolation).</summary>
    public float Sample(float postExposureMaxRgb)
    {
        if (postExposureMaxRgb <= 0f)
        {
            return 0f;
        }

        var pq = ColorMath.NitsToPq(postExposureMaxRgb * ImagingConstants.RefWhiteNits);
        var t = Math.Clamp((pq - InputMinPq) / Math.Max(InputMaxPq - InputMinPq, 1e-8f), 0f, 1f);
        var idx = t * (Size - 1);
        var i0 = (int)MathF.Floor(idx);
        var i1 = Math.Min(Size - 1, i0 + 1);
        var f = idx - i0;
        return (Samples[i0] * (1f - f)) + (Samples[i1] * f);
    }

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

        // P2: baked samples are EV=0 (lc = Analyze Lc). Non-zero EV must
        // recompute Lc·2^EV via MapScalar so the shoulder extends.
        var mapped = Math.Abs(Params.ExposureEv) < 1e-6f
            ? Sample(maxRgb)
            : ToneMapper.MapScalar(maxRgb, Params);
        var scale = mapped / maxRgb;
        or = r * scale;
        og = g * scale;
        ob = b * scale;

        var ld = Params.UseHdrPath
            ? Params.Display.Ld
            : Math.Max(Params.Display.PeakNormalized, 1e-6f);
        GamutMap.DesaturateHighlights(ref or, ref og, ref ob, ld);
        var clipPeak = Params.UseHdrPath ? ld : ld * SdrToneCurve.HighlightHeadroom;
        GamutMap.HighlightSoftClip(ref or, ref og, ref ob, clipPeak);
    }
}

