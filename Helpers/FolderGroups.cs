namespace Palace.Helpers;

public sealed class FolderGroup<T>
{
    public string Title { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public IReadOnlyList<string> RelativeSegments { get; init; } = [];
    public IReadOnlyList<T> Items { get; init; } = [];
}

/// <summary>
/// Library mosaic groups for a folder browse prefix (source root or nested).
/// Titles are <c>Selected / Sub / Sub</c> using the first 1–2 child folders.
/// Path math only — no disk probes.
/// </summary>
public static class FolderGroups
{
    public const int MaxSubFolders = 2;
    public const string TitleSeparator = " / ";
    public const string MosaicHeaderPrefix = "folder-group:";

    public static string MosaicTag(bool isFolderHeader, string? id, string? folderGroupPath)
    {
        if (!isFolderHeader)
        {
            return id ?? "";
        }

        return string.IsNullOrEmpty(folderGroupPath) ? "" : MosaicHeaderPrefix + folderGroupPath;
    }

    public static string? FolderPathFromMosaicTag(string? key)
    {
        if (string.IsNullOrEmpty(key)
            || !key.StartsWith(MosaicHeaderPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var path = key[MosaicHeaderPrefix.Length..];
        return path.Length == 0 ? null : path;
    }

    public static int NextNonHeaderIndex(IReadOnlyList<bool> isHeader, int from, int step)
    {
        if (step == 0 || isHeader.Count == 0)
        {
            return -1;
        }

        for (var i = from + step; i >= 0 && i < isHeader.Count; i += step)
        {
            if (!isHeader[i])
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Group when browsing a folder prefix. Search, tag browse, and all-library stay flat.
    /// </summary>
    public static bool ShouldGroup(bool isTagBrowse, bool hasSearch, bool hasFolderPath) =>
        !isTagBrowse && !hasSearch && hasFolderPath;

    public static bool IsTopLevelFolder(IEnumerable<string> rootPaths, string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return false;
        }

        return rootPaths.Any(root =>
            string.Equals(root, selectedPath, StringComparison.OrdinalIgnoreCase));
    }

    public static bool NeedsHeaders<T>(IReadOnlyList<FolderGroup<T>> groups) =>
        groups.Count > 1
        || groups.Any(group => group.RelativeSegments.Count > 0);

    public static string[] SplitSegments(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? []
            : path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

    public static IReadOnlyList<string> RelativeSegments(string selectedPath, string? directoryPath)
    {
        var root = SplitSegments(selectedPath);
        var dir = SplitSegments(directoryPath);
        if (root.Length == 0 || dir.Length < root.Length)
        {
            return [];
        }

        for (var i = 0; i < root.Length; i++)
        {
            if (!string.Equals(root[i], dir[i], StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }
        }

        return dir.Length == root.Length
            ? []
            : dir.Skip(root.Length).ToArray();
    }

    public static string Title(
        string selectedName,
        IReadOnlyList<string> relativeSegments,
        int maxSubFolders = MaxSubFolders)
    {
        var parts = new List<string>();
        var name = (selectedName ?? "").Trim();
        if (name.Length > 0)
        {
            parts.Add(name);
        }

        var take = TakenCount(relativeSegments.Count, maxSubFolders);
        for (var i = 0; i < take; i++)
        {
            parts.Add(relativeSegments[i]);
        }

        return string.Join(TitleSeparator, parts);
    }

    public static string CombineUnder(
        string selectedPath,
        IReadOnlyList<string> relativeSegments,
        int maxSubFolders = MaxSubFolders)
    {
        var selected = selectedPath ?? "";
        var take = TakenCount(relativeSegments.Count, maxSubFolders);
        if (take == 0)
        {
            return selected;
        }

        // Path.Combine("D:", "Vacation") is "D:Vacation" (drive-relative). Keep
        // a trailing separator on a Windows drive root so FindNode can match.
        var sep = PreferredSeparator(selected);
        var path = KeepRoot(selected, sep);
        for (var i = 0; i < take; i++)
        {
            if (!EndsWithSeparator(path))
            {
                path += sep;
            }

            path += relativeSegments[i];
        }

        return path;
    }

    public static bool IsWindowsDriveRoot(string? path)
    {
        var spec = (path ?? "").TrimEnd('\\', '/');
        return spec.Length == 2 && char.IsAsciiLetter(spec[0]) && spec[1] == ':';
    }

    private static string KeepRoot(string selected, char sep)
    {
        if (IsWindowsDriveRoot(selected))
        {
            return selected.TrimEnd('\\', '/') + sep;
        }

        return selected.TrimEnd('\\', '/');
    }

    private static bool EndsWithSeparator(string path) =>
        path.Length > 0 && path[^1] is '\\' or '/';

    private static char PreferredSeparator(string path)
    {
        if (path.Contains('\\'))
        {
            return '\\';
        }

        if (path.Contains('/'))
        {
            return '/';
        }

        return IsWindowsDriveRoot(path) ? '\\' : Path.DirectorySeparatorChar;
    }

    public static string DirectoryOf(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return "";
        }

        var trimmed = assetPath.TrimEnd('\\', '/');
        var slash = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        return slash <= 0 ? "" : trimmed[..slash];
    }

    public static IReadOnlyList<FolderGroup<T>> GroupByTopFolders<T>(
        IReadOnlyList<T> items,
        string selectedName,
        string selectedPath,
        Func<T, string> assetPath,
        int maxSubFolders = MaxSubFolders)
    {
        var groups = new Dictionary<string, FolderGroupBuilder<T>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var item in items)
        {
            var relative = RelativeSegments(selectedPath, DirectoryOf(assetPath(item)));
            var taken = relative.Take(TakenCount(relative.Count, maxSubFolders)).ToArray();
            var key = string.Join('\0', taken);
            if (!groups.TryGetValue(key, out var builder))
            {
                builder = new FolderGroupBuilder<T>
                {
                    Title = Title(selectedName, taken, maxSubFolders),
                    RelativePath = string.Join(Path.DirectorySeparatorChar, taken),
                    RelativeSegments = taken
                };
                groups[key] = builder;
                order.Add(key);
            }

            builder.Items.Add(item);
        }

        return order
            .Select(key => groups[key])
            .OrderBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Title, StringComparer.Ordinal)
            .Select(group => new FolderGroup<T>
            {
                Title = group.Title,
                RelativePath = group.RelativePath,
                RelativeSegments = group.RelativeSegments,
                Items = group.Items
            })
            .ToList();
    }

    public static List<T> InterleaveHeaders<T>(
        IReadOnlyList<FolderGroup<T>> groups,
        Func<FolderGroup<T>, T> headerFactory)
    {
        var list = new List<T>();
        foreach (var group in groups)
        {
            list.Add(headerFactory(group));
            list.AddRange(group.Items);
        }

        return list;
    }

    private static int TakenCount(int available, int maxSubFolders) =>
        Math.Min(Math.Max(0, maxSubFolders), available);

    private sealed class FolderGroupBuilder<T>
    {
        public string Title { get; init; } = "";
        public string RelativePath { get; init; } = "";
        public IReadOnlyList<string> RelativeSegments { get; init; } = [];
        public List<T> Items { get; } = [];
    }
}
