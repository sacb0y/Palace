namespace Palace.Helpers;

public sealed class FolderGroup<T>
{
    public string Title { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public IReadOnlyList<string> RelativeSegments { get; init; } = [];
    public IReadOnlyList<T> Items { get; init; } = [];
}

/// <summary>
/// Library mosaic groups for a top-level (source) folder.
/// Titles are <c>Selected / Sub / Sub</c> using the first 1–2 child folders.
/// Path math only — no disk probes.
/// </summary>
public static class FolderGroups
{
    public const int MaxSubFolders = 2;
    public const string TitleSeparator = " / ";

    public static bool ShouldGroup(bool isTagBrowse, bool hasSearch, bool isTopLevelFolder) =>
        !isTagBrowse && !hasSearch && isTopLevelFolder;

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
        var path = (selectedPath ?? "").TrimEnd('\\', '/');
        var take = TakenCount(relativeSegments.Count, maxSubFolders);
        for (var i = 0; i < take; i++)
        {
            path = Path.Combine(path, relativeSegments[i]);
        }

        return path;
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
