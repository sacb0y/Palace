namespace Palace.Imaging;

/// <summary>
/// libplacebo SDR spline + <c>st2094_pick_knee</c>, ported from
/// <c>src/tone_mapping.c</c> at commit <see cref="ImagingConstants.LibplaceboCommit"/>
/// (LGPL-2.1+). Constants: knee_default 0.4, knee_offset 1.0, slope_tuning 1.5,
/// slope_offset 0.2, spline_contrast 0.5 (libplacebo defaults).
/// </summary>
public static class PlaceboSpline
{
    public const float KneeAdaptation = 0.4f;
    public const float KneeMinimum = 0.1f;
    public const float KneeMaximum = 0.8f;
    public const float KneeDefault = 0.4f;
    public const float SlopeTuning = 1.5f;
    public const float SlopeOffset = 0.2f;
    public const float SplineContrast = 0.5f;

    /// <summary>
    /// Map a single luminance (working-space linear, 1.0 = 203 nits) through the
    /// PQ-space hyperbolic spline from <paramref name="inputMax"/> → <paramref name="outputMax"/>.
    /// </summary>
    public static float MapLinear(
        float linear,
        float inputMin,
        float inputMax,
        float inputAvg,
        float outputMin,
        float outputMax)
    {
        // Function scaling is PQ (absolute ST.2084 0..1).
        var inMinPq = ColorMath.NitsToPq(Math.Max(inputMin, 0f) * ImagingConstants.RefWhiteNits);
        var inMaxPq = ColorMath.NitsToPq(Math.Max(inputMax, 1e-6f) * ImagingConstants.RefWhiteNits);
        var inAvgPq = inputAvg > 0f
            ? ColorMath.NitsToPq(inputAvg * ImagingConstants.RefWhiteNits)
            : 0f;
        var outMinPq = ColorMath.NitsToPq(Math.Max(outputMin, 0f) * ImagingConstants.RefWhiteNits);
        var outMaxPq = ColorMath.NitsToPq(Math.Max(outputMax, 1e-6f) * ImagingConstants.RefWhiteNits);

        // Constrain input peak ≥ ~100 nits in PQ (libplacebo pl_tone_map_params_infer).
        var sdrPq = Math.Min(outMaxPq, ColorMath.NitsToPq(100f));
        inMaxPq = Math.Max(inMaxPq, sdrPq);

        if (inMaxPq <= outMaxPq + 1e-6f && Math.Abs(inMinPq - outMinPq) < 1e-4f)
        {
            return Math.Clamp(linear, outputMin, outputMax);
        }

        var xPq = ColorMath.NitsToPq(Math.Max(linear, 0f) * ImagingConstants.RefWhiteNits);
        xPq = Math.Clamp(xPq, inMinPq, inMaxPq);
        var yPq = SplinePq(xPq, inMinPq, inMaxPq, inAvgPq, outMinPq, outMaxPq);
        yPq = Math.Clamp(yPq, outMinPq, outMaxPq);
        return ColorMath.PqToNits(yPq) / ImagingConstants.RefWhiteNits;
    }

    /// <summary>Pure PQ-domain sample (for LUT bake / shader ports).</summary>
    public static float SplinePq(
        float x,
        float inputMin,
        float inputMax,
        float inputAvg,
        float outputMin,
        float outputMax)
    {
        St2094PickKnee(
            inputMin, inputMax, inputAvg, outputMin, outputMax,
            out var srcPivot, out var dstPivot);

        var slope = (dstPivot - outputMin) / Math.Max(srcPivot - inputMin, 1e-8f);
        var ratio = (inputMax / Math.Max(outputMax, 1e-8f)) - 1f;
        ratio = Math.Clamp(SlopeTuning * ratio, SlopeOffset, 1f + SlopeOffset);
        slope = MathF.Pow(slope, (1f - SplineContrast) * ratio);

        var inMin = inputMin - srcPivot;
        var inMax = inputMax - srcPivot;
        var outMin = outputMin - dstPivot;
        var outMax = outputMax - dstPivot;

        var pk = (slope * inMin - outMin) / Math.Max(outMin * inMin, 1e-12f);
        var qk = (slope * inMax - outMax) / Math.Max(outMax * inMax, 1e-12f);

        x -= srcPivot;
        x = slope * x / (1f + ((x > 0f ? qk : pk) * x));
        return x + dstPivot;
    }

    /// <summary>
    /// libplacebo <c>st2094_pick_knee</c> — inspired by SMPTE ST 2094-10 with
    /// modifications (not a verbatim Annex B.2 implementation).
    /// </summary>
    public static void St2094PickKnee(
        float srcMin,
        float srcMax,
        float srcAvg,
        float dstMin,
        float dstMax,
        out float srcKnee,
        out float dstKnee)
    {
        // Inputs already in PQ.
        var minKnee = KneeMinimum;
        var maxKnee = KneeMaximum;
        var defKnee = KneeDefault;

        var srcKneeMin = Mix(srcMin, srcMax, minKnee);
        var srcKneeMax = Mix(srcMin, srcMax, maxKnee);
        var dstKneeMin = Mix(dstMin, dstMax, minKnee);
        var dstKneeMax = Mix(dstMin, dstMax, maxKnee);

        srcKnee = srcAvg > 0f ? srcAvg : Mix(srcMin, srcMax, defKnee);
        srcKnee = Math.Clamp(srcKnee, srcKneeMin, srcKneeMax);

        var target = (srcKnee - srcMin) / Math.Max(srcMax - srcMin, 1e-8f);
        var adapted = Mix(dstMin, dstMax, target);

        var tuning = 1f
            - (ColorMath.Smoothstep(maxKnee, defKnee, target)
               * ColorMath.Smoothstep(minKnee, defKnee, target));
        var adaptation = Mix(KneeAdaptation, 1f, tuning);
        adaptation = Mix(1f, adaptation, ColorMath.Smoothstep(dstMin, dstKneeMin, srcKnee));

        dstKnee = Mix(srcKnee, adapted, adaptation);
        dstKnee = Math.Clamp(dstKnee, dstKneeMin, dstKneeMax);
    }

    private static float Mix(float a, float b, float t) => a + ((b - a) * t);
}
