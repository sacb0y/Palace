using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Select-mode command bar + multi-select preview rules (WinUI-free).
/// Spec: selection-mockups-v2 — 44 px bar, overflow fold order, Rooms disabled.
/// </summary>
public static class SelectCommandBar
{
    public const double TargetHeightDip = 44;

    /// <summary>Max stacked thumbs in the multi-select preview.</summary>
    public const int MaxStackedThumbs = 5;

    /// <summary>Rooms pin UI exists, but batch Add-to-room stays disabled until Rooms is finished.</summary>
    public const bool RoomsBatchEnabled = false;

    public static string ToggleLabel(bool selectMode) => selectMode ? "Done" : "Select";

    public static string ToggleAutomationName(bool selectMode) =>
        selectMode ? "Done" : "Select";

    /// <summary>Toolbar Add to room stays off while the bar owns selection Add to room.</summary>
    public static bool ToolbarAddToRoomEnabled(bool selectMode, bool hasSelection) =>
        !selectMode && hasSelection;

    /// <summary>Command-bar Add to room: visible, disabled until Rooms backend is ready.</summary>
    public static bool BarAddToRoomEnabled(bool hasSelection) =>
        RoomsBatchEnabled && hasSelection;

    /// <summary>Multi-select summary replaces the single-asset preview when 2+ tiles are selected.</summary>
    public static bool ShowMultiSelectPreview(int selectedCount) => selectedCount >= 2;

    public static int StackedThumbCount(int selectedCount) =>
        selectedCount <= 0 ? 0 : Math.Min(selectedCount, MaxStackedThumbs);

    /// <summary>
    /// Narrow fold from the right: Move → Add to room → Tag selected….
    /// Width is the command-bar row's available DIP width.
    /// </summary>
    public static SelectBarOverflow ResolveOverflow(double availableWidthDip)
    {
        // Approximate content width with 44 px targets + labels + spacing.
        var showMove = availableWidthDip >= 920;
        var showAddToRoom = availableWidthDip >= 800;
        var showTagSelected = availableWidthDip >= 680;
        return new SelectBarOverflow(showMove, showAddToRoom, showTagSelected);
    }

    public static bool OverflowHasFoldedActions(SelectBarOverflow overflow) =>
        !overflow.ShowMove || !overflow.ShowAddToRoom || !overflow.ShowTagSelected;

    /// <summary>
    /// Preview detail under "N selected": type counts + total size.
    /// Example: <c>3 images · 1 video · 12.4 MB</c>.
    /// </summary>
    public static string MultiSelectDetail(
        IReadOnlyList<AssetKind> kinds,
        IReadOnlyList<long?> sizes)
    {
        var images = 0;
        var videos = 0;
        var other = 0;
        long total = 0;
        var sizeKnown = false;
        var n = Math.Min(kinds.Count, sizes.Count);
        for (var i = 0; i < n; i++)
        {
            switch (kinds[i])
            {
                case AssetKind.Image:
                case AssetKind.Gif:
                    images++;
                    break;
                case AssetKind.Video:
                    videos++;
                    break;
                default:
                    other++;
                    break;
            }

            if (sizes[i] is long bytes and >= 0)
            {
                total += bytes;
                sizeKnown = true;
            }
        }

        var parts = new List<string>();
        if (images > 0)
        {
            parts.Add(images == 1 ? "1 image" : $"{images} images");
        }

        if (videos > 0)
        {
            parts.Add(videos == 1 ? "1 video" : $"{videos} videos");
        }

        if (other > 0)
        {
            parts.Add(other == 1 ? "1 other" : $"{other} other");
        }

        if (sizeKnown)
        {
            parts.Add(GalleryPresent.FormatSize(total));
        }

        return parts.Count == 0 ? "" : string.Join(" · ", parts);
    }

    /// <summary>True when selected ratings differ (preview shows "Mixed").</summary>
    public static bool RatingIsMixed(IReadOnlyList<int?> ratings)
    {
        if (ratings.Count <= 1)
        {
            return false;
        }

        var first = ratings[0] ?? 0;
        for (var i = 1; i < ratings.Count; i++)
        {
            if ((ratings[i] ?? 0) != first)
            {
                return true;
            }
        }

        return false;
    }

    public static double RatingDisplayValue(IReadOnlyList<int?> ratings)
    {
        if (ratings.Count == 0 || RatingIsMixed(ratings))
        {
            return 0;
        }

        return ratings[0] ?? 0;
    }

    public static string RatingCaption(bool mixed) => mixed ? "Mixed" : "Rating";

    public static string DeleteTitle(int count) =>
        count == 1 ? "Delete 1 item?" : $"Delete {Math.Max(0, count)} items?";

    public static string DeleteBody(int count)
    {
        var n = Math.Max(0, count);
        var files = n == 1 ? "The 1 file goes" : $"The {n} files go";
        return $"{files} to the Recycle Bin and leave the catalog. You can restore them from the Recycle Bin.";
    }

    public static string DeletePrimary => "Delete";
}

/// <summary>Which mid-bar actions stay in the row (rest fold into ⋯).</summary>
public readonly record struct SelectBarOverflow(bool ShowMove, bool ShowAddToRoom, bool ShowTagSelected);
