namespace Palace.Imaging;

/// <summary>
/// BT.2020 linear RGBA (1.0 = 203 nits) plus clip metadata from Normalize.
/// </summary>
public sealed class NormalizedFrame
{
    public required float[] Rgba { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required bool SourceClipped { get; init; }
    public required float SourceClipRatio { get; init; }
    public float? MetadataPeakNits { get; init; }

    public int PixelCount => Width * Height;
}
