namespace Palace.Imaging;

/// <summary>Source RGB primaries before Normalize converts to BT.2020.</summary>
public enum ColorPrimaries
{
    /// <summary>Already BT.2020 (PQ/HLG HDR masters).</summary>
    Bt2020 = 0,

    /// <summary>BT.709 / sRGB / typical JXR scRGB / many EXRs.</summary>
    Bt709 = 1,

    /// <summary>Display P3 (D65).</summary>
    DisplayP3 = 2,
}
