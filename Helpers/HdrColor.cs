namespace Palace.Helpers;

/// <summary>
/// SKIV <c>viewer.cpp</c> AVIF present: libavif YUV→RGB (CICP matrix), then
/// PQ/HLG EOTF, then primaries → scRGB. Coefficients match
/// <c>c_Bt2100toscRGB</c> / <c>c_fromDCIP3to709</c> / <c>c_from601to709</c>
/// as applied by <c>XMVector3Transform</c>.
/// </summary>
public static class HdrColor
{
    /// <summary>10000 nits / 80 nits — SKIV’s PQ-linear → scRGB scale.</summary>
    public const float PqToScrgb = 125f;

    /// <summary>HLG nominal peak 1000 nits / 80.</summary>
    public const float HlgToScrgb = 12.5f;

    /// <summary>
    /// Palace MaxCLL 207.561 on Sacb0y’s AVIF: PQ peak in R only, then
    /// BT.2020→scRGB. That is Y-as-R (WIC left YUV), not SKIV RGB.
    /// </summary>
    public const float YuvAsRedScrgbPeak = 1.660491f * PqToScrgb;

    /// <summary>
    /// CICP <c>matrix_coefficients</c> 0 is Identity: planes are GBR
    /// (Y=G, Cb=B, Cr=R). No chroma offset.
    /// </summary>
    public static bool IsIdentityMatrix(int? matrix) => matrix == 0;

    public static float LumaRange(float sample, bool fullRange) =>
        fullRange ? sample : Math.Clamp((sample - (16f / 255f)) / (219f / 255f), 0, 1);

    public static void YuvToRgb(
        float y,
        float u,
        float v,
        int? matrix,
        bool fullRange,
        out float r,
        out float g,
        out float b)
    {
        if (IsIdentityMatrix(matrix))
        {
            // GBR: scale like luma on every plane; never subtract 0.5.
            r = LumaRange(v, fullRange);
            g = LumaRange(y, fullRange);
            b = LumaRange(u, fullRange);
            return;
        }

        var yf = LumaRange(y, fullRange);
        var uf = fullRange ? u - 0.5f : (u - (128f / 255f)) / (224f / 255f);
        var vf = fullRange ? v - 0.5f : (v - (128f / 255f)) / (224f / 255f);

        switch (matrix)
        {
            case 5:
            case 6:
                r = yf + (1.402f * vf);
                g = yf - (0.344136f * uf) - (0.714136f * vf);
                b = yf + (1.772f * uf);
                return;
            case 1:
                r = yf + (1.5748f * vf);
                g = yf - (0.187324f * uf) - (0.468124f * vf);
                b = yf + (1.8556f * uf);
                return;
            default:
                // BT.2020 NCL (9) — libavif / SKIV HDR AVIF default.
                r = yf + (1.4746f * vf);
                g = yf - (0.164553f * uf) - (0.571353f * vf);
                b = yf + (1.8814f * uf);
                return;
        }
    }

    public static void EncodedRgbToScrgb(
        float r,
        float g,
        float b,
        HdrTransfer transfer,
        int? primaries,
        out float sr,
        out float sg,
        out float sb)
    {
        var lr = GalleryPresent.TransferToLinear01(r, transfer);
        var lg = GalleryPresent.TransferToLinear01(g, transfer);
        var lb = GalleryPresent.TransferToLinear01(b, transfer);
        Linear01ToScrgb(lr, lg, lb, transfer, primaries, out sr, out sg, out sb);
    }

    /// <summary>
    /// SKIV else after 709 / unspecified-709 (2) / XYZ / 601 / DCI-P3:
    /// BT.2020 (9), Display P3 (12), and missing/unspecified <c>colr</c>
    /// already went through <c>c_Bt2100toscRGB</c> /
    /// <see cref="GalleryPresent.Bt2020ToBt709"/>. Histogram luma must invert
    /// that same set — not only primaries 9.
    /// </summary>
    public static bool UsesBt2100ToScrgbMatrix(int? primaries) =>
        primaries is not (1 or 2 or 5 or 6 or 10 or 11);

    /// <summary>
    /// SKIV AVIF primaries switch after PQ-linear (0–1, 1 = 10 000 nits).
    /// BT.709 + non-PQ stays encoded-as-linear * 203/80 (SDR).
    /// Unspecified / BT.2020 / Display P3 (12) use <c>c_Bt2100toscRGB</c>.
    /// </summary>
    public static void Linear01ToScrgb(
        float r,
        float g,
        float b,
        HdrTransfer transfer,
        int? primaries,
        out float sr,
        out float sg,
        out float sb)
    {
        if (transfer == HdrTransfer.Scrgb)
        {
            sr = r;
            sg = g;
            sb = b;
            return;
        }

        var scale = transfer switch
        {
            HdrTransfer.Pq => PqToScrgb,
            HdrTransfer.Hlg => HlgToScrgb,
            HdrTransfer.Linear => GalleryPresent.SdrReferenceNits / GalleryPresent.ScrgbNits,
            _ => GalleryPresent.SdrReferenceNits / GalleryPresent.ScrgbNits
        };

        if (primaries is 1 or 2)
        {
            sr = r * scale;
            sg = g * scale;
            sb = b * scale;
            return;
        }

        if (primaries == 10)
        {
            XyzToBt709(r, g, b, out r, out g, out b);
            sr = r * scale;
            sg = g * scale;
            sb = b * scale;
            return;
        }

        if (primaries is 5 or 6)
        {
            Bt601ToBt709(r, g, b, out r, out g, out b);
            sr = r * scale;
            sg = g * scale;
            sb = b * scale;
            return;
        }

        if (primaries == 11)
        {
            DciP3ToBt709(r, g, b, out r, out g, out b);
            sr = r * scale;
            sg = g * scale;
            sb = b * scale;
            return;
        }

        // SKIV else: BT.2020 / BT.2100 / unspecified / Display P3 (12).
        // Keep in lockstep with UsesBt2100ToScrgbMatrix.
        if (transfer == HdrTransfer.Pq)
        {
            Bt2100ToScrgb(r, g, b, out sr, out sg, out sb);
            return;
        }

        GalleryPresent.Bt2020ToBt709(r, g, b, out r, out g, out b);
        sr = r * scale;
        sg = g * scale;
        sb = b * scale;
    }

