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
    /// Apply a row-major 3×3 (from <see cref="TryFillBt2020Matrix"/>).
    /// </summary>
    public static void ApplyMatrix(
        float r,
        float g,
        float b,
        ReadOnlySpan<float> m,
        out float or,
        out float og,
        out float ob)
    {
        or = (m[0] * r) + (m[1] * g) + (m[2] * b);
        og = (m[3] * r) + (m[4] * g) + (m[5] * b);
        ob = (m[6] * r) + (m[7] * g) + (m[8] * b);
    }

    /// <summary>
    /// P2: fill a 9-float row-major RGB(source) → RGB(BT.2020) matrix once
    /// per frame (Normalize hoists this out of the pixel loop). Known sets
    /// use the enum matrices via <see cref="ToBt2020"/> instead.
    /// </summary>
    public static bool TryFillBt2020Matrix(Chromaticities chrom, Span<float> m9)
    {
        if (m9.Length < 9)
        {
            return false;
        }

        if (chrom.MatchKnown() is { } known)
        {
            // Encode enum convert as a matrix for a uniform Apply path.
            ToBt2020(1f, 0f, 0f, known, out var r0, out var g0, out var b0);
            ToBt2020(0f, 1f, 0f, known, out var r1, out var g1, out var b1);
            ToBt2020(0f, 0f, 1f, known, out var r2, out var g2, out var b2);
            m9[0] = r0; m9[1] = r1; m9[2] = r2;
            m9[3] = g0; m9[4] = g1; m9[5] = g2;
            m9[6] = b0; m9[7] = b1; m9[8] = b2;
            return true;
        }

        Span<float> srcXy = stackalloc float[8];
        srcXy[0] = chrom.Rx; srcXy[1] = chrom.Ry;
        srcXy[2] = chrom.Gx; srcXy[3] = chrom.Gy;
        srcXy[4] = chrom.Bx; srcXy[5] = chrom.By;
        srcXy[6] = chrom.Wx; srcXy[7] = chrom.Wy;
        Span<double> src = stackalloc double[9];
        Span<double> dst = stackalloc double[9];
        if (!TryRgbToXyz(srcXy, src) || !TryRgbToXyz(Bt2020Xy, dst))
        {
            return false;
        }

        Span<double> xyzTo2020 = stackalloc double[9];
        if (!TryInvert(dst, xyzTo2020))
        {
            return false;
        }

        Span<double> adapt = stackalloc double[9];
        Bradford(chrom.Wx, chrom.Wy, Bt2020Xy[6], Bt2020Xy[7], adapt);
        Span<double> tmp = stackalloc double[9];
        Mul(adapt, src, tmp);
        Span<double> m = stackalloc double[9];
        Mul(xyzTo2020, tmp, m);
        for (var i = 0; i < 9; i++)
        {
            m9[i] = (float)m[i];
        }

        return true;
    }

    /// <summary>
    /// Convenience: build matrix then apply (tests / single-pixel). Prefer
    /// <see cref="TryFillBt2020Matrix"/> once per <see cref="Normalize.Run"/>.
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
        Span<float> m = stackalloc float[9];
        if (!TryFillBt2020Matrix(chrom, m))
        {
            or = r;
            og = g;
            ob = b;
            return false;
        }

        ApplyMatrix(r, g, b, m, out or, out og, out ob);
        return true;
    }

    private static bool TryRgbToXyz(ReadOnlySpan<float> c, Span<double> m9)
    {
        if (c.Length < 8 || m9.Length < 9)
        {
            return false;
        }

        for (var i = 0; i < 8; i++)
        {
            if (!float.IsFinite(c[i]))
            {
                return false;
            }
        }

        if (c[1] == 0f || c[3] == 0f || c[5] == 0f || c[7] <= 0f || c[6] <= 0f)
        {
            return false;
        }

        double xr = c[0] / c[1], yr = 1, zr = (1 - c[0] - c[1]) / c[1];
        double xg = c[2] / c[3], yg = 1, zg = (1 - c[2] - c[3]) / c[3];
        double xb = c[4] / c[5], yb = 1, zb = (1 - c[4] - c[5]) / c[5];
        double Xw = c[6] / c[7], Yw = 1, Zw = (1 - c[6] - c[7]) / c[7];
        Span<double> prim = stackalloc double[9];
        prim[0] = xr; prim[1] = xg; prim[2] = xb;
        prim[3] = yr; prim[4] = yg; prim[5] = yb;
        prim[6] = zr; prim[7] = zg; prim[8] = zb;
        Span<double> inv = stackalloc double[9];
        if (!TryInvert(prim, inv))
        {
            return false;
        }

        var s0 = (inv[0] * Xw) + (inv[1] * Yw) + (inv[2] * Zw);
        var s1 = (inv[3] * Xw) + (inv[4] * Yw) + (inv[5] * Zw);
        var s2 = (inv[6] * Xw) + (inv[7] * Yw) + (inv[8] * Zw);
        m9[0] = s0 * xr; m9[1] = s1 * xg; m9[2] = s2 * xb;
        m9[3] = s0 * yr; m9[4] = s1 * yg; m9[5] = s2 * yb;
        m9[6] = s0 * zr; m9[7] = s1 * zg; m9[8] = s2 * zb;
        return true;
    }

    private static void Bradford(float srcWx, float srcWy, float dstWx, float dstWy, Span<double> out9)
    {
        // Hunt-Pointer-Estevez / Bradford cone response (public CIE data).
        Span<double> b = stackalloc double[9];
        b[0] = 0.8951; b[1] = 0.2664; b[2] = -0.1614;
        b[3] = -0.7502; b[4] = 1.7135; b[5] = 0.0367;
        b[6] = 0.0389; b[7] = -0.0685; b[8] = 1.0296;
        var sx = srcWx / srcWy;
        var sz = (1 - srcWx - srcWy) / srcWy;
        var dx = dstWx / dstWy;
        var dz = (1 - dstWx - dstWy) / dstWy;
        var src0 = (b[0] * sx) + (b[1] * 1) + (b[2] * sz);
        var src1 = (b[3] * sx) + (b[4] * 1) + (b[5] * sz);
        var src2 = (b[6] * sx) + (b[7] * 1) + (b[8] * sz);
        var dst0 = (b[0] * dx) + (b[1] * 1) + (b[2] * dz);
        var dst1 = (b[3] * dx) + (b[4] * 1) + (b[5] * dz);
        var dst2 = (b[6] * dx) + (b[7] * 1) + (b[8] * dz);
        Span<double> scale = stackalloc double[9];
        scale[0] = dst0 / src0; scale[1] = 0; scale[2] = 0;
        scale[3] = 0; scale[4] = dst1 / src1; scale[5] = 0;
        scale[6] = 0; scale[7] = 0; scale[8] = dst2 / src2;
        Span<double> invB = stackalloc double[9];
        TryInvert(b, invB);
        Span<double> tmp = stackalloc double[9];
        Mul(scale, b, tmp);
        Mul(invB, tmp, out9);
    }

    private static void Mul(ReadOnlySpan<double> a, ReadOnlySpan<double> b, Span<double> o)
    {
        o[0] = (a[0] * b[0]) + (a[1] * b[3]) + (a[2] * b[6]);
        o[1] = (a[0] * b[1]) + (a[1] * b[4]) + (a[2] * b[7]);
        o[2] = (a[0] * b[2]) + (a[1] * b[5]) + (a[2] * b[8]);
        o[3] = (a[3] * b[0]) + (a[4] * b[3]) + (a[5] * b[6]);
        o[4] = (a[3] * b[1]) + (a[4] * b[4]) + (a[5] * b[7]);
        o[5] = (a[3] * b[2]) + (a[4] * b[5]) + (a[5] * b[8]);
        o[6] = (a[6] * b[0]) + (a[7] * b[3]) + (a[8] * b[6]);
        o[7] = (a[6] * b[1]) + (a[7] * b[4]) + (a[8] * b[7]);
        o[8] = (a[6] * b[2]) + (a[7] * b[5]) + (a[8] * b[8]);
    }

    private static bool TryInvert(ReadOnlySpan<double> m, Span<double> o)
    {
        var det =
            (m[0] * ((m[4] * m[8]) - (m[5] * m[7])))
            - (m[1] * ((m[3] * m[8]) - (m[5] * m[6])))
            + (m[2] * ((m[3] * m[7]) - (m[4] * m[6])));
        if (Math.Abs(det) < 1e-12)
        {
            return false;
        }

        var invDet = 1.0 / det;
        o[0] = ((m[4] * m[8]) - (m[5] * m[7])) * invDet;
        o[1] = ((m[2] * m[7]) - (m[1] * m[8])) * invDet;
        o[2] = ((m[1] * m[5]) - (m[2] * m[4])) * invDet;
        o[3] = ((m[5] * m[6]) - (m[3] * m[8])) * invDet;
        o[4] = ((m[0] * m[8]) - (m[2] * m[6])) * invDet;
        o[5] = ((m[2] * m[3]) - (m[0] * m[5])) * invDet;
        o[6] = ((m[3] * m[7]) - (m[4] * m[6])) * invDet;
        o[7] = ((m[1] * m[6]) - (m[0] * m[7])) * invDet;
        o[8] = ((m[0] * m[4]) - (m[1] * m[3])) * invDet;
        return true;
    }
}
