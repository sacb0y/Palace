namespace Palace.Helpers;

/// <summary>
/// Autosuggest ranking for assign / find-or-create. Prefix matches beat substring;
/// already-assigned tags are omitted so Down+Enter never re-picks a chip on the image.
/// </summary>
public static class TagSuggest
{
    public readonly record struct Candidate(string TagId, string Name, string Display);

    /// <summary>
    /// Lower score is better: 1 = name prefix, 2 = name contains, 3 = display contains, 0 = no match.
    /// </summary>
    public static int Score(string name, string display, string query)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0)
        {
            return 0;
        }

        if (name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (name.Contains(q, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return display.Contains(q, StringComparison.OrdinalIgnoreCase) ? 3 : 0;
    }

    public static IReadOnlyList<Candidate> Rank(
        IEnumerable<Candidate> candidates,
        string? query,
        IEnumerable<string>? excludeTagIds = null,
        int take = 40)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0)
        {
            return [];
        }

        var excluded = excludeTagIds is null
            ? null
            : new HashSet<string>(excludeTagIds.Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);

        return candidates
            .Where(c => excluded is null || !excluded.Contains(c.TagId))
            .Select(c => (Item: c, Score: Score(c.Name, c.Display, q)))
            .Where(x => x.Score > 0)
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Item)
            .Take(take)
            .ToList();
    }
}
