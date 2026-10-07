namespace Palace.Helpers;

/// <summary>
/// Overlay / GalleryWindow Details, Info, and video-loop chrome. Persist via
/// LocalSettings (<see cref="ShowDetailsKey"/> / <see cref="ShowImageInfoKey"/> /
/// <see cref="LoopVideoKey"/>). First-run Details/Info default on; video loop
/// defaults on. Off sticks until toggled. Off WinUI.
/// </summary>
public static class GalleryChrome
{
    public const string ShowDetailsKey = "GalleryShowDetails";
    public const string ShowImageInfoKey = "GalleryShowImageInfo";
    public const string LoopVideoKey = "GalleryLoopVideo";

    public const bool DefaultShowDetails = true;
    public const bool DefaultShowImageInfo = true;
    public const bool DefaultLoopVideo = true;

    public static bool ParseShowDetails(object? stored) =>
        ParseBool(stored, DefaultShowDetails);

    public static bool ParseShowImageInfo(object? stored) =>
        ParseBool(stored, DefaultShowImageInfo);

    public static bool ParseLoopVideo(object? stored) =>
        ParseBool(stored, DefaultLoopVideo);

    public static bool ParseBool(object? stored, bool defaultValue) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => defaultValue
    };
}
