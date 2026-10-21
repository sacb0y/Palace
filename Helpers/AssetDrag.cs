namespace Palace.Helpers;

/// <summary>
/// Drag mosaic tiles onto a folder in the tree. Mouse only: touch / pen hold already means
/// "enter select mode", and in select mode a drag that starts on an unselected tile is a range select.
/// </summary>
public static class AssetDrag
{
    /// <summary>DataPackage property carrying newline-joined catalog asset ids.</summary>
    public const string DataKey = "PalaceAssetIds";

    public static bool CanStart(bool isMouse, bool isSelectMode, bool isSelected, bool isFolderHeader, bool overlayOpen) =>
        isMouse && !isFolderHeader && !overlayOpen && (!isSelectMode || isSelected);

    /// <summary>
    /// <c>ItemContainer.CanDrag</c> steals touch pan from the mosaic ScrollViewer.
    /// Arm it only for a mouse press that may start a folder drag; keep false for
    /// touch / pen so finger drag scrolls (tap / long-press select stay intact).
    /// </summary>
    public static bool ArmsItemContainerCanDrag(
        bool isMouse,
        bool isSelectMode,
        bool isSelected,
        bool isFolderHeader,
        bool overlayOpen) =>
        CanStart(isMouse, isSelectMode, isSelected, isFolderHeader, overlayOpen);

    /// <summary>Mosaic touch pan scrolls only while the tile is not drag-armed.</summary>
    public static bool AllowsMosaicTouchScroll(bool itemContainerCanDrag) => !itemContainerCanDrag;

    /// <summary>
    /// ItemsView tiles must use <c>ManipulationMode=System</c> so the parent
    /// ScrollView/ScrollViewer receives touch pan. <c>None</c> (used historically
    /// for DoubleTapped) swallows finger-drag and leaves the mosaic stuck.
    /// Tap / double-tap / Holding still work with System.
    /// </summary>
    public const string MosaicItemManipulationMode = "System";

    public static bool MosaicItemAllowsParentTouchScroll(string manipulationMode) =>
        string.Equals(manipulationMode, MosaicItemManipulationMode, StringComparison.Ordinal);

    /// <summary>Range select yields to drag when the press lands on an already selected tile.</summary>
    public static bool YieldsRangeSelect(bool isMouse, bool isSelected) => isMouse && isSelected;

    /// <summary>The dragged tile plus the rest of the selection when it is part of it; else just the tile.</summary>
    public static IReadOnlyList<string> Ids(string itemId, bool itemSelected, IEnumerable<string> selectedIds)
    {
        if (!itemSelected)
        {
            return string.IsNullOrEmpty(itemId) ? [] : [itemId];
        }

        var ids = selectedIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
        if (!string.IsNullOrEmpty(itemId) && !ids.Contains(itemId, StringComparer.Ordinal))
        {
            ids.Insert(0, itemId);
        }

        return ids;
    }

    public static string Serialize(IEnumerable<string> ids) => string.Join('\n', ids);

    public static IReadOnlyList<string> Parse(object? value) =>
        value is string text
            ? text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList()
            : [];

    public static string Caption(int count, string folderName) =>
        count == 1 ? $"Move to {folderName}" : $"Move {count} files to {folderName}";
}
