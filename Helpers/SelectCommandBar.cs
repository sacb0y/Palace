namespace Palace.Helpers;

/// <summary>
/// Select-mode command bar + multi-select preview rules (WinUI-free).
/// Spec: selection-mockups-v2 — 44 px bar, overflow fold order, Rooms disabled.
/// Multi-select header: T2 fan stack of 3 + extension breakdown (audit b).
/// </summary>
public static class SelectCommandBar
{
    public const double TargetHeightDip = 44;

    /// <summary>Max fanned thumbs in the multi-select preview (T2 mockup).</summary>
    public const int MaxStackedThumbs = 3;

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
    /// Fan-stack layout for up to 3 thumbs (angle deg, Canvas left/top) from t2-01.
    /// Index 0 is the back card; the last index is front-most.
    /// </summary>
    public static IReadOnlyList<(double Angle, double Left, double Top)> FanLayout(int thumbCount)
    {
        var n = Math.Clamp(thumbCount, 0, MaxStackedThumbs);
        return n switch
        {
            0 => [],
            1 => [(0, 40, 10)],
            2 => [(-4, 22, 14), (4, 58, 8)],
            _ => [(-4, 18, 14), (0, 44, 10), (4, 70, 6)]
        };
    }

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
    /// Extension label for the multi-select breakdown (e.g. <c>PNG</c>, <c>JPG</c>).
    /// </summary>
    public static string ExtensionLabel(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "").TrimStart('.');
        return ext.Length == 0 ? "OTHER" : ext.ToUpperInvariant();
    }

    /// <summary>
    /// Preview detail under "N selected": by-extension counts + total size.
    /// Example: <c>19 PNG · 5 JPG · 186 MB</c> (never "images" / "videos").
    /// </summary>
    public static string MultiSelectDetail(
        IReadOnlyList<string> fileNames,
        IReadOnlyList<long?> sizes)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        long total = 0;
        var sizeKnown = false;
        var n = Math.Min(fileNames.Count, sizes.Count);
        for (var i = 0; i < n; i++)
        {
            var label = ExtensionLabel(fileNames[i]);
            counts[label] = counts.TryGetValue(label, out var c) ? c + 1 : 1;

            if (sizes[i] is long bytes and >= 0)
            {
                total += bytes;
                sizeKnown = true;
            }
        }

        // Also count any leftover names when sizes are shorter (size still optional).
        for (var i = n; i < fileNames.Count; i++)
        {
            var label = ExtensionLabel(fileNames[i]);
            counts[label] = counts.TryGetValue(label, out var c) ? c + 1 : 1;
        }

        var parts = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Value} {kv.Key}")
            .ToList();

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

    /// <summary>
    /// Short filename / "N files (a, b, c, …)" lead-in for the delete confirm.
    /// </summary>
    public static string DeleteFilePreview(IReadOnlyList<string> fileNames)
    {
        if (fileNames.Count == 0)
        {
            return "";
        }

        if (fileNames.Count == 1)
        {
            return fileNames[0];
        }

        var shown = string.Join(", ", fileNames.Take(3));
        return fileNames.Count > 3
            ? $"{fileNames.Count} files ({shown}, …)"
            : $"{fileNames.Count} files ({shown})";
    }

    public static string DeleteBody(int count, IReadOnlyList<string>? fileNames = null)
    {
        var n = Math.Max(0, count);
        var preview = fileNames is { Count: > 0 } ? DeleteFilePreview(fileNames) : "";
        if (!string.IsNullOrEmpty(preview))
        {
            return $"Send {preview} to the Recycle Bin? This also removes them from the catalog. You can restore them from the Recycle Bin.";
        }

        var files = n == 1 ? "The 1 file goes" : $"The {n} files go";
        return $"{files} to the Recycle Bin and leave the catalog. You can restore them from the Recycle Bin.";
    }

    public static string DeletePrimary => "Delete";
}

/// <summary>Which mid-bar actions stay in the row (rest fold into ⋯).</summary>
public readonly record struct SelectBarOverflow(bool ShowMove, bool ShowAddToRoom, bool ShowTagSelected);
