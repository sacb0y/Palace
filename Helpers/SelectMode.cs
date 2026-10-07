namespace Palace.Helpers;

/// <summary>
/// Select-mode rules for the Library and Tags mosaics. Off WinUI so the
/// toggle and count text are testable.
/// </summary>
public static class SelectMode
{
    /// <summary>
    /// True when a plain click or tap on a tile should toggle it
    /// (<c>ItemsViewSelectionMode.Multiple</c>). Off is a single preview tile
    /// (<c>Single</c>) — never Extended Ctrl/Shift multi-select. Space opens overlay, not toggle.
    /// </summary>
    public static bool ClickTogglesTile(bool selectMode) => selectMode;

    /// <summary>
    /// Press-and-hold / click-and-hold on a mosaic tile (not a folder header,
    /// not while the overlay is open) enters select mode and selects that tile.
    /// Does not open the overlay — double-click / Enter / context Open still do.
    /// </summary>
    public static bool EnterFromHold(bool selectMode, bool overlayOpen, bool isFolderHeader) =>
        !selectMode && !overlayOpen && !isFolderHeader;

    /// <summary>Count line beside the toggle. Empty when select mode is off and nothing is selected.</summary>
    public static string Summary(bool selectMode, int selectedCount)
    {
        if (selectedCount > 0)
        {
            return $"{selectedCount} selected";
        }

        return selectMode ? "Click or drag tiles to select" : "";
    }

    /// <summary>
    /// Select-mode chrome shows <c>Tag selected…</c> only when mode is on and tiles are selected.
    /// </summary>
    public static bool ShowBatchTagCta(bool selectMode, int selectedCount) =>
        selectMode && selectedCount > 0;
}
