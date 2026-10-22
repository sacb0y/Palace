using System.Security.Cryptography;
using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Per-folder Library mosaic prefs: flatten subfolders, and which child
/// sections are collapsed. Off WinUI so Palace.Tests can cover the keys.
/// Keys hash the folder path (LocalSettings has no catalog column yet —
/// deviation from storing layout prefs on the folder row).
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
        FlattenPrefix + PathHash(folderPath);

    public static string CollapsedKey(string folderPath) =>
        CollapsedPrefix + PathHash(folderPath);

    /// <summary>Stable short hex of the normalized path (not reversible).</summary>
    public static string PathHash(string? path)
    {
        var norm = NormalizePath(path);
        if (norm.Length == 0)
        {
            return "0";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(norm));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

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
    /// Drop collapsed paths that are no longer live section folders under the browse root.
    /// </summary>
    public static HashSet<string> PruneCollapsed(
        IReadOnlySet<string> collapsed,
        IEnumerable<string> liveSectionPaths)
    {
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in liveSectionPaths)
        {
            var norm = NormalizePath(path);
            if (norm.Length > 0)
            {
                live.Add(norm);
            }
        }

        var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in collapsed)
        {
            var norm = NormalizePath(path);
            if (live.Contains(norm))
            {
                next.Add(norm);
            }
        }

        return next;
    }

    /// <summary>
    /// Legacy keys stored the raw path after the prefix; hashed keys are hex only.
    /// </summary>
    public static bool IsLegacySettingsKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        string rest;
        if (key.StartsWith(FlattenPrefix, StringComparison.Ordinal))
        {
            rest = key[FlattenPrefix.Length..];
        }
        else if (key.StartsWith(CollapsedPrefix, StringComparison.Ordinal))
        {
            rest = key[CollapsedPrefix.Length..];
        }
        else
        {
            return false;
        }

        if (rest.Length == 0)
        {
            return false;
        }

        // Hashed keys are 16 lowercase hex chars (8 bytes).
        if (rest.Length == 16 && rest.All(IsHex))
        {
            return false;
        }

        return true;
    }

    public static int PruneLegacySettingsKeys(IDictionary<string, object> values)
    {
        var remove = values.Keys.Where(IsLegacySettingsKey).ToList();
        foreach (var key in remove)
        {
            values.Remove(key);
        }

        return remove.Count;
    }

    /// <summary>Overload for WinRT LocalSettings (IPropertySet keys are object-enumerable).</summary>
    public static int PruneLegacySettingsKeys(IEnumerable<KeyValuePair<string, object>> entries, Action<string> remove)
    {
        var count = 0;
        foreach (var entry in entries.ToList())
        {
            if (IsLegacySettingsKey(entry.Key))
            {
                remove(entry.Key);
                count++;
            }
        }

        return count;
    }

    public static bool TrySave(Action save)
    {
        try
        {
            save();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
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

    /// <summary>
    /// Display mosaic omits tiles under collapsed headers; the full ordered
    /// list still holds them for Review / select-all / range.
    /// </summary>
    public static List<T> FilterCollapsedDisplay<T>(
        IReadOnlyList<T> ordered,
        Func<T, bool> isHeader,
        Func<T, bool> isCollapsed)
    {
        var list = new List<T>(ordered.Count);
        var hide = false;
        foreach (var item in ordered)
        {
            if (isHeader(item))
            {
                hide = isCollapsed(item);
                list.Add(item);
                continue;
            }

            if (!hide)
            {
                list.Add(item);
            }
        }

        return list;
    }

    private static bool IsHex(char c) =>
        c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
}
