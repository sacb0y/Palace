namespace Palace.Helpers;

/// <summary>
/// Autosuggest ranking for assign / find-or-create. Prefix matches beat substring;
/// already-assigned tags are omitted so Down+Enter never re-picks a chip on the image.
/// t2-02 row chrome: bold match span, create-last row, alias pill.
/// </summary>
public static class TagSuggest
{
    public readonly record struct Candidate(string TagId, string Name, string Display);

    /// <summary>Matched substring split for bold highlight in the suggestion row.</summary>
    public readonly record struct MatchSpan(string Before, string Match, string After);

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

    /// <summary>
    /// First case-insensitive occurrence of <paramref name="query"/> in <paramref name="name"/>.
    /// When missing, the whole name is <see cref="MatchSpan.Match"/> (no bold split).
    /// </summary>
    public static MatchSpan SplitMatch(string? name, string? query)
    {
        var text = name ?? "";
        var q = (query ?? "").Trim();
        if (q.Length == 0 || text.Length == 0)
        {
            return new MatchSpan("", text, "");
        }

        var idx = text.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return new MatchSpan("", text, "");
        }

        return new MatchSpan(
            text[..idx],
            text.Substring(idx, q.Length),
            text[(idx + q.Length)..]);
    }
}
