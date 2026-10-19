namespace Palace.Helpers;

/// <summary>
/// Sidecar beside a cached thumbnail JPEG recording whether that JPEG is the placeholder tile
/// (the source could not be decoded). Cache reads need this so a rescan that reuses a current
/// cached thumbnail keeps <see cref="Models.Asset.Undecodable"/> instead of resetting it.
/// Thumbnails cached before the sidecar existed read as unknown (<c>null</c>).
/// </summary>
public static class ThumbStatus
{
    public const string Suffix = ".status";

    public static string PathFor(string thumbPath) => thumbPath + Suffix;

    /// <summary><c>true</c> placeholder, <c>false</c> real thumbnail, <c>null</c> unknown (legacy or unreadable).</summary>
    public static bool? Read(string thumbPath)
    {
        try
        {
            var path = PathFor(thumbPath);
            if (!File.Exists(path))
            {
                return null;
            }

            return File.ReadAllText(path).Trim() switch
            {
                "1" => true,
                "0" => false,
                _ => null
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(string thumbPath, bool isPlaceholder)
    {
        try
        {
            File.WriteAllText(PathFor(thumbPath), isPlaceholder ? "1" : "0");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: an unknown status only costs one regeneration on the next scan.
        }
    }

    public static void Delete(string thumbPath)
    {
        try
        {
            File.Delete(PathFor(thumbPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
