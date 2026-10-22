namespace Palace.Imaging;

/// <summary>Encoded transfer of a decoder buffer before <see cref="Normalize"/>.</summary>
public enum TransferKind
{
    /// <summary>sRGB / BT.709 OETF; linearize so 1.0 = reference white.</summary>
    Sdr,

    /// <summary>SMPTE ST 2084 PQ (0..1 → 0..10 000 nits).</summary>
    Pq,

    /// <summary>BT.2100 HLG scene-referred; OOTF at 1000-nit peak then /203.</summary>
    Hlg,

    /// <summary>Windows scRGB linear (1.0 = 80 nits).</summary>
    Scrgb,

    /// <summary>
    /// Scene-linear float (EXR / Radiance). Scale by white luminance or
    /// <see cref="ImagingConstants.ExrRefWhite"/>.
    /// </summary>
    LinearScene,
}
