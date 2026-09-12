namespace Palace.Helpers;

public sealed class TagLetterGroup<T>
{
    public string Letter { get; init; } = "";
    public IReadOnlyList<T> Items { get; init; } = [];
    public string Header => $"{Letter} ({Items.Count})";
}

public static class TagAlphaIndex
{
    public static char LetterOf(string? name)
    {
        var text = (name ?? "").Trim();
        if (text.Length == 0)
        {
            return '#';
        }

        var ch = text[0];
        return char.IsLetter(ch) ? char.ToUpperInvariant(ch) : '#';
    }

    public static IReadOnlyList<TagLetterGroup<T>> GroupByLetter<T>(
        IEnumerable<T> items,
        Func<T, string?> name)
    {
        return items
            .OrderBy(item => name(item) ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => name(item) ?? "", StringComparer.Ordinal)
            .GroupBy(item => LetterOf(name(item)))
            .OrderBy(group => group.Key == '#' ? char.MaxValue : group.Key)
            .Select(group => new TagLetterGroup<T>
            {
                Letter = group.Key.ToString(),
                Items = group.ToList()
            })
            .ToList();
    }
}
