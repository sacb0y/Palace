namespace Palace.Helpers;

/// <summary>
/// V1 tile pixel contract (Geo / Palace.Imaging Working).
/// Both HDR and SDR tiles are stored as <b>RGBA16F Working</b> so the GPU
/// path stays one format: BT.2020 linear, 1.0 = 203 nits, premultiplied alpha.
/// SDR sources convert to Working at upload; never store RGBA8_SRGB in the
/// V1 tile array (<c>SMOOTH_SRGB8=0</c>).
/// </summary>
public static class SmoothTileWorkingSpace
{
    public const float RefWhiteNits = 203f;

    /// <summary>Documented storage format for every V1 tile slot.</summary>
    public const string GpuFormat = "RGBA16F";

    /// <summary>Diligent / HLSL compile define for the mip CS.</summary>
    public const int SmoothSrgb8 = 0;

    public const int StraightAlpha = 0; // premultiplied in tiles

    public const int Border = SmoothTileMath.Border;
    public const int SlotSize = SmoothTileMath.SlotSize;
    public const int ContentSize = SmoothTileMath.ContentSize;

    /// <summary>Default mip filter: box (Geo Params.z = 0).</summary>
    public const int FilterBox = 0;

    public const int AntiRing = 1;
}
