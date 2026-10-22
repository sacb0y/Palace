namespace Palace.Imaging;

/// <summary>
/// Synthetic master → encoded transfer buffers for cross-format normalize tests.
/// Real JXL/AVIF/PNG/JXR/EXR file bytes arrive with P2 decoders; P1 checks the
/// linear working-space contract from encoded float stand-ins.
/// </summary>
public static class FixtureMaster
{
    /// <summary>Mid-grey ≈ 0.18, ref white 1.0, and a 4× highlight (working space).</summary>
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
            {
                // Approximate inverse of OOTF+EOTF for greys; chromatic samples are
                // best-effort for round-trip tolerance tests.
                InverseHlgApprox(wr, wg, wb, out er, out eg, out eb);
                return;
            }

            case TransferKind.Scrgb:
                er = wr * ImagingConstants.RefWhiteNits / ImagingConstants.ScrgbNits;
                eg = wg * ImagingConstants.RefWhiteNits / ImagingConstants.ScrgbNits;
                eb = wb * ImagingConstants.RefWhiteNits / ImagingConstants.ScrgbNits;
                return;

            case TransferKind.LinearScene:
                er = wr * exrRefWhite;
                eg = wg * exrRefWhite;
                eb = wb * exrRefWhite;
                return;

            default:
                er = ColorMath.SrgbOetf(Math.Clamp(wr, 0f, 1f));
                eg = ColorMath.SrgbOetf(Math.Clamp(wg, 0f, 1f));
                eb = ColorMath.SrgbOetf(Math.Clamp(wb, 0f, 1f));
                return;
        }
    }

    /// <summary>
    /// Grey-oriented HLG inverse: undo OOTF (Y^γ * 1000) then OETF.
    /// </summary>
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

        // Y_disp = Y_scene^γ * 1000 → Y_scene = (Y_disp/1000)^(1/γ)
        var yScene = MathF.Pow(yDisp / ColorMath.HlgPeakNits, 1f / ColorMath.HlgOotfGamma);
        var k = yScene / (yDisp / ColorMath.HlgPeakNits); // scene / (nits/1000)
        // nits = scene * Y_scene^(γ-1) * 1000 → scene = nits / (Y_scene^(γ-1)*1000)
        var denom = MathF.Pow(yScene, ColorMath.HlgOotfGamma - 1f) * ColorMath.HlgPeakNits;
        var sr = nitsR / denom;
        var sg = nitsG / denom;
        var sb = nitsB / denom;
        _ = k;
        er = ColorMath.HlgOetf(sr);
        eg = ColorMath.HlgOetf(sg);
        eb = ColorMath.HlgOetf(sb);
    }
}
