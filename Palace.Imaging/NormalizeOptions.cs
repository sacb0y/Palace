namespace Palace.Imaging;

/// <summary>Per-frame options for <see cref="Normalize"/>.</summary>
public readonly record struct NormalizeOptions(
    TransferKind Transfer,
    ColorPrimaries Primaries = ColorPrimaries.Bt709,
    float? WhiteLuminance = null,
    float? MetadataPeakNits = null,
    float ExrRefWhite = ImagingConstants.ExrRefWhite)
{
    /// <summary>
    /// Effective white luminance in nits for LinearScene.
    /// Normalize uses <c>x * EffectiveWhiteNits / 203</c>.
    /// </summary>
    public float EffectiveWhiteNits =>
        WhiteLuminance is > 0f ? WhiteLuminance.Value : ExrRefWhite;
}
