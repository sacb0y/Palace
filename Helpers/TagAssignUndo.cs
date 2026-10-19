namespace Palace.Helpers;

/// <summary>
/// Implied tags a single assignment newly wrote. Undo removes these rows along
/// with the assigned tag. Tags that were already on the asset stay.
/// </summary>
public static class TagAssignUndo
{
    public static IReadOnlyList<string> ImpliedAdded(
        IEnumerable<string> presentBefore,
        IEnumerable<string> presentAfter,
        string assignedTagId)
    {
        var before = presentBefore as IReadOnlySet<string>
            ?? presentBefore.ToHashSet(StringComparer.Ordinal);
        var added = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in presentAfter)
        {
            if (string.IsNullOrEmpty(id)
                || string.Equals(id, assignedTagId, StringComparison.Ordinal)
                || before.Contains(id)
                || !seen.Add(id))
            {
                continue;
            }

            added.Add(id);
        }

        return added;
    }

    public static string Encode(IReadOnlyList<string> impliedIds) =>
        string.Join('\n', impliedIds);

    public static IReadOnlyList<string> Decode(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
        {
            return [];
        }

        return encoded.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
