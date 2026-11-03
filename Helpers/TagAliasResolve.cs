namespace Palace.Helpers;

/// <summary>
/// Hydrus-style tag siblings (aliases): each alias maps n→1 to an ideal tag.
/// Distinct from <see cref="TagSiblings"/> (group children) and <see cref="TagMembership"/> (groups).
/// Resolve alias→ideal transitively; callers apply this before implications.
/// </summary>
public static class TagAliasResolve
{
    /// <summary>
    /// Follow <paramref name="aliasToIdeal"/> until a tag that is not an alias.
    /// Stops before looping if the map contains a cycle.
    /// </summary>
    public static string Ideal(string tagId, IReadOnlyDictionary<string, string> aliasToIdeal)
    {
        if (string.IsNullOrEmpty(tagId) || aliasToIdeal.Count == 0)
        {
            return tagId;
        }

        var current = tagId;
        var seen = new HashSet<string>(StringComparer.Ordinal) { tagId };
        while (aliasToIdeal.TryGetValue(current, out var ideal))
        {
            if (!seen.Add(ideal))
            {
                return current;
            }

            current = ideal;
        }

        return current;
    }

    /// <summary>
    /// True when <paramref name="aliasId"/> may map to <paramref name="idealId"/>:
    /// not self, n→1 (alias not already bound to a different ideal), and no cycle.
    /// </summary>
    public static bool CanAdd(
        string aliasId,
        string idealId,
        IReadOnlyDictionary<string, string> aliasToIdeal)
    {
        if (string.IsNullOrEmpty(aliasId)
            || string.IsNullOrEmpty(idealId)
            || string.Equals(aliasId, idealId, StringComparison.Ordinal))
        {
            return false;
        }

        if (aliasToIdeal.TryGetValue(aliasId, out var existing))
        {
            return string.Equals(existing, idealId, StringComparison.Ordinal);
        }

        return !WouldCycle(aliasId, idealId, aliasToIdeal);
    }

    /// <summary>
    /// True when adding alias→ideal would let a walk from <paramref name="idealId"/>
    /// reach <paramref name="aliasId"/> (including through existing edges).
    /// </summary>
    public static bool WouldCycle(
        string aliasId,
        string idealId,
        IReadOnlyDictionary<string, string> aliasToIdeal)
    {
        if (string.Equals(aliasId, idealId, StringComparison.Ordinal))
        {
            return true;
        }

        var current = idealId;
        var seen = new HashSet<string>(StringComparer.Ordinal) { idealId };
        while (aliasToIdeal.TryGetValue(current, out var next))
        {
            if (string.Equals(next, aliasId, StringComparison.Ordinal))
            {
                return true;
            }

            if (!seen.Add(next))
            {
                return false;
            }

            current = next;
        }

        return false;
    }
}
