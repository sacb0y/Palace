namespace Palace.Helpers;

public static class TagNameList
{
    public static IReadOnlyList<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (var part in text.Split([',', '\n', '\r'], StringSplitOptions.None))
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
