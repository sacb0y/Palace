namespace Palace.Helpers;

public static class TagBoardExpand
{
    public readonly record struct Row(string Key, string? GroupId, IReadOnlyList<string> ChipIds);

    public static string RevealKey(string currentKey, string selectedTagId, IReadOnlyList<Row> rows)
    {
        var current = rows.FirstOrDefault(row => string.Equals(row.Key, currentKey, StringComparison.Ordinal));
        if (current.Key is not null && RowShows(current, selectedTagId))
        {
            return currentKey;
        }

        var named = rows.FirstOrDefault(row =>
            !string.IsNullOrEmpty(row.GroupId) &&
            row.ChipIds.Contains(selectedTagId, StringComparer.Ordinal));
        if (named.Key is not null)
        {
            return named.Key;
        }

        var ungrouped = rows.FirstOrDefault(row =>
            string.Equals(row.Key, "ungrouped", StringComparison.Ordinal) &&
            row.ChipIds.Contains(selectedTagId, StringComparer.Ordinal));
        if (ungrouped.Key is not null)
        {
            return ungrouped.Key;
        }

        return rows.Any(row => string.Equals(row.Key, "all", StringComparison.Ordinal))
            ? "all"
            : currentKey;
    }

    private static bool RowShows(Row row, string selectedTagId) =>
        string.Equals(row.GroupId, selectedTagId, StringComparison.Ordinal) ||
        row.ChipIds.Contains(selectedTagId, StringComparer.Ordinal);
}
