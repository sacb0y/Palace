using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// T8-01 Library filter strip + status copy (above-grid chips, Match label, Clear).
/// Pure — off WinUI.
/// </summary>
public static class TagFilterStrip
{
    public static string MatchLabel(TagFilterMode mode) =>
        mode switch
        {
            TagFilterMode.Any => "Match any",
            TagFilterMode.None => "Match none",
            _ => "Match all"
        };

    /// <summary>Status joiner between tag names (mockup: landscape AND wip).</summary>
    public static string StatusJoiner(TagFilterMode mode) =>
        mode switch
        {
            TagFilterMode.Any => " OR ",
            TagFilterMode.None => " EXCEPT ",
            _ => " AND "
        };

    public static string StripLead(int matchedCount) =>
        matchedCount == 1 ? "1 file with" : $"{Math.Max(0, matchedCount)} files with";

    /// <summary>
    /// Status bar: "41 of 412 files · landscape AND wip". Empty names → matched-only count.
    /// </summary>
    public static string StatusLine(
        int matchedCount,
        int totalCount,
        IReadOnlyList<string> tagNames,
        TagFilterMode mode)
    {
        var matched = Math.Max(0, matchedCount);
        var total = Math.Max(matched, Math.Max(0, totalCount));
        var noun = matched == 1 && total == 1 ? "file" : "files";
        if (tagNames is null || tagNames.Count == 0)
        {
            return $"{matched} of {total} {noun}";
        }

        var joined = string.Join(StatusJoiner(mode), tagNames.Where(n => !string.IsNullOrWhiteSpace(n)));
        return string.IsNullOrEmpty(joined)
            ? $"{matched} of {total} {noun}"
            : $"{matched} of {total} {noun} · {joined}";
    }
}
