namespace Palace.Imaging;

/// <summary>
/// Linear RGB primary conversions into the BT.2020 working space.
/// Matrices are standard D65 Bradford-adapted transforms (public CIE data).
/// </summary>
public static class Primaries
{
    // BT.2020 / D65 chromaticities (same as Chromaticities.Bt2020).
    private static readonly float[] Bt2020Xy =
        [0.708f, 0.292f, 0.170f, 0.797f, 0.131f, 0.046f, 0.3127f, 0.3290f];

    public static void ToBt2020(
        float r,
        float g,
        float b,
        ColorPrimaries source,
        out float or,
        out float og,
        out float ob)
    {
        switch (source)
        {
            case ColorPrimaries.Bt709:
                // BT.709 → BT.2020 (D65).
                or = (0.6274040f * r) + (0.3292820f * g) + (0.0433136f * b);
                og = (0.0690970f * r) + (0.9195400f * g) + (0.0113610f * b);
                ob = (0.0163916f * r) + (0.0880130f * g) + (0.8955950f * b);
                return;

            case ColorPrimaries.DisplayP3:
                // Display P3 → BT.2020 (D65).
                or = (0.7538330f * r) + (0.1985970f * g) + (0.0475700f * b);
                og = (0.0457440f * r) + (0.9417770f * g) + (0.0124790f * b);
                ob = (-0.0012103f * r) + (0.0176020f * g) + (0.9836090f * b);
                return;

            default:
                or = r;
                og = g;
                ob = b;
                return;
        }
    }

    /// <summary>
    /// P2: linear RGB in arbitrary OpenEXR chromaticities → BT.2020 / D65
    /// (Bradford white adapt). Degenerate / non-finite xy → identity (caller
    /// should fall back to <see cref="ToBt2020(float,float,float,ColorPrimaries,out float,out float,out float)"/>).
    /// </summary>
    public static bool TryToBt2020(
        float r,
        float g,
        float b,
        Chromaticities chrom,
        out float or,
        out float og,
        out float ob)
    {
        or = r;
        og = g;
        ob = b;
        if (chrom.MatchKnown() is { } known)
        {
            ToBt2020(r, g, b, known, out or, out og, out ob);
            return true;
        }

        var m = RgbToBt2020Matrix(chrom);
        if (m is null)
        {
            return false;
        }

        or = (float)((m[0] * r) + (m[1] * g) + (m[2] * b));
        og = (float)((m[3] * r) + (m[4] * g) + (m[5] * b));
        ob = (float)((m[6] * r) + (m[7] * g) + (m[8] * b));
        return true;
    }

    /// <summary>Row-major 3×3 RGB(source) → RGB(BT.2020), or null if degenerate.</summary>
    public static double[]? RgbToBt2020Matrix(Chromaticities chrom)
    {
        Span<float> srcXy =
        [
            chrom.Rx, chrom.Ry, chrom.Gx, chrom.Gy, chrom.Bx, chrom.By, chrom.Wx, chrom.Wy
        ];
        var src = RgbToXyz(srcXy);
        var dst = RgbToXyz(Bt2020Xy);
        if (src is null || dst is null || Invert(dst) is not { } xyzTo2020)
        {
            return null;
        }

        var adapt = Bradford(chrom.Wx, chrom.Wy, Bt2020Xy[6], Bt2020Xy[7]);
        return Mul(xyzTo2020, Mul(adapt, src));
    }

