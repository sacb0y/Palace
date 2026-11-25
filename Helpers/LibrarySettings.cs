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
    public const string IndexModel3DKey = "LibraryIndexModel3D";
    public const string GenerateThumbsOnScanKey = "LibraryGenerateThumbsOnScan";

    public const bool DefaultWatchForChanges = true;
    public const bool DefaultIndexImages = true;
    public const bool DefaultIndexVideo = true;
    /// <summary>Off until mesh thumbs / out-of-proc probe (D1) ship.</summary>
    public const bool DefaultIndexModel3D = false;
    public const bool DefaultGenerateThumbsOnScan = true;

    public static bool WatchForChanges { get; private set; } = DefaultWatchForChanges;

    public static bool IndexImages { get; private set; } = DefaultIndexImages;

    public static bool IndexVideo { get; private set; } = DefaultIndexVideo;

    public static bool IndexModel3D { get; private set; } = DefaultIndexModel3D;

    public static bool GenerateThumbsOnScan { get; private set; } = DefaultGenerateThumbsOnScan;

    public static event EventHandler? Changed;

    public static void Apply(
        bool watchForChanges,
        bool indexImages,
        bool indexVideo,
        bool generateThumbsOnScan,
        bool indexModel3D = DefaultIndexModel3D)
    {
        if (WatchForChanges == watchForChanges
            && IndexImages == indexImages
            && IndexVideo == indexVideo
            && IndexModel3D == indexModel3D
            && GenerateThumbsOnScan == generateThumbsOnScan)
        {
            return;
        }

        WatchForChanges = watchForChanges;
        IndexImages = indexImages;
        IndexVideo = indexVideo;
        IndexModel3D = indexModel3D;
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
    /// GIFs follow the Images toggle. Audio stays out until that kind ships.
    /// </summary>
    public static bool IsIndexedExt(string? pathOrExt)
    {
        var ext = PathSafe.Extension(pathOrExt);
        if (!PathSafe.IsCatalogExt(ext))
        {
            return false;
        }

        return AssetKindRegistry.Get(PathSafe.KindFromExt(ext)).IndexEnabled();
    }

    /// <summary>One-line caption of image extensions Palace indexes today.</summary>
    public static string ImagesFormatsCaption { get; } =
        "JPG, PNG, WEBP, GIF, AVIF, HEIC, JXR, JXL, HDR, PSD, EXR, TGA, and more";

    /// <summary>One-line caption of video extensions Palace indexes today.</summary>
    public static string VideoFormatsCaption { get; } =
        "MP4, MOV, MKV, WEBM, AVI";

    /// <summary>One-line caption of 3D extensions (index toggle default off).</summary>
    public static string Model3DFormatsCaption { get; } =
        "GLB, GLTF, FBX";

    public static string FormatsPlaceholderCaption { get; } =
        "Audio indexing lands when that kind ships.";
}
