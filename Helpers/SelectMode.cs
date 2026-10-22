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
    /// Touch long-press is select (not the context menu — that is "…" / right-click).
    /// </summary>
    public static bool EnterFromHold(bool selectMode, bool overlayOpen, bool isFolderHeader) =>
        !selectMode && !overlayOpen && !isFolderHeader;

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
    /// Esc exits select mode while the overlay is closed (selection kept —
    /// Clear is the path that drops tiles).
    /// </summary>
    public static bool ExitsSelectModeOnEscape(bool selectMode, bool overlayOpen) =>
        selectMode && !overlayOpen;

    /// <summary>
    /// Trailing clause when some selected tiles live under collapsed sections:
    /// <c> · 30 in collapsed sections</c>.
    /// </summary>
    public static string CollapsedClause(int collapsedSelectedCount) =>
        collapsedSelectedCount > 0
            ? $" · {collapsedSelectedCount} in collapsed sections"
            : "";

    /// <summary>
    /// Count line beside the toggle / delete-move confirm. Empty when select
    /// mode is off and nothing is selected. When some selected tiles live under
    /// collapsed sections: <c>42 selected · 30 in collapsed sections</c>.
    /// Esc exits select mode but keeps the count line (selection is not cleared).
    /// </summary>
    public static string Summary(bool selectMode, int selectedCount, int collapsedSelectedCount = 0)
    {
        if (selectedCount > 0)
        {
            return $"{selectedCount} selected{CollapsedClause(collapsedSelectedCount)}";
        }

        return selectMode ? "Click or drag tiles to select" : "";
    }

    /// <summary>
    /// How many of <paramref name="targetIds"/> are ordered mosaic tiles that are
    /// not in the current display set (collapsed-section / hidden).
    /// </summary>
    public static int CountCollapsedAmongTargets<T>(
        IReadOnlyList<T> ordered,
        Func<T, bool> isHeader,
        Func<T, string?> id,
        IReadOnlySet<T> displayed,
        IEnumerable<string> targetIds)
        where T : class
    {
        var ids = targetIds as HashSet<string>
            ?? new HashSet<string>(
                targetIds.Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.Ordinal);
        if (ids.Count == 0 || ordered.Count == 0)
        {
            return 0;
        }

        var n = 0;
        foreach (var item in ordered)
        {
            if (isHeader(item))
            {
                continue;
            }

            var assetId = id(item);
            if (assetId is null || !ids.Contains(assetId) || displayed.Contains(item))
            {
                continue;
            }

            n++;
        }

        return n;
    }

    /// <summary>
    /// Select-mode chrome shows <c>Tag selected…</c> only when mode is on and tiles are selected.
    /// </summary>
    public static bool ShowBatchTagCta(bool selectMode, int selectedCount) =>
        selectMode && selectedCount > 0;
}
