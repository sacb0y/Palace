using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Per-tag file counts for the Library ▸ Tags panel (t8-01 rows).
/// Includes assets tagged with a source that <b>implies</b> the row tag
/// (mockup: environment finds everything tagged landscape).
/// Pure — off WinUI.
/// </summary>
public static class TagBrowseCounts
{
    /// <summary>
    /// Build tagId → distinct asset count for each requested id, expanding
    /// implication sources via <see cref="ImpliedNearest.BuildReverse"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ForTags(
        IEnumerable<(string AssetId, string TagId)>? pairs,
        IReadOnlyList<TagImplication>? implications,
        IEnumerable<string>? tagIds)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (tagIds is null)
        {
            return result;
        }

        var byTag = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (pairs is not null)
        {
            foreach (var (assetId, tagId) in pairs)
            {
                if (string.IsNullOrEmpty(assetId) || string.IsNullOrEmpty(tagId))
                {
                    continue;
                }

                if (!byTag.TryGetValue(tagId, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    byTag[tagId] = set;
                }

                set.Add(assetId);
            }
        }

        var reverse = ImpliedNearest.BuildReverse(
            (implications ?? []).Select(i => (i.TagId, i.ImpliedTagId)));

        foreach (var id in tagIds)
        {
            if (string.IsNullOrEmpty(id) || result.ContainsKey(id))
            {
                continue;
            }

            var assets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in CollectSelfAndImplying(id, reverse))
            {
                if (byTag.TryGetValue(source, out var set))
                {
                    assets.UnionWith(set);
                }
            }

            result[id] = assets.Count;
        }

        return result;
    }

    /// <summary>Face text for a row trailing count (empty when 0).</summary>
    public static string Face(int count) =>
        count > 0 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";

    private static IEnumerable<string> CollectSelfAndImplying(
        string tagId,
        IReadOnlyDictionary<string, List<string>> reverse)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { tagId };
        var stack = new Stack<string>();
        stack.Push(tagId);
        yield return tagId;

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!reverse.TryGetValue(current, out var sources))
            {
                continue;
            }

            foreach (var source in sources)
            {
                if (string.IsNullOrEmpty(source) || !seen.Add(source))
                {
                    continue;
                }

                stack.Push(source);
                yield return source;
            }
        }
    }
}