    private static double[]? RgbToXyz(ReadOnlySpan<float> c)
    {
        if (c.Length < 8)
        {
            return null;
        }

        for (var i = 0; i < 8; i++)
        {
            if (!float.IsFinite(c[i]))
            {
                return null;
            }
        }

        if (c[1] == 0f || c[3] == 0f || c[5] == 0f || c[7] <= 0f || c[6] <= 0f)
        {
            return null;
        }

        double xr = c[0] / c[1], yr = 1, zr = (1 - c[0] - c[1]) / c[1];
        double xg = c[2] / c[3], yg = 1, zg = (1 - c[2] - c[3]) / c[3];
        double xb = c[4] / c[5], yb = 1, zb = (1 - c[4] - c[5]) / c[5];
        double Xw = c[6] / c[7], Yw = 1, Zw = (1 - c[6] - c[7]) / c[7];
        var prim = new[] { xr, xg, xb, yr, yg, yb, zr, zg, zb };
        if (Invert(prim) is not { } inv)
        {
            return null;
        }

        var s = MulVec(inv, Xw, Yw, Zw);
        return
        [
            s[0] * xr, s[1] * xg, s[2] * xb,
            s[0] * yr, s[1] * yg, s[2] * yb,
            s[0] * zr, s[1] * zg, s[2] * zb
        ];
    }

    private static double[] Bradford(float srcWx, float srcWy, float dstWx, float dstWy)
    {
        // Hunt-Pointer-Estevez / Bradford cone response (public CIE data).
        double[] b =
        [
            0.8951, 0.2664, -0.1614,
            -0.7502, 1.7135, 0.0367,
            0.0389, -0.0685, 1.0296
        ];
        var src = Cone(b, srcWx / srcWy, 1, (1 - srcWx - srcWy) / srcWy);
        var dst = Cone(b, dstWx / dstWy, 1, (1 - dstWx - dstWy) / dstWy);
        var scale = new[]
        {
            dst[0] / src[0], 0, 0,
            0, dst[1] / src[1], 0,
            0, 0, dst[2] / src[2]
        };
        return Mul(Invert(b)!, Mul(scale, b));
    }

    private static double[] Cone(double[] m, double x, double y, double z) =>
        MulVec(m, x, y, z);

    private static double[] Mul(double[] a, double[] b) =>
    [
        (a[0] * b[0]) + (a[1] * b[3]) + (a[2] * b[6]),
        (a[0] * b[1]) + (a[1] * b[4]) + (a[2] * b[7]),
        (a[0] * b[2]) + (a[1] * b[5]) + (a[2] * b[8]),
        (a[3] * b[0]) + (a[4] * b[3]) + (a[5] * b[6]),
        (a[3] * b[1]) + (a[4] * b[4]) + (a[5] * b[7]),
        (a[3] * b[2]) + (a[4] * b[5]) + (a[5] * b[8]),
        (a[6] * b[0]) + (a[7] * b[3]) + (a[8] * b[6]),
        (a[6] * b[1]) + (a[7] * b[4]) + (a[8] * b[7]),
        (a[6] * b[2]) + (a[7] * b[5]) + (a[8] * b[8])
    ];

    private static double[] MulVec(double[] m, double x, double y, double z) =>
    [
        (m[0] * x) + (m[1] * y) + (m[2] * z),
        (m[3] * x) + (m[4] * y) + (m[5] * z),
        (m[6] * x) + (m[7] * y) + (m[8] * z)
    ];

    private static double[]? Invert(double[] m)
    {
        var det =
            (m[0] * ((m[4] * m[8]) - (m[5] * m[7])))
            - (m[1] * ((m[3] * m[8]) - (m[5] * m[6])))
            + (m[2] * ((m[3] * m[7]) - (m[4] * m[6])));
        if (Math.Abs(det) < 1e-12)
        {
            return null;
        }

        var invDet = 1.0 / det;
        return
        [
            ((m[4] * m[8]) - (m[5] * m[7])) * invDet,
            ((m[2] * m[7]) - (m[1] * m[8])) * invDet,
            ((m[1] * m[5]) - (m[2] * m[4])) * invDet,
            ((m[5] * m[6]) - (m[3] * m[8])) * invDet,
            ((m[0] * m[8]) - (m[2] * m[6])) * invDet,
            ((m[2] * m[3]) - (m[0] * m[5])) * invDet,
            ((m[3] * m[7]) - (m[4] * m[6])) * invDet,
            ((m[1] * m[6]) - (m[0] * m[7])) * invDet,
            ((m[0] * m[4]) - (m[1] * m[3])) * invDet
        ];
    }
}
