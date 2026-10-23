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
    /// Default white-luminance scale (nits) for OpenEXR / Radiance when
    /// <c>whiteLuminance</c> / EXPOSURE metadata is absent. Normalize does
    /// <c>x * ExrRefWhite / 203</c>, so metadata-less 1.0 → working
    /// <c>80/203</c>.
    /// <para>
    /// Measured on Pregmata_SteamAVIF (3840×2160): EXR pixel-identical to JXR
    /// (per-pixel luminance ratio median 1.0000, IQR 1.0000–1.0000); .hdr/JXR
    /// ratio 0.9968 (IQR 0.9954–0.9981, RGBE rounding); EXR had no
    /// whiteLuminance/chromaticities; .hdr had no EXPOSURE/PRIMARIES; peak
    /// maxRGB ≈ 10.05 scRGB (~804 nits). Conclusion: EXR and .hdr
    /// <b>1.0 = 80 nits</b> (= <see cref="ScrgbNits"/>) for metadata-less files.
    /// </para>
    /// </summary>
    public const float ExrRefWhite = ScrgbNits; // 80

    /// <summary>
    /// Fraction of pixels at encoded/mastering max that sets
    /// <see cref="NormalizedFrame.SourceClipped"/>. Stored on
    /// <see cref="ImageStats"/> for later UI/pipeline use — nothing consumes
    /// it in P1 yet (S1).
    /// </summary>
    public const float SourceClipFraction = 0.005f;

    /// <summary>Encoded-max epsilon for 0..1 transfers.</summary>
    public const float EncodedMaxEpsilon = 1e-3f;

    /// <summary>
    /// Pipeline version baked into LUT cache keys. Bump when Normalize /
    /// Analyze / ToneMap math changes incompatibly.
    /// </summary>
    public const int PipelineVersion = 6;

    // BT.2020 non-constant luminance Y (Rec. ITU-R BT.2020-2 Table 4).
    public const float Bt2020Yr = 0.2627f;
    public const float Bt2020Yg = 0.6780f;
    public const float Bt2020Yb = 0.0593f;

    public static float Bt2020Y(float r, float g, float b) =>
        (Bt2020Yr * r) + (Bt2020Yg * g) + (Bt2020Yb * b);
}
