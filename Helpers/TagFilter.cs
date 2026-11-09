using Palace.Models;

namespace Palace.Helpers;

public static class TagFilter
{
    public static HashSet<string> Expand(string tagId, IReadOnlyList<TagMembership> memberships)
    {
        var found = new HashSet<string>(StringComparer.Ordinal) { tagId };
        if (memberships.Count == 0)
        {
            return found;
        }

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

        var stack = new Stack<string>();
        stack.Push(tagId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!children.TryGetValue(current, out var kids))
            {
                continue;
            }

            foreach (var kid in kids)
            {
                if (found.Add(kid))
                {
                    stack.Push(kid);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Library tag-filter expand: resolve <paramref name="tagId"/> through
    /// <see cref="TagAliasResolve.Ideal"/>, then membership-descendants of that ideal.
    /// Optionally unions sibling alias ids (stale AssetTag defense).
    /// Does <b>not</b> add implication closure — intentional vs search/FTS.
    /// </summary>
    public static HashSet<string> Expand(
        string tagId,
        IReadOnlyList<TagMembership> memberships,
        IReadOnlyDictionary<string, string> aliasToIdeal,
        bool includeSiblingAliases = true)
    {
        if (aliasToIdeal is null || aliasToIdeal.Count == 0)
        {
            return Expand(tagId, memberships);
        }

        var idealId = TagAliasResolve.Ideal(tagId, aliasToIdeal);
        var found = Expand(idealId, memberships);
        if (!includeSiblingAliases)
        {
            return found;
        }

        found.Add(tagId);
        foreach (var (aliasId, _) in aliasToIdeal)
        {
            if (string.Equals(
                    TagAliasResolve.Ideal(aliasId, aliasToIdeal),
                    idealId,
                    StringComparison.Ordinal))
            {
                found.Add(aliasId);
            }
        }

        return found;
    }

    public static List<HashSet<string>> ExpandEach(
        IEnumerable<string> tagIds,
        IReadOnlyList<TagMembership> memberships) =>
        tagIds.Select(id => Expand(id, memberships)).ToList();

    /// <inheritdoc cref="Expand(string,IReadOnlyList{TagMembership},IReadOnlyDictionary{string,string},bool)"/>
    public static List<HashSet<string>> ExpandEach(
        IEnumerable<string> tagIds,
        IReadOnlyList<TagMembership> memberships,
        IReadOnlyDictionary<string, string> aliasToIdeal,
        bool includeSiblingAliases = true) =>
        tagIds.Select(id => Expand(id, memberships, aliasToIdeal, includeSiblingAliases)).ToList();

    public static HashSet<string> MatchAssetIds(
        TagFilterMode mode,
        IReadOnlyList<IReadOnlySet<string>> expandedSets,
        IReadOnlyDictionary<string, HashSet<string>> tagsByAsset,
        IReadOnlySet<string>? universe = null)
    {
        var world = universe is null
            ? new HashSet<string>(tagsByAsset.Keys, StringComparer.Ordinal)
            : new HashSet<string>(universe, StringComparer.Ordinal);

        if (expandedSets.Count == 0 || expandedSets.All(set => set.Count == 0))
        {
            return world;
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        if (mode == TagFilterMode.All)
        {
            foreach (var assetId in world)
            {
                if (!tagsByAsset.TryGetValue(assetId, out var tags))
                {
                    continue;
                }

                if (expandedSets.All(set => Hits(tags, set)))
                {
                    result.Add(assetId);
                }
            }

            return result;
        }

        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var set in expandedSets)
        {
            foreach (var id in set)
            {
                union.Add(id);
            }
        }

        foreach (var assetId in world)
        {
            tagsByAsset.TryGetValue(assetId, out var tags);
            var hit = tags is not null && Hits(tags, union);
            if (mode == TagFilterMode.Any && hit)
            {
                result.Add(assetId);
            }
            else if (mode == TagFilterMode.None && !hit)
            {
                result.Add(assetId);
            }
        }

        return result;
    }

    private static bool Hits(HashSet<string> assetTags, IReadOnlySet<string> set)
    {
        foreach (var id in set)
        {
            if (assetTags.Contains(id))
            {
                return true;
            }
        }

        return false;
    }
}