    /// <summary>SKIV <c>c_Bt2100toscRGB</c> × PQ-linear (includes ×125).</summary>
    public static void Bt2100ToScrgb(float r, float g, float b, out float sr, out float sg, out float sb)
    {
        sr = (1.660491f * r) + (-0.587641f * g) + (-0.072850f * b);
        sg = (-0.124550f * r) + (1.132900f * g) + (-0.008349f * b);
        sb = (-0.018151f * r) + (-0.100579f * g) + (1.118730f * b);
        sr *= PqToScrgb;
        sg *= PqToScrgb;
        sb *= PqToScrgb;
    }

    public static void DciP3ToBt709(float r, float g, float b, out float r709, out float g709, out float b709)
    {
        r709 = (1.215661f * r) + (-0.223146f * g);
        g709 = (-0.041757f * r) + (1.038042f * g);
        b709 = (-0.022841f * r) + (-0.080108f * g) + (1.098369f * b);
    }

    public static void Bt601ToBt709(float r, float g, float b, out float r709, out float g709, out float b709)
    {
        r709 = r;
        g709 = g;
        b709 = 0.9184f * b;
    }

    public static void XyzToBt709(float x, float y, float z, out float r, out float g, out float b)
    {
        r = (3.2409699f * x) + (-1.5373832f * y) + (-0.4986108f * z);
        g = (-0.9692436f * x) + (1.8759675f * y) + (0.0415551f * z);
        b = (0.0556301f * x) + (-0.2039770f * y) + (1.0569715f * z);
    }

    public static bool NeedsYuvConvert(HdrKind kind) =>
        kind is HdrKind.HdrAvif or HdrKind.HdrHeif;

    public static void YuvEncodedToScrgb(
        float y,
        float u,
        float v,
        HdrProbe probe,
        out float sr,
        out float sg,
        out float sb)
    {
        YuvToRgb(y, u, v, probe.CicpMatrix ?? 9, probe.FullRange ?? true, out var r, out var g, out var b);
        EncodedRgbToScrgb(r, g, b, probe.Transfer, probe.CicpPrimaries, out sr, out sg, out sb);
    }

    /// <summary>
    /// Info CIE Y in the source primaries (PQ/HLG nits before 2020→709).
    /// Do not measure scRGB after the primaries matrix — that is display
    /// 709 and reads high versus SKIV when <c>LuminanceY</c> still uses
    /// BT.2020 weights.
    /// </summary>
    public static float SourcePrimaryNitsY(
        float encodedR,
        float encodedG,
        float encodedB,
        HdrTransfer transfer,
        int? primaries) =>
        GalleryPresent.LuminanceY(
            GalleryPresent.EncodedToNits(encodedR, transfer),
            GalleryPresent.EncodedToNits(encodedG, transfer),
            GalleryPresent.EncodedToNits(encodedB, transfer),
            primaries == 9);

    public static float YuvSourcePrimaryNitsY(float y, float u, float v, HdrProbe probe)
    {
        YuvToRgb(y, u, v, probe.CicpMatrix ?? 9, probe.FullRange ?? true, out var r, out var g, out var b);
        return SourcePrimaryNitsY(r, g, b, probe.Transfer, probe.CicpPrimaries);
    }

    /// <summary>
    /// SKIV map-CLL-to-display in scRGB. SDR present uses this; HDR keeps
    /// identity + clip in <see cref="GalleryPresent.PresentMap"/>.
    /// </summary>
    public static float MapCllToDisplayScrgb(float scrgb, float contentMaxNits, float displayNits)
    {
        var peak = displayNits > 0 ? displayNits : GalleryPresent.ScrgbNits;
        var nits = GalleryPresent.MapCllAndClip(
            scrgb * GalleryPresent.ScrgbNits,
            contentMaxNits,
            peak);
        return nits / GalleryPresent.ScrgbNits;
    }

    /// <summary>
    /// WIC <c>Rgba16</c> on HDR AVIF often writes luma in R and zeros G/B
    /// (DoNotColorManage skips libavif’s YUV→RGB). SKIV never presents that.
    /// </summary>
    public static bool IsLumaInRedOnly(ReadOnlySpan<float> red, ReadOnlySpan<float> green, ReadOnlySpan<float> blue)
    {
        if (red.Length == 0 || red.Length != green.Length || red.Length != blue.Length)
        {
            return false;
        }

        var maxR = 0f;
        var maxG = 0f;
        var maxB = 0f;
        for (var i = 0; i < red.Length; i++)
        {
            maxR = Math.Max(maxR, red[i]);
            maxG = Math.Max(maxG, green[i]);
            maxB = Math.Max(maxB, blue[i]);
        }

        return maxR > 0.05f && maxG < 0.02f && maxB < 0.02f;
    }
}
