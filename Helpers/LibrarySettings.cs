using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Library &amp; Folders prefs (LocalSettings). Off WinUI for Palace.Tests.
/// Scan / watcher honor these via <see cref="Apply"/>; Settings wires the UI.
/// </summary>
public static class LibrarySettings
{
    public const string WatchForChangesKey = "LibraryWatchForChanges";
    public const string IndexImagesKey = "LibraryIndexImages";
    public const string IndexVideoKey = "LibraryIndexVideo";
    public const string GenerateThumbsOnScanKey = "LibraryGenerateThumbsOnScan";

    public const bool DefaultWatchForChanges = true;
    public const bool DefaultIndexImages = true;
    public const bool DefaultIndexVideo = true;
    public const bool DefaultGenerateThumbsOnScan = true;

    public static bool WatchForChanges { get; private set; } = DefaultWatchForChanges;

    public static bool IndexImages { get; private set; } = DefaultIndexImages;

    public static bool IndexVideo { get; private set; } = DefaultIndexVideo;

    public static bool GenerateThumbsOnScan { get; private set; } = DefaultGenerateThumbsOnScan;

    public static event EventHandler? Changed;

    public static void Apply(
        bool watchForChanges,
        bool indexImages,
        bool indexVideo,
        bool generateThumbsOnScan)
    {
        if (WatchForChanges == watchForChanges
            && IndexImages == indexImages
            && IndexVideo == indexVideo
            && GenerateThumbsOnScan == generateThumbsOnScan)
        {
            return;
        }

        WatchForChanges = watchForChanges;
        IndexImages = indexImages;
        IndexVideo = indexVideo;
        GenerateThumbsOnScan = generateThumbsOnScan;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static bool ParseBool(object? stored, bool defaultValue) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => defaultValue
    };

    /// <summary>
    /// Whether scan / watcher should index this extension under current format toggles.
    /// GIFs follow the Images toggle. 3D / audio stay out until those kinds ship.
    /// </summary>
    public static bool IsIndexedExt(string? pathOrExt)
    {
        var ext = PathSafe.Extension(pathOrExt);
        if (!PathSafe.IsCatalogExt(ext))
        {
            return false;
        }

        return PathSafe.KindFromExt(ext) switch
        {
            AssetKind.Video => IndexVideo,
            AssetKind.Gif => IndexImages,
            AssetKind.Image => IndexImages,
            _ => false
        };
    }

    /// <summary>One-line caption of image extensions Palace indexes today.</summary>
    public static string ImagesFormatsCaption { get; } =
        "JPG, PNG, WEBP, GIF, AVIF, HEIC, JXR, JXL, HDR, PSD, EXR, TGA, and more";

    /// <summary>One-line caption of video extensions Palace indexes today.</summary>
    public static string VideoFormatsCaption { get; } =
        "MP4, MOV, MKV, WEBM, AVI";

    public static string FormatsPlaceholderCaption { get; } =
        "3D and audio indexing land when those kinds ship.";
}
