namespace Palace.Imaging;

/// <summary>
/// Synthetic master → encoded transfer buffers for normalize tests.
/// Checked-in multi-format binaries live under
/// <c>Palace.Tests/Fixtures/imaging/</c> (Radiance + float dumps of one master).
/// </summary>
public static class FixtureMaster
{
    /// <summary>Mid-grey ≈ 0.18, ref white 1.0, 4× highlight, warm patch (BT.2020 working).</summary>
    public static float[] WorkingMasterRgba { get; } =
    [
        0.18f, 0.18f, 0.18f, 1f,
        1.00f, 1.00f, 1.00f, 1f,
        4.00f, 4.00f, 4.00f, 1f,
        0.50f, 0.10f, 0.05f, 1f,
    ];

    public const int MasterWidth = 2;
    public const int MasterHeight = 2;

    public static float[] Encode(TransferKind transfer, float exrRefWhite = ImagingConstants.ExrRefWhite)
    {
        var src = WorkingMasterRgba;
        var dst = new float[src.Length];
        for (var i = 0; i < src.Length; i += 4)
        {
            EncodePixel(src[i], src[i + 1], src[i + 2], transfer, exrRefWhite, out var r, out var g, out var b);
            dst[i] = r;
            dst[i + 1] = g;
            dst[i + 2] = b;
            dst[i + 3] = src[i + 3];
        }

        return dst;
    }

    public static void EncodePixel(
        float wr,
        float wg,
        float wb,
        TransferKind transfer,
        float exrRefWhite,
        out float er,
        out float eg,
        out float eb)
    {
        switch (transfer)
        {
            case TransferKind.Pq:
                er = ColorMath.NitsToPq(wr * ImagingConstants.RefWhiteNits);
                eg = ColorMath.NitsToPq(wg * ImagingConstants.RefWhiteNits);
                eb = ColorMath.NitsToPq(wb * ImagingConstants.RefWhiteNits);
                return;

            case TransferKind.Hlg:
                InverseHlgApprox(wr, wg, wb, out er, out eg, out eb);
                return;

            case TransferKind.Scrgb:
                // Working BT.2020 → BT.709 scRGB for encode stand-in.
                Bt2020ToBt709(wr, wg, wb, out var r709, out var g709, out var b709);
                er = r709 * ImagingConstants.RefWhiteNits / ImagingConstants.ScrgbNits;
                eg = g709 * ImagingConstants.RefWhiteNits / ImagingConstants.ScrgbNits;
                eb = b709 * ImagingConstants.RefWhiteNits / ImagingConstants.ScrgbNits;
                return;

            case TransferKind.LinearScene:
                // Inverse of x * ExrRefWhite / 203 with Bt709→2020 on normalize:
                // store BT.709 scene-linear so round-trip lands on working.
                Bt2020ToBt709(wr, wg, wb, out var sr, out var sg, out var sb);
                var inv = ImagingConstants.RefWhiteNits / Math.Max(exrRefWhite, 1e-6f);
                er = sr * inv;
                eg = sg * inv;
                eb = sb * inv;
                return;

            default:
                Bt2020ToBt709(wr, wg, wb, out var dr, out var dg, out var db);
                er = ColorMath.SrgbOetf(Math.Clamp(dr, 0f, 1f));
                eg = ColorMath.SrgbOetf(Math.Clamp(dg, 0f, 1f));
                eb = ColorMath.SrgbOetf(Math.Clamp(db, 0f, 1f));
                return;
        }
    }

    public static NormalizeOptions OptionsFor(TransferKind transfer) =>
        transfer switch
        {
            TransferKind.Pq or TransferKind.Hlg => new NormalizeOptions(transfer, ColorPrimaries.Bt2020),
            TransferKind.LinearScene => new NormalizeOptions(transfer, ColorPrimaries.Bt709),
            _ => new NormalizeOptions(transfer, ColorPrimaries.Bt709),
        };

    private static void Bt2020ToBt709(float r, float g, float b, out float or, out float og, out float ob)
    {
        // Inverse of Primaries.ToBt2020(Bt709).
        or = (1.6604910f * r) + (-0.5876411f * g) + (-0.0728499f * b);
        og = (-0.1245505f * r) + (1.1328999f * g) + (-0.0083494f * b);
        ob = (-0.0181508f * r) + (-0.1005789f * g) + (1.1187297f * b);
    }

    private static void InverseHlgApprox(
        float wr, float wg, float wb,
        out float er, out float eg, out float eb)
    {
        var nitsR = wr * ImagingConstants.RefWhiteNits;
        var nitsG = wg * ImagingConstants.RefWhiteNits;
        var nitsB = wb * ImagingConstants.RefWhiteNits;
        var yDisp = ImagingConstants.Bt2020Y(nitsR, nitsG, nitsB);
        if (yDisp <= 1e-12f)
        {
            er = eg = eb = 0f;
            return;
        }

        var yScene = MathF.Pow(yDisp / ColorMath.HlgPeakNits, 1f / ColorMath.HlgOotfGamma);
        var denom = MathF.Pow(yScene, ColorMath.HlgOotfGamma - 1f) * ColorMath.HlgPeakNits;
        er = ColorMath.HlgOetf(nitsR / denom);
        eg = ColorMath.HlgOetf(nitsG / denom);
        eb = ColorMath.HlgOetf(nitsB / denom);
    }
}
