namespace Palace.Imaging;

/// <summary>
/// P3 / P4 tonemap path flag. Mosaic thumbs bake a PQ-spaced LUT; full SDR
/// (P4) may use direct <see cref="ToneMapper.MapFrame"/>. HDR present (P5)
/// uses <see cref="DisplayTarget.IsHdrOutput"/> + BT.2390 (not this enum).
/// </summary>
public enum ImagingPathKind
{
    /// <summary>Library mosaic / preview JPEG — LUT path (default size 256).</summary>
    Thumb = 0,

    /// <summary>Full-resolution SDR (P4) — direct map; shares Analyze stats with thumbs when size matches.</summary>
    Full = 1,
}
