namespace Palace.Helpers;

/// <summary>
/// Thumbnail cache generations. A marker file in the thumbs folder records
/// when the current generation started; cached JPEGs for formats whose
/// rendering changed (HDR stills were clipped red, PSD/JXL/EXR/… had no
/// built-in decoder) that predate the marker are regenerated on next use.
/// </summary>
public static class ThumbCacheVersion
{
    /// <summary>Bump when thumbnail rendering changes for existing files.</summary>
    public const int Current = 2;

    public static string MarkerName => $".thumbs-v{Current}";

    /// <summary>Returns the UTC time the current generation started, creating the marker if needed.</summary>
    public static DateTime EnsureMarker(string thumbsRoot, Func<DateTime>? nowUtc = null)
    {
        var path = Path.Combine(thumbsRoot, MarkerName);
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

    public static bool IsStale(DateTime cachedThumbUtc, DateTime generationStartUtc, bool renderingChanged) =>
        renderingChanged && generationStartUtc > DateTime.MinValue && cachedThumbUtc < generationStartUtc;
}
