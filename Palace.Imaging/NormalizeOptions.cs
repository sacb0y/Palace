namespace Palace.Imaging;

/// <summary>Per-frame options for <see cref="Normalize"/>.</summary>
public readonly record struct NormalizeOptions(
    TransferKind Transfer,
    float? WhiteLuminance = null,
    float? MetadataPeakNits = null,
    float ExrRefWhite = ImagingConstants.ExrRefWhite)
{
    /// <summary>Effective white scale for LinearScene (EXR / Radiance).</summary>
    public float EffectiveExrWhite =>
        WhiteLuminance is > 0f ? WhiteLuminance.Value : ExrRefWhite;
}
