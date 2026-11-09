namespace Palace.Helpers;

/// <summary>
/// T8 implied-chip tooltips: name only the nearest applied tag that implies a tag.
/// Pure graph walk — off WinUI.
/// </summary>
public static class ImpliedNearest
{
    /// <summary>
    /// Build impliedTagId → list of tagIds that imply it. Call once per preview
    /// and reuse across chips.
    /// </summary>
    public static Dictionary<string, List<string>> BuildReverse(
        IEnumerable<(string TagId, string ImpliedTagId)>? edges)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (edges is null)
        {
            return map;
        }

        foreach (var (tagId, impliedTagId) in edges)
        {
            if (string.IsNullOrEmpty(tagId) || string.IsNullOrEmpty(impliedTagId))
            {
                continue;
            }

            if (!map.TryGetValue(impliedTagId, out var list))
            {
                list = [];
                map[impliedTagId] = list;
            }

            list.Add(tagId);
        }

        return map;
    }

    /// <summary>
    /// Shortest reverse path along implication edges from <paramref name="impliedTagId"/>
    /// to any id in <paramref name="appliedTagIds"/>. Returns that applied id, or null.
    /// Same-distance ties break with <see cref="StringComparer.Ordinal"/>.
    /// </summary>
    public static string? NearestSourceId(
        string? impliedTagId,
        IReadOnlyCollection<string>? appliedTagIds,
        IReadOnlyDictionary<string, List<string>> reverse)
    {
        if (string.IsNullOrWhiteSpace(impliedTagId) || appliedTagIds is null || appliedTagIds.Count == 0)
        {
            return null;
        }

        var applied = appliedTagIds as ISet<string>
            ?? appliedTagIds.ToHashSet(StringComparer.Ordinal);
        if (applied.Contains(impliedTagId))
        {
            // Already applied — not an implied-only chip.
            return null;
        }

        var queue = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { impliedTagId };
        queue.Enqueue(impliedTagId);
        while (queue.Count > 0)
        {
            var levelSize = queue.Count;
            List<string>? hits = null;
            for (var i = 0; i < levelSize; i++)
            {
                var current = queue.Dequeue();
                if (!reverse.TryGetValue(current, out var sources))
                {
                    continue;
                }

                // Stable neighbor walk — Ordinal — so same-distance ties are deterministic.
                IEnumerable<string> ordered = sources.Count <= 1
                    ? sources
                    : sources.OrderBy(s => s, StringComparer.Ordinal);
                foreach (var source in ordered)
                {
                    if (!seen.Add(source))
                    {
                        continue;
                    }

                    if (applied.Contains(source))
                    {
                        hits ??= [];
                        hits.Add(source);
                    }
                    else
                    {
                        queue.Enqueue(source);
                    }
                }
            }

            if (hits is { Count: > 0 })
            {
                hits.Sort(StringComparer.Ordinal);
                return hits[0];
            }
        }

        return null;
    }

    public static string? NearestSourceId(
        string? impliedTagId,
        IReadOnlyCollection<string>? appliedTagIds,
        IEnumerable<(string TagId, string ImpliedTagId)>? edges) =>
        NearestSourceId(impliedTagId, appliedTagIds, BuildReverse(edges));

    /// <summary>
    /// True when <paramref name="appliedTagId"/> is also reached by implication
    /// from another applied tag (applied + also implied).
    /// </summary>
    public static bool IsAlsoImplied(
        string? appliedTagId,
        IReadOnlyCollection<string>? appliedTagIds,
        IReadOnlyDictionary<string, List<string>> reverse)
    {
        if (string.IsNullOrWhiteSpace(appliedTagId) || appliedTagIds is null || appliedTagIds.Count < 2)
        {
            return false;
        }

        var others = appliedTagIds
            .Where(id => !string.Equals(id, appliedTagId, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        if (others.Count == 0)
        {
            return false;
        }

        return NearestSourceId(appliedTagId, others, reverse) is not null;
    }

    public static bool IsAlsoImplied(
        string? appliedTagId,
        IReadOnlyCollection<string>? appliedTagIds,
        IEnumerable<(string TagId, string ImpliedTagId)>? edges) =>
        IsAlsoImplied(appliedTagId, appliedTagIds, BuildReverse(edges));

    public static string ImpliedTooltip(string? nearestSourceName) =>
        string.IsNullOrWhiteSpace(nearestSourceName)
            ? "Implied by tag rules"
            : $"Implied by {nearestSourceName.Trim()}";

    public static string AlsoImpliedTooltip(string? nearestSourceName) =>
        string.IsNullOrWhiteSpace(nearestSourceName)
            ? "Applied · also implied by tag rules"
            : $"Applied · also implied by {nearestSourceName.Trim()}";

    public static string AliasAlsoLine(IEnumerable<string>? aliasNames)
    {
        var names = (aliasNames ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count == 0 ? "" : $"also: {string.Join(", ", names)}";
    }
}
