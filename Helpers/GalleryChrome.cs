namespace Palace.Helpers;

/// <summary>
/// Overlay / GalleryWindow Details, Info, and video-loop chrome. Persist via
/// LocalSettings (<see cref="ShowDetailsKey"/> / <see cref="ShowImageInfoKey"/> /
/// <see cref="LoopVideoKey"/> / section collapse keys). First-run Details/Info
/// keys each default on, but the floating Details card and top-left Info panel
/// are exclusive — <see cref="CoerceExclusivePanels"/> keeps only one open
/// (Details wins when both would be on). Video loop defaults on. Off sticks
/// until toggled. Off WinUI.
/// </summary>
public static class GalleryChrome
{
    public const string ShowDetailsKey = "GalleryShowDetails";
    public const string ShowImageInfoKey = "GalleryShowImageInfo";
    public const string LoopVideoKey = "GalleryLoopVideo";
    public const string DetailsSectionKeyPrefix = "GalleryDetailsSection_";

    public const bool DefaultShowDetails = true;
    public const bool DefaultShowImageInfo = true;
    public const bool DefaultLoopVideo = true;

    public static string DetailsSectionKey(string moduleId) =>
        DetailsSectionKeyPrefix + moduleId;

    public static bool ParseShowDetails(object? stored) =>
        ParseBool(stored, DefaultShowDetails);

    public static bool ParseShowImageInfo(object? stored) =>
        ParseBool(stored, DefaultShowImageInfo);

    public static bool ParseLoopVideo(object? stored) =>
        ParseBool(stored, DefaultLoopVideo);

    public static bool ParseSectionExpanded(object? stored, bool defaultExpanded) =>
        ParseBool(stored, defaultExpanded);

    /// <summary>
    /// Floating Details and the top-left Info card must not stack (Details would
    /// cover Info and let it show through). Opening either panel closes the
    /// other. When both prefs request open (legacy / first-run defaults), Details
    /// wins so the richer card stays.
    /// </summary>
    public static (bool showDetails, bool showImageInfo) CoerceExclusivePanels(
        bool showDetails,
        bool showImageInfo)
    {
        if (showDetails && showImageInfo)
        {
            return (true, false);
        }

        return (showDetails, showImageInfo);
    }

    public static bool ParseBool(object? stored, bool defaultValue) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => defaultValue
    };
}
