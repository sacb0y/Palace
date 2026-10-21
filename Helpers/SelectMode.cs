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

    /// <summary>
    /// In select mode, long-press opens the batch ContextFlyout (same as right-click).
    /// Enter-select hold still uses <see cref="EnterFromHold"/> when mode is off.
    /// </summary>
    public static bool OpensBatchContextFromHold(bool selectMode, bool overlayOpen, bool isFolderHeader) =>
        selectMode && !overlayOpen && !isFolderHeader;

    /// <summary>
    /// Viewer overlay long-press opens ContextFlyout (not a select badge).
    /// Prefer <see cref="TouchGestures.ViewerHoldOpensContext"/>.
    /// </summary>
    public static bool ViewerHoldTogglesSelection(bool overlayOpen) =>
        TouchGestures.ViewerHoldTogglesSelection(overlayOpen);

    /// <summary>
    /// Viewer long-press / … / right-click share one ContextFlyout. Review mode
    /// (#129) does nothing on long-press.
    /// </summary>
    public static bool ViewerHoldOpensContext(bool overlayOpen, bool reviewMode) =>
        TouchGestures.ViewerHoldOpensContext(overlayOpen, reviewMode);

    /// <summary>
    /// Batch context: when the pressed tile is not already selected, add it —
    /// never replace the existing selection. Outside select mode, right-click
    /// still replaces (single-item context).
    /// </summary>
    public static bool AddsPressedToSelection(bool selectMode, bool alreadySelected) =>
        selectMode && !alreadySelected;

    /// <summary>Outside select mode, context open replaces the selection with the pressed tile.</summary>
    public static bool ReplacesSelectionForContext(bool selectMode) => !selectMode;

    /// <summary>Batch ContextFlyout header: <c>N selected</c>.</summary>
    public static string BatchMenuHeader(int selectedCount) =>
        selectedCount <= 0 ? "0 selected" : $"{selectedCount} selected";

    /// <summary>Esc exits select mode while the overlay is closed (selection kept).</summary>
    public static bool ExitsSelectModeOnEscape(bool selectMode, bool overlayOpen) =>
        selectMode && !overlayOpen;

    /// <summary>Clear selection exits select mode.</summary>
    public static bool ExitsSelectModeOnClear(bool selectMode) => selectMode;

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
