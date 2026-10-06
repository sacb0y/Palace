namespace Palace.Helpers;

/// <summary>
/// Library Tags-tab browse list: a wrapping chip mosaic, not a tall same-width column.
/// Path math only — off WinUI.
/// </summary>
public static class TagBrowseMosaic
{
    public static IReadOnlyList<T> Flatten<T>(
        IEnumerable<T> roots,
        Func<T, string?> tagId,
        Func<T, IEnumerable<T>> children)
    {
        var list = new List<T>();
        Walk(roots, tagId, children, list);
        return list;
    }

    private static void Walk<T>(
        IEnumerable<T> nodes,
        Func<T, string?> tagId,
        Func<T, IEnumerable<T>> children,
        List<T> list)
    {
        foreach (var node in nodes)
        {
            if (!string.IsNullOrEmpty(tagId(node)))
            {
                list.Add(node);
            }

            Walk(children(node), tagId, children, list);
        }
    }
}
