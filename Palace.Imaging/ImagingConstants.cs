namespace Palace.Imaging;

/// <summary>
/// Pipeline constants for the HDR imaging rewrite (P1). Working space is
/// BT.2020 linear with <see cref="RefWhiteNits"/> = 1.0.
/// </summary>
public static class ImagingConstants
{
    /// <summary>
    /// Report ITU-R BT.2408 reference / paper white (cd/m²). Working-space 1.0.
    /// (Confirm § against the current BT.2408 PDF before tightening citations.)
    /// </summary>
    public const float RefWhiteNits = 203f;

    /// <summary>scRGB / DXGI composition white (nits). Used for JXR scRGB scale.</summary>
    public const float ScrgbNits = 80f;

    /// <summary>
    /// Default <c>whiteLuminance</c>-like scale (nits) for OpenEXR / Radiance when
    /// metadata is absent. Normalize does <c>x * ExrRefWhite / 203</c>, so
    /// <b>203</b> keeps relative 1.0 → working 1.0.
    /// <para>
    /// Pragmata EXR / <c>.hdr</c> / JXR pixel-ratio measurement is pending
    /// (files not in-repo yet). When measured, replace this constant and record
    /// the ratios in <c>docs/hdr-sdr-tuning.md</c> / store plan.
    /// Provisional: relative EXR 1.0 ≡ BT.2408 paper white → 203.
    /// </para>
    /// </summary>
    public const float ExrRefWhite = 203f;

    /// <summary>Fraction of pixels at encoded/mastering max that flags source-clipped.</summary>
    public const float SourceClipFraction = 0.005f;

    /// <summary>Encoded-max epsilon for 0..1 transfers.</summary>
    public const float EncodedMaxEpsilon = 1e-3f;

    /// <summary>
    /// Pipeline version baked into LUT cache keys. Bump when Normalize /
    /// Analyze / ToneMap math changes incompatibly.
    /// </summary>
    public const int PipelineVersion = 3;

    // BT.2020 non-constant luminance Y (Rec. ITU-R BT.2020-2 Table 4).
    public const float Bt2020Yr = 0.2627f;
    public const float Bt2020Yg = 0.6780f;
    public const float Bt2020Yb = 0.0593f;

    public static float Bt2020Y(float r, float g, float b) =>
        (Bt2020Yr * r) + (Bt2020Yg * g) + (Bt2020Yb * b);
}
