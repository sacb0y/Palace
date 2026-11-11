namespace Palace.Helpers;

/// <summary>
/// Library-wide dismissed From-prompt tokens (LocalSettings). Normalized names only —
/// spelling / <c>_</c> / case variants share one entry via <see cref="Data.TagNames.Normalize"/>.
/// </summary>
public static class DismissedPromptTokens
{
    public const string SettingsKey = "DismissedPromptTokens";

    /// <summary>Soft cap so a long session cannot grow LocalSettings without bound.</summary>
    public const int Limit = 500;

    public static IReadOnlyList<string> Remember(IReadOnlyList<string>? current, string? token)
    {
        // FromPromptToken strips weights then normalizes — matches Suggest dismiss keys.
        var key = Data.TagNames.FromPromptToken(token);
        if (key.Length == 0)
        {
            return current ?? [];
        }

        var list = (current ?? [])
            .Where(t => !Data.TagNames.Same(t, key))
            .ToList();
        list.Insert(0, key);
        if (list.Count > Limit)
        {
            list.RemoveRange(Limit, list.Count - Limit);
        }

        return list;
    }

    public static IReadOnlyList<string> RememberMany(
        IReadOnlyList<string>? current,
        IEnumerable<string>? tokens)
    {
        var list = (current ?? []).ToList();
        if (tokens is null)
        {
            return list;
        }

        foreach (var token in tokens)
        {
            list = Remember(list, token).ToList();
        }

        return list;
    }

    public static bool Contains(IReadOnlyList<string>? current, string? token)
    {
        var key = Data.TagNames.FromPromptToken(token);
        if (key.Length == 0 || current is null || current.Count == 0)
        {
            return false;
        }

        return current.Any(t => Data.TagNames.Same(t, key));
    }

    public static IReadOnlyList<string> Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = new List<string>();
        foreach (var part in stored.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var key = Data.TagNames.FromPromptToken(part);
            if (key.Length == 0 || !seen.Add(key))
            {
                continue;
            }

            tokens.Add(key);
        }

        return tokens.Count <= Limit ? tokens : tokens.Take(Limit).ToList();
    }

    public static string Serialize(IReadOnlyList<string> tokens) =>
        string.Join(
            ",",
            (tokens ?? [])
                .Select(Data.TagNames.FromPromptToken)
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Limit));
}
