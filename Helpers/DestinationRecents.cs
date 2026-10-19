namespace Palace.Helpers;

/// <summary>
/// Recent "Move to" destinations (newest first, de-duplicated ignoring case, capped).
/// Stored per project in LocalSettings as one newline-separated string.
/// </summary>
public static class DestinationRecents
{
    public const int Max = 8;

    public static string SettingsKey(string projectId) => $"MoveRecents_{projectId}";

    public static IReadOnlyList<string> Parse(object? stored)
    {
        if (stored is not string text || string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return Normalize(text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static string Serialize(IEnumerable<string> recents) =>
        string.Join('\n', Normalize(recents));

    /// <summary>Put <paramref name="path"/> first; drop an older copy; keep at most <see cref="Max"/>.</summary>
    public static IReadOnlyList<string> Push(IEnumerable<string> recents, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Normalize(recents);
        }

        var trimmed = TrimSeparators(path);
        return Normalize(new[] { trimmed }.Concat(recents));
    }

    /// <summary>Rewrite entries at or under a renamed folder; drop entries under a deleted one.</summary>
    public static IReadOnlyList<string> Rebase(IEnumerable<string> recents, string oldDir, string? newDir)
    {
        var list = new List<string>();
        foreach (var entry in recents)
        {
            if (FolderNames.IsSameOrUnder(entry, oldDir))
            {
                if (newDir is null)
                {
                    continue;
                }

                var rel = Path.GetRelativePath(oldDir, entry);
                list.Add(rel == "." ? TrimSeparators(newDir) : Path.Combine(TrimSeparators(newDir), rel));
                continue;
            }

            list.Add(entry);
        }

        return Normalize(list);
    }

    /// <summary>Entries that still exist and sit inside a watched folder (for the menu).</summary>
    public static IReadOnlyList<string> Visible(
        IEnumerable<string> recents,
        IEnumerable<string> roots,
        Func<string, bool> directoryExists)
    {
        var rootList = roots.ToList();
        return recents
            .Where(r => rootList.Any(root => FolderNames.IsSameOrUnder(r, root)) && directoryExists(r))
            .ToList();
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> recents)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var raw in recents)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var path = TrimSeparators(raw.Trim());
            if (path.Length == 0 || !seen.Add(path))
            {
                continue;
            }

            list.Add(path);
            if (list.Count == Max)
            {
                break;
            }
        }

        return list;
    }

    private static string TrimSeparators(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Keep a drive root like "D:\" intact.
        return trimmed.Length == 0 || trimmed.EndsWith(':') ? path : trimmed;
    }
}
