namespace Palace.Helpers;

/// <summary>
/// Pure gates for Library shortcut <c>T</c> → focus Add tags.
/// Wired on LibraryRoot PreviewKeyDown (tunnel) and GrdAssets KeyDown
/// (handledEventsToo, same pattern as Move <c>M</c>) so ItemsView children
/// cannot swallow the key. Never use a page KeyboardAccelerator for bare T —
/// that would steal typing in TextBox / AutoSuggestBox / NumberBox / editable ComboBox.
/// </summary>
public static class LibraryAddTagsShortcut
{
    /// <summary>WinRT <c>VirtualKey.T</c> / ASCII <c>'T'</c>.</summary>
    public const int KeyT = 84;

    /// <summary>WinRT <c>VirtualKey.M</c> / ASCII <c>'M'</c> (negative control in tests).</summary>
    public const int KeyM = 77;

    public static bool ShouldHandle(
        int key,
        bool galleryOverlayOpen,
        bool controlDown,
        bool menuDown,
        bool shiftDown,
        bool focusIsTextInput,
        bool hasSelection) =>
        key == KeyT
        && !galleryOverlayOpen
        && !controlDown
        && !menuDown
        && !shiftDown
        && !focusIsTextInput
        && hasSelection;
}
