namespace Palace.Helpers;

public static class TagNameList
{
    // ASCII comma/semicolon + newlines; full-width ， 、 and full-width ；.
    private static readonly char[] Separators = [',', ';', '\n', '\r', '\uFF0C', '\u3001', '\uFF1B'];

    public static IReadOnlyList<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (var part in text.Split(Separators, StringSplitOptions.None))
        {
            var name = part.Trim();
            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            names.Add(name);
        }

        return names;
    }
}
