namespace Palace.Helpers;

/// <summary>
/// T2 Add tags box: paste → pending chips (no autocomplete); typed single token → suggestions.
/// Typing a separator commits the current token and keeps the caret for the next name.
/// </summary>
public static class TagAddInput
{
    /// <summary>Ungrouped / new-tag chip dot (Tailwind slate-500).</summary>
    public const string SlateHex = "#64748B";

    public const string Placeholder = "Add tags — type, or paste a list";

    public const string ShortcutHint = "T";

    /// <summary>Separators that force paste/list mode (same set as <see cref="TagNameList"/>).</summary>
    private static readonly char[] Separators = [',', ';', '\n', '\r', '\uFF0C', '\u3001', '\uFF1B'];

    public static bool ContainsSeparator(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.IndexOfAny(Separators) >= 0;
    }

    /// <summary>
    /// True when the box should open the suggestion list: a single token with no separator.
    /// Paste lists (any separator) never open autocomplete.
    /// </summary>
    public static bool OpensSuggestions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || ContainsSeparator(text))
        {
            return false;
        }

        return TagNameList.Split(text).Count <= 1;
    }

    /// <summary>
    /// When the text contains a separator (typed comma or paste list), commit every complete name.
    /// Trailing separator (user typed <c>,</c>) or a multi-name paste both clear the box
    /// (<c>remainder</c> empty) so pending chips own the list.
    /// </summary>
    public static (IReadOnlyList<string> Committed, string Remainder) CommitOnSeparator(
        string? previous,
        string? current)
    {
        _ = previous;
        var text = current ?? "";
        if (!ContainsSeparator(text))
        {
            return ([], text);
        }

        var names = TagNameList.Split(text);
        return (names, "");
    }

    public static string PasteSummary(int total, int newCount, int aliasCount)
    {
        var parts = new List<string> { $"Pasted {total} tag{(total == 1 ? "" : "s")}" };
        if (newCount > 0)
        {
            parts.Add($"{newCount} new");
        }

        if (aliasCount > 0)
        {
            parts.Add($"{aliasCount} alias{(aliasCount == 1 ? "" : "es")}");
        }

        return string.Join(" · ", parts);
    }

    public static string ApplyButtonLabel(int tagCount, int fileCount)
    {
        if (tagCount <= 0)
        {
            return "Apply tags";
        }

        if (fileCount <= 1)
        {
            return tagCount == 1 ? "Apply 1 tag" : $"Apply {tagCount} tags";
        }

        return $"Apply {tagCount} tags to {fileCount} files";
    }

    public static string AliasFace(string aliasName, string idealName) =>
        $"{aliasName} › {idealName}";

    public static string DotColor(string? effectiveColor, bool isNew) =>
        !string.IsNullOrWhiteSpace(effectiveColor)
            ? effectiveColor!
            : SlateHex;
}
