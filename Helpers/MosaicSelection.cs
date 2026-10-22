namespace Palace.Helpers;

/// <summary>
/// Mosaic selection helpers that stay off WinUI so collapse / republish can
/// keep selection by asset id (collapse is view-only).
/// </summary>
public static class MosaicSelection
{
    /// <summary>
    /// Resolve selected tiles from the full ordered mosaic by id. Headers and
    /// empty ids are skipped. Order follows <paramref name="ordered"/>.
    /// </summary>
    public static List<T> ResolveByIds<T>(
        IReadOnlyList<T> ordered,
        IEnumerable<string> ids,
        Func<T, bool> isHeader,
        Func<T, string?> idOf)
    {
        var want = ids as HashSet<string>
            ?? new HashSet<string>(
                ids.Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);
        if (want.Count == 0 || ordered.Count == 0)
        {
            return [];
        }

        var list = new List<T>(want.Count);
        foreach (var item in ordered)
        {
            if (isHeader(item))
            {
                continue;
            }

            var id = idOf(item);
            if (id is not null && want.Contains(id))
            {
                list.Add(item);
            }
        }

        return list;
    }

    /// <summary>
    /// ItemsView only reports displayed tiles. Merge those with previously
    /// selected tiles that are still in the ordered mosaic but not displayed
    /// (collapsed sections) so a rebuild / SelectionChanged wipe cannot drop them.
    /// </summary>
    public static List<T> MergeVisibleWithHiddenSelected<T>(
        IReadOnlyList<T> visibleSelected,
        IReadOnlyList<T> previousSelected,
        IReadOnlySet<T> displayed,
        Func<T, bool> isHeader)
        where T : class
    {
        var merged = new List<T>(visibleSelected.Count + 4);
        var seen = new HashSet<T>();
        foreach (var item in visibleSelected)
        {
            if (isHeader(item) || !seen.Add(item))
            {
                continue;
            }

            merged.Add(item);
        }

        foreach (var item in previousSelected)
        {
            if (isHeader(item) || displayed.Contains(item) || !seen.Add(item))
            {
                continue;
            }

            merged.Add(item);
        }

        return merged;
    }
}
