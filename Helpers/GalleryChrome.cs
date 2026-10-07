namespace Palace.Helpers;

/// <summary>
/// Overlay / GalleryWindow Details and Info chrome. Persist via LocalSettings
/// (<see cref="ShowDetailsKey"/> / <see cref="ShowImageInfoKey"/>). First-run
/// defaults on; Off sticks until toggled on again. Off WinUI.
/// </summary>
public static class GalleryChrome
{
    public const string ShowDetailsKey = "GalleryShowDetails";
    public const string ShowImageInfoKey = "GalleryShowImageInfo";

    public const bool DefaultShowDetails = true;
    public const bool DefaultShowImageInfo = true;

    public static bool ParseShowDetails(object? stored) =>
        ParseBool(stored, DefaultShowDetails);

    public static bool ParseShowImageInfo(object? stored) =>
        ParseBool(stored, DefaultShowImageInfo);

    public static bool ParseBool(object? stored, bool defaultValue) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => defaultValue
    };
}
