namespace Palace.Helpers;

/// <summary>
/// Tags board A–Z letter section fold (Expand / Collapse all). Off WinUI for tests.
/// Session-only collapsed keys — new sections default expanded.
/// </summary>
public static class TagLetterFold
{
    public static string AutomationName(string? letter, int chipCount, bool expanded)
    {
        var label = string.IsNullOrEmpty(letter) ? "#" : letter;
        var count = Math.Max(0, chipCount);
        var noun = count == 1 ? "tag" : "tags";
        var state = expanded ? "expanded" : "collapsed";
        return $"{label}, {count} {noun}, {state}";
    }

    public static string CollapseKey(string? boardKey, string? letter) =>
        $"{boardKey ?? ""}\n{letter ?? ""}";

    public static bool IsExpandedDefault(ISet<string> collapsedKeys, string? boardKey, string? letter) =>
        !collapsedKeys.Contains(CollapseKey(boardKey, letter));

    public static void RememberExpanded(ISet<string> collapsedKeys, string? boardKey, string? letter, bool expanded)
    {
        var key = CollapseKey(boardKey, letter);
        if (expanded)
        {
            collapsedKeys.Remove(key);
        }
        else
        {
            collapsedKeys.Add(key);
        }
    }

    /// <summary>Empty when collapsed so chips are not realized in the repeater.</summary>
    public static IEnumerable<T> RealizedChips<T>(bool expanded, IEnumerable<T> chips) =>
        expanded ? chips : Array.Empty<T>();
}
