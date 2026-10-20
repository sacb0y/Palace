namespace Palace.Helpers;

/// <summary>
/// Overlay / GalleryWindow Details and video-loop chrome. Persist via
/// LocalSettings (<see cref="ShowDetailsKey"/> / <see cref="LoopVideoKey"/> /
/// section collapse keys). The Info pill toggles the single floating Details
/// card (<see cref="ShowDetailsKey"/>). First-run Details defaults on; Off
/// sticks until toggled. Video loop defaults on. Off WinUI.
/// </summary>
public static class GalleryChrome
{
    public const string ShowDetailsKey = "GalleryShowDetails";
    public const string LoopVideoKey = "GalleryLoopVideo";
    public const string DetailsSectionKeyPrefix = "GalleryDetailsSection_";

    public const bool DefaultShowDetails = true;
    public const bool DefaultLoopVideo = true;

    public static string DetailsSectionKey(string moduleId) =>
        DetailsSectionKeyPrefix + moduleId;

    public static bool ParseShowDetails(object? stored) =>
        ParseBool(stored, DefaultShowDetails);

    public static bool ParseLoopVideo(object? stored) =>
        ParseBool(stored, DefaultLoopVideo);

    public static bool ParseSectionExpanded(object? stored, bool defaultExpanded) =>
        ParseBool(stored, defaultExpanded);

    public static bool ParseBool(object? stored, bool defaultValue) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => defaultValue
    };
}
