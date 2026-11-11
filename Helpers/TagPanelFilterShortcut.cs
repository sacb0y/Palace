namespace Palace.Helpers;

/// <summary>
/// Pure gates for Library Tags panel Ctrl+F → focus filter (t8-01 / audit #30).
/// Wired on LibraryRoot PreviewKeyDown; only while tag-browse is open.
/// </summary>
public static class TagPanelFilterShortcut
{
    /// <summary>WinRT <c>VirtualKey.F</c> / ASCII <c>'F'</c>.</summary>
    public const int KeyF = 70;

    public static bool ShouldHandle(
        int key,
        bool controlDown,
        bool menuDown,
        bool shiftDown,
        bool galleryOverlayOpen,
        bool isTagBrowse) =>
        key == KeyF
        && controlDown
        && !menuDown
        && !shiftDown
        && !galleryOverlayOpen
        && isTagBrowse;
}
