namespace Palace.Imaging;

/// <summary>
/// Transfer functions and PQ↔nits rescale used by Normalize / ToneMap.
/// PQ math matches Rec. ITU-R BT.2100 (ST 2084) coefficients.
/// </summary>
public static class ColorMath
{
    public const float PqPeakNits = 10_000f;
    public const float HlgPeakNits = 1_000f;
    public const float HlgOotfGamma = 1.2f;

    public static float Sanitize(float v) =>
        float.IsFinite(v) ? v : 0f;

    /// <summary>
    /// Hermite smoothstep. If edges are reversed, they are swapped so the
    /// blend still rises with <paramref name="x"/> (never a hard step).
    /// </summary>
    public static float Smoothstep(float edge0, float edge1, float x)
    {
        if (edge0 > edge1)
        {
            (edge0, edge1) = (edge1, edge0);
        }

        if (Math.Abs(edge1 - edge0) < 1e-12f)
        {
            return x < edge0 ? 0f : 1f;
        }

        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    public static float SrgbEotf(float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        return v <= 0.04045f
            ? v / 12.92f
            : (float)Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    public static float SrgbOetf(float linear)
    {
        linear = Math.Max(linear, 0f);
        return linear <= 0.0031308f
            ? 12.92f * linear
            : (1.055f * (float)Math.Pow(linear, 1.0 / 2.4)) - 0.055f;
    }

    /// <summary>ST 2084 EOTF: encoded 0..1 → linear 0..1 (1 = 10 000 nits).</summary>
    public static float PqEotf(float v)
    {
        const double m1 = 2610.0 / 16384.0;
        const double m2 = 2523.0 / 32.0;
        const double c1 = 3424.0 / 4096.0;
        const double c2 = 2413.0 / 128.0;
        const double c3 = 2392.0 / 128.0;
        v = Math.Clamp(v, 0f, 1f);
        var vp = Math.Pow(v, 1.0 / m2);
        var den = c2 - (c3 * vp);
        if (den <= 0)
        {
            return 0f;
        }

        return (float)Math.Pow(Math.Max(vp - c1, 0) / den, 1.0 / m1);
    }

    /// <summary>ST 2084 OETF: linear 0..1 (1 = 10 000 nits) → encoded 0..1.</summary>
    public static float PqOetf(float linear01)
    {
        const double m1 = 2610.0 / 16384.0;
        const double m2 = 2523.0 / 32.0;
        const double c1 = 3424.0 / 4096.0;
        const double c2 = 2413.0 / 128.0;
        const double c3 = 2392.0 / 128.0;
        var y = Math.Clamp(linear01, 0.0, 1.0);
        var ym = Math.Pow(y, m1);
        return (float)Math.Pow((c1 + (c2 * ym)) / (1.0 + (c3 * ym)), m2);
    }

    public static float NitsToPq(float nits) =>
        PqOetf(Math.Max(nits, 0f) / PqPeakNits);

    public static float PqToNits(float pq) =>
        PqEotf(pq) * PqPeakNits;

    public static float HlgEotf(float v)
    {
        const double a = 0.17883277;
        const double b = 0.28466892;
        const double c = 0.55991073;
        v = Math.Clamp(v, 0f, 1f);
        if (v <= 0.5f)
        {
            return (float)((v * v) / 3.0);
        }

        return (float)((Math.Exp((v - c) / a) + b) / 12.0);
    }

    public static float HlgOetf(float scene)
    {
        const double a = 0.17883277;
        const double b = 0.28466892;
        const double c = 0.55991073;
        scene = Math.Max(scene, 0f);
        if (scene <= 1.0 / 12.0)
        {
            return (float)Math.Sqrt(3.0 * scene);
        }

        return (float)(a * Math.Log((12.0 * scene) - b) + c);
    }

    public static void HlgOotfRgb(
        float sceneR,
        float sceneG,
        float sceneB,
        out float nitsR,
        out float nitsG,
        out float nitsB)
    {
        var y = Math.Max(ImagingConstants.Bt2020Y(sceneR, sceneG, sceneB), 0f);
        if (y <= 0f)
        {
            nitsR = nitsG = nitsB = 0f;
            return;
        }

        var k = MathF.Pow(y, HlgOotfGamma - 1f) * HlgPeakNits;
        nitsR = sceneR * k;
        nitsG = sceneG * k;
        nitsB = sceneB * k;
    }
}
