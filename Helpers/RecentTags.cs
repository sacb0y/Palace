namespace Palace.Helpers;

public static class RecentTags
{
    public const int Limit = 20;
    public const string SettingsKey = "RecentTagIds";

    public static IReadOnlyList<string> Remember(IReadOnlyList<string>? current, string tagId)
    {
        if (string.IsNullOrWhiteSpace(tagId))
        {
            return current ?? [];
        }

        var list = (current ?? [])
            .Where(id => !string.Equals(id, tagId, StringComparison.Ordinal))
            .ToList();
        list.Insert(0, tagId);
        if (list.Count > Limit)
        {
            list.RemoveRange(Limit, list.Count - Limit);
        }

        return list;
    }

    public static IReadOnlyList<string> Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>();
        foreach (var part in stored.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var id = part.Trim();
            if (id.Length == 0 || !seen.Add(id))
            {
                continue;
            }

            ids.Add(id);
        }

        return ids.Count <= Limit ? ids : ids.Take(Limit).ToList();
    }

    public static string Serialize(IReadOnlyList<string> ids) =>
        string.Join(",", (ids ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Take(Limit));
}
