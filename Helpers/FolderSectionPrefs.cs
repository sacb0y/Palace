namespace Palace.Helpers;

/// <summary>
/// Per-folder Library mosaic prefs: flatten subfolders, and which child
/// sections are collapsed. Off WinUI so Palace.Tests can cover the keys.
/// </summary>
public static class FolderSectionPrefs
{
    public const string FlattenPrefix = "FolderFlatten_";
    public const string CollapsedPrefix = "FolderCollapsed_";

    /// <summary>Sections smaller than this fraction of the row pack side by side.</summary>
    public const double PackRowFraction = 0.6;

    /// <summary>Portrait tiles may not be narrower than this × row height.</summary>
    public const double MinPortraitAspect = 0.6;

    public static string FlattenKey(string folderPath) =>
        FlattenPrefix + NormalizePath(folderPath);

    public static string CollapsedKey(string folderPath) =>
        CollapsedPrefix + NormalizePath(folderPath);

    public static string NormalizePath(string? path)
    {
        var text = (path ?? "").Trim().TrimEnd('\\', '/').ToLowerInvariant();
        return text.Replace('\\', '/');
    }

    public static bool ParseBool(object? stored, bool defaultValue = false) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        int i => i != 0,
        _ => defaultValue
    };

    public static HashSet<string> ParseCollapsed(object? stored)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (stored is not string text || string.IsNullOrWhiteSpace(text))
        {
            return set;
        }

        foreach (var part in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var path = NormalizePath(part);
            if (path.Length > 0)
            {
                set.Add(path);
            }
        }

        return set;
    }

    public static string SerializeCollapsed(IEnumerable<string> paths) =>
        string.Join(
            "\n",
            paths
                .Select(NormalizePath)
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

    public static bool IsCollapsed(IReadOnlySet<string> collapsed, string? sectionPath) =>
        collapsed.Contains(NormalizePath(sectionPath));

    public static HashSet<string> ToggleCollapsed(IReadOnlySet<string> collapsed, string sectionPath)
    {
        var next = new HashSet<string>(collapsed, StringComparer.OrdinalIgnoreCase);
        var key = NormalizePath(sectionPath);
        if (key.Length == 0)
        {
            return next;
        }

        if (!next.Add(key))
        {
            next.Remove(key);
        }

        return next;
    }

    /// <summary>
    /// Natural justified width of <paramref name="aspects"/> at
    /// <paramref name="rowHeight"/> (spacing between tiles only).
    /// </summary>
    public static double SectionNaturalWidth(
        IReadOnlyList<double> aspects,
        double rowHeight,
        double spacing)
    {
        if (aspects.Count == 0 || rowHeight <= 0)
        {
            return 0;
        }

        var sum = 0d;
        foreach (var aspect in aspects)
        {
            sum += rowHeight * MosaicRows.ClampTileAspect(aspect);
        }

        return sum + spacing * Math.Max(0, aspects.Count - 1);
    }

    public static bool IsSmallSection(
        IReadOnlyList<double> aspects,
        double rowWidth,
        double rowHeight,
        double spacing,
        double fraction = PackRowFraction) =>
        rowWidth > 0
        && SectionNaturalWidth(aspects, rowHeight, spacing) < rowWidth * Math.Clamp(fraction, 0.1, 1);
}
