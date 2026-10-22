namespace Palace.Helpers;

/// <summary>
/// Thumbnail cache generations. A marker file in the thumbs folder records
/// when the current generation started; cached JPEGs for formats whose
/// rendering changed (HDR stills were clipped red, PSD/JXL/EXR/… had no
/// built-in decoder) that predate the marker are regenerated on next use.
/// Each generation keeps its own marker, so a later bump only regenerates
/// the formats that generation changed.
/// </summary>
public static class ThumbCacheVersion
{
    /// <summary>Bump when thumbnail rendering changes for existing files.</summary>
    public const int Current = 7;

    /// <summary>Generation 2: HDR stills, PSD, JXL, EXR, Radiance, TGA, Clip, ICO.</summary>
    public const int DecodersGeneration = 2;

    /// <summary>Generation 3: BMP / TIFF names that hold Substance Painter shelf metadata with a preview.</summary>
    public const int ShelfPreviewGeneration = 3;

    /// <summary>
    /// Generation 4: HDR JPEG XL intensity → scRGB uses 80-nit white (was ÷203),
    /// so prior JXL mosaic thumbs look ~2.5× too hot / wrong exposure.
    /// </summary>
    public const int JxlIntensityGeneration = 4;

    /// <summary>
    /// Generation 5: P3 / BT.2020 JXL thumbs keep OOG via BT.2100 + matrix
    /// (Relative on plain LinearSRGB had collapsed them to Rec.709).
    /// </summary>
    public const int JxlWideGamutGeneration = 5;

    /// <summary>
    /// Generation 6: HDR JXL still-PQ buffers use EOTF→scRGB (no intensity_target
    /// scale on PQ codes) after verifying TARGET_DATA transfer.
    /// </summary>
    public const int JxlActualTransferGeneration = 6;

    /// <summary>
    /// Generation 7: JXL never falls through to Store WIC / shell thumbs when
    /// libjxl fails (those washed PQ became the SDR full view via ThumbPath).
    /// </summary>
    public const int JxlNoShellWicGeneration = 7;

    public static string MarkerName => MarkerNameFor(Current);

    public static string MarkerNameFor(int generation) => $".thumbs-v{generation}";

    /// <summary>Returns the UTC time the current generation started, creating the marker if needed.</summary>
    public static DateTime EnsureMarker(string thumbsRoot, Func<DateTime>? nowUtc = null) =>
        EnsureMarker(thumbsRoot, Current, nowUtc);

    /// <summary>Returns the UTC time <paramref name="generation"/> started, creating its marker if needed.</summary>
    public static DateTime EnsureMarker(string thumbsRoot, int generation, Func<DateTime>? nowUtc = null)
    {
        var path = Path.Combine(thumbsRoot, MarkerNameFor(generation));
        try
        {
            if (File.Exists(path) && DateTime.TryParse(File.ReadAllText(path).Trim(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var stamp))
            {
                return stamp.ToUniversalTime();
            }

            var now = (nowUtc ?? (() => DateTime.UtcNow))();
            Directory.CreateDirectory(thumbsRoot);
            File.WriteAllText(path, now.ToString("O"));
            return now;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>Formats whose thumbnails changed in generation 2.</summary>
    public static bool RenderingChanged(string path, bool isHdr) =>
        isHdr || StillDecode.HandlesPath(path) || PathSafe.IsJxl(path) || PathSafe.IsPsd(path)
        || PathSafe.IsIco(path);

    /// <summary>
    /// Formats whose thumbnails changed in generation 3: a BMP / TIFF that is really
    /// Substance Painter shelf metadata now shows its embedded preview instead of the placeholder.
    /// </summary>
    public static bool ShelfPreviewChanged(string path) =>
        PathSafe.Extension(path).ToLowerInvariant() is ".bmp" or ".tif" or ".tiff";

    /// <summary>Formats whose thumbnails changed in generation 4 (HDR JXL 80-nit scale).</summary>
    public static bool JxlIntensityChanged(string path) => PathSafe.IsJxl(path);

    /// <summary>Formats whose thumbnails changed in generation 5 (wide-gamut JXL OOG).</summary>
    public static bool JxlWideGamutChanged(string path) => PathSafe.IsJxl(path);

    /// <summary>Formats whose thumbnails changed in generation 6 (JXL actual-TF normalize).</summary>
    public static bool JxlActualTransferChanged(string path) => PathSafe.IsJxl(path);

    /// <summary>Formats whose thumbnails changed in generation 7 (no JXL shell/WIC thumb).</summary>
    public static bool JxlNoShellWicChanged(string path) => PathSafe.IsJxl(path);

    public static bool IsStale(DateTime cachedThumbUtc, DateTime generationStartUtc, bool renderingChanged) =>
        renderingChanged && generationStartUtc > DateTime.MinValue && cachedThumbUtc < generationStartUtc;
}
