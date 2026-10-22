namespace Palace.Imaging;

/// <summary>Per-frame options for <see cref="Normalize"/>.</summary>
/// <remarks>
/// <para>
/// <b>M3 / EXR chromaticities:</b> pass <see cref="Chromaticities"/> from the
/// OpenEXR attribute when the decoder has it. If null, LinearScene uses
/// <see cref="Primaries"/> (default BT.709). Custom xy sets that do not match
/// BT.709/2020/P3 fall back to <see cref="Primaries"/> — full matrix from
/// arbitrary chromaticities is deferred to P2 with the EXR decoder.
/// </para>
/// </remarks>
public readonly record struct NormalizeOptions(
    TransferKind Transfer,
    ColorPrimaries Primaries = ColorPrimaries.Bt709,
    float? WhiteLuminance = null,
    float? MetadataPeakNits = null,
    float ExrRefWhite = ImagingConstants.ExrRefWhite,
    Chromaticities? Chromaticities = null)
{
    public float EffectiveWhiteNits =>
        WhiteLuminance is > 0f ? WhiteLuminance.Value : ExrRefWhite;

    /// <summary>Resolved primaries: chromaticities match wins when present.</summary>
    public ColorPrimaries EffectivePrimaries =>
        Chromaticities?.MatchKnown() ?? Primaries;
}
