using Palace.Data;
using Palace.Models;

namespace Palace.Helpers;

public enum TagAliasSort
{
    /// <summary>Group rows under the tag they point to (al-01 default).</summary>
    Tag,
    Alias
}

/// <summary>Rows, filter, sort, and header copy for the Tags ▸ Aliases view. Pure; off WinUI.</summary>
public static class TagAliasList
{
    /// <summary>Alias rows (alias › ideal), filtered by alias or tag name (spelling folds apply) then sorted.</summary>
    public static IReadOnlyList<TagAliasRow> Build(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagAlias> aliases,
        IReadOnlyDictionary<string, int>? fileCounts = null,
        IReadOnlyDictionary<string, string?>? colors = null,
        string? query = null,
        TagAliasSort sort = TagAliasSort.Tag)
    {
        var byId = tags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var rows = new List<TagAliasRow>(aliases.Count);
        foreach (var edge in aliases)
        {
            if (!byId.TryGetValue(edge.AliasTagId, out var alias) || !byId.TryGetValue(edge.IdealTagId, out var ideal))
            {
                continue;
            }

            string? color = colors is not null && colors.TryGetValue(ideal.Id, out var mapped)
                ? mapped
                : string.IsNullOrWhiteSpace(ideal.Color) ? null : ideal.Color;
            rows.Add(new TagAliasRow
            {
                AliasTagId = alias.Id,
                AliasName = alias.Name,
                IdealTagId = ideal.Id,
                IdealName = ideal.Name,
                IdealColor = color,
                FileCount = fileCounts is not null && fileCounts.TryGetValue(ideal.Id, out var count) ? count : 0
            });
        }

        var q = TagNames.Normalize(query);
        IEnumerable<TagAliasRow> kept = q.Length == 0
            ? rows
            : rows.Where(r =>
                TagNames.Normalize(r.AliasName).Contains(q, StringComparison.OrdinalIgnoreCase)
                || TagNames.Normalize(r.IdealName).Contains(q, StringComparison.OrdinalIgnoreCase));

        var ordered = sort switch
        {
            TagAliasSort.Tag => kept
                .OrderBy(r => r.IdealName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.AliasName, StringComparer.OrdinalIgnoreCase),
            TagAliasSort.Alias => kept
                .OrderBy(r => r.AliasName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.IdealName, StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null)
        };
        return ordered.ToList();
    }

    public static IReadOnlyList<TagAliasRow> ForIdeal(IReadOnlyList<TagAliasRow> rows, string idealTagId) =>
        rows.Where(r => string.Equals(r.IdealTagId, idealTagId, StringComparison.Ordinal))
            .OrderBy(r => r.AliasName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>"14 aliases · 11 tags", or "1 of 14 match" while searching.</summary>
    public static string Caption(int shown, int total, int tagCount, bool filtering) =>
        filtering
            ? $"{shown} of {total} match"
            : $"{StatusLine(total)} · {tagCount} tag{(tagCount == 1 ? "" : "s")}";

    public static string StatusLine(int count) =>
        count == 1 ? "1 alias" : $"{count} aliases";
}
