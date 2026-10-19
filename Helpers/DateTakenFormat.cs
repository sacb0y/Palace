using System.Globalization;

namespace Palace.Helpers;

/// <summary>
/// Catalog form of a photo's EXIF capture time. EXIF has no zone, so it is stored as local
/// wall time "yyyy-MM-ddTHH:mm:ss" (sorts as text next to ISO <c>DateModified</c>).
/// </summary>
public static class DateTakenFormat
{
    private static readonly HashSet<string> ExifExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".tif", ".tiff", ".heic", ".heif", ".avif", ".webp", ".png",
        ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf", ".jxl"
    };

    public static string? Format(DateTime value) =>
        value.Year is < 1800 or > 2200 ? null : value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Formats that can carry EXIF; others are not opened for the backfill.</summary>
    public static bool MayHaveExif(string path) => ExifExtensions.Contains(Path.GetExtension(path));
}
