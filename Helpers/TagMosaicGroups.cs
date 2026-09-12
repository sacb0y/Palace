using Palace.Models;

namespace Palace.Helpers;

public sealed class TagMosaicBucket<T>
{
    public string Header { get; init; } = "";
    public IReadOnlyList<Tag> Path { get; init; } = [];
    public IReadOnlyList<T> Items { get; init; } = [];
}

public static class TagMosaicGroups
{
    public static IReadOnlyList<Tag> PathFor(
        string selectedId,
        IReadOnlyCollection<string> assignedTagIds,
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships)
    {
        var byId = tags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        if (!byId.TryGetValue(selectedId, out var selected))
        {
            return [];
        }

        var under = TagGroups.DescendantsIncludingSelf(memberships, selectedId);
        var assigned = assignedTagIds
            .Where(under.Contains)
            .Select(id => byId.GetValueOrDefault(id))
            .OfType<Tag>()
            .ToList();
        var candidates = assigned.Where(t => t.Id != selectedId).ToList();
        if (candidates.Count == 0)
        {
            return [selected];
        }

        var depth = DepthFrom(selectedId, memberships);
        var leaf = candidates
            .OrderByDescending(t => depth.GetValueOrDefault(t.Id))
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .First();
        return WalkPath(selectedId, leaf.Id, memberships, byId, under, depth);
    }

    public static string LabelFor(
        string selectedId,
        IReadOnlyCollection<string> assignedTagIds,
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships) =>
        string.Join(" / ", PathFor(selectedId, assignedTagIds, tags, memberships).Select(t => t.Name));

    public static IReadOnlyList<TagMosaicBucket<T>> Group<T>(
        string selectedId,
        IReadOnlyList<T> items,
        Func<T, string> assetId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> assignedByAsset,
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships)
    {
        var rows = new List<(IReadOnlyList<Tag> Path, string Header, T Item)>(items.Count);
        foreach (var item in items)
        {
            var id = assetId(item);
            assignedByAsset.TryGetValue(id, out var assigned);
            var path = PathFor(selectedId, assigned ?? [], tags, memberships);
            if (path.Count == 0)
            {
                continue;
            }

            rows.Add((path, string.Join(" / ", path.Select(t => t.Name)), item));
        }

        return rows
            .GroupBy(row => row.Header, StringComparer.Ordinal)
            .Select(group =>
            {
                var path = group.First().Path;
                return new TagMosaicBucket<T>
                {
                    Header = group.Key,
                    Path = path,
                    Items = group.Select(row => row.Item).ToList()
                };
            })
            .OrderBy(bucket => bucket.Path, PathComparer.Instance)
            .ToList();
    }

    public static int ComparePaths(IReadOnlyList<Tag> left, IReadOnlyList<Tag> right) =>
        PathComparer.Instance.Compare(left, right);

    private static IReadOnlyList<Tag> WalkPath(
        string rootId,
        string leafId,
        IReadOnlyList<TagMembership> memberships,
        IReadOnlyDictionary<string, Tag> byId,
        HashSet<string> underRoot,
        IReadOnlyDictionary<string, int> depth)
    {
        var chain = new List<Tag>();
        var current = leafId;
        for (var i = 0; i < 64 && current is not null; i++)
        {
            if (!byId.TryGetValue(current, out var tag))
            {
                break;
            }

            chain.Add(tag);
            if (current == rootId)
            {
                break;
            }

            var next = memberships
                .Where(m => m.ChildId == current && underRoot.Contains(m.ParentId) && byId.ContainsKey(m.ParentId))
                .Select(m => byId[m.ParentId])
                .OrderByDescending(t => depth.GetValueOrDefault(t.Id))
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            current = next?.Id;
        }

        chain.Reverse();
        if (chain.Count == 0 || chain[0].Id != rootId)
        {
            return byId.TryGetValue(rootId, out var root) ? [root] : [];
        }

        return chain;
    }

    private static Dictionary<string, int> DepthFrom(string rootId, IReadOnlyList<TagMembership> memberships)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in memberships)
        {
            if (!children.TryGetValue(edge.ParentId, out var list))
            {
                list = [];
                children[edge.ParentId] = list;
            }

            list.Add(edge.ChildId);
        }

        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new HashSet<string>(StringComparer.Ordinal);
        WalkMaxDepth(rootId, 0, children, depth, stack);
        return depth;
    }

    private static void WalkMaxDepth(
        string current,
        int currentDepth,
        Dictionary<string, List<string>> children,
        Dictionary<string, int> depth,
        HashSet<string> stack)
    {
        if (!stack.Add(current))
        {
            return;
        }

        if (depth.TryGetValue(current, out var existing) && currentDepth <= existing)
        {
            stack.Remove(current);
            return;
        }

        depth[current] = currentDepth;
        if (children.TryGetValue(current, out var kids))
        {
            foreach (var kid in kids)
            {
                WalkMaxDepth(kid, currentDepth + 1, children, depth, stack);
            }
        }

        stack.Remove(current);
    }

    private sealed class PathComparer : IComparer<IReadOnlyList<Tag>>
    {
        public static readonly PathComparer Instance = new();

        public int Compare(IReadOnlyList<Tag>? left, IReadOnlyList<Tag>? right)
        {
            left ??= [];
            right ??= [];
            var n = Math.Min(left.Count, right.Count);
            for (var i = 0; i < n; i++)
            {
                var byPriority = right[i].Priority.CompareTo(left[i].Priority);
                if (byPriority != 0)
                {
                    return byPriority;
                }

                var byName = string.Compare(left[i].Name, right[i].Name, StringComparison.OrdinalIgnoreCase);
                if (byName != 0)
                {
                    return byName;
                }
            }

            return left.Count.CompareTo(right.Count);
        }
    }
}
