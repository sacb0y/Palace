namespace Palace.Imaging;

/// <summary>Per-frame options for <see cref="Normalize"/>.</summary>
/// <remarks>
/// <para>
/// <b>M3 / EXR chromaticities:</b> pass <see cref="Chromaticities"/> from the
/// OpenEXR attribute when the decoder has it. If null, LinearScene uses
/// <see cref="Primaries"/> (default BT.709). Known xy sets
/// (BT.709 / BT.2020 / Display P3 via <see cref="Chromaticities.MatchKnown"/>)
/// select the enum matrix; <b>P2</b> unknown xy use
/// <see cref="Primaries.ToBt2020(float,float,float,Chromaticities,out float,out float,out float)"/>
/// (RGB→XYZ→BT.2020, Bradford white adapt). Degenerate chromaticities fall
/// back to <see cref="Primaries"/>.
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
    /// <summary>
    /// Prefer the positional primary ctor (or named args). A zeroed /
    /// object-initializer struct leaves <see cref="ExrRefWhite"/> at 0 —
    /// treat that as <see cref="ImagingConstants.ExrRefWhite"/> (N3).
    /// </summary>
    public float EffectiveWhiteNits =>
        WhiteLuminance is > 0f
            ? WhiteLuminance.Value
            : ExrRefWhite > 0f ? ExrRefWhite : ImagingConstants.ExrRefWhite;

    /// <summary>Resolved primaries: chromaticities match wins when present.</summary>
    public ColorPrimaries EffectivePrimaries =>
        Chromaticities?.MatchKnown() ?? Primaries;

    /// <summary>
    /// True when <see cref="Chromaticities"/> is set but not a known enum set —
    /// Normalize builds a custom RGB→BT.2020 matrix (P2).
    /// </summary>
    public bool UsesCustomChromaticities =>
        Chromaticities is { } c && c.MatchKnown() is null;
}
