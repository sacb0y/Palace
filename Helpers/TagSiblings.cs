using Palace.Models;

namespace Palace.Helpers;

public static class TagSiblings
{
    public static IReadOnlyList<Tag> Of(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships,
        string? parentId)
    {
        if (parentId is not null)
        {
            var siblingIds = memberships
                .Where(m => m.ParentId == parentId)
                .Select(m => m.ChildId)
                .ToHashSet(StringComparer.Ordinal);
            return tags.Where(t => siblingIds.Contains(t.Id)).ToList();
        }

        var childIds = memberships.Select(m => m.ChildId).ToHashSet(StringComparer.Ordinal);
        return tags.Where(t => !childIds.Contains(t.Id)).ToList();
    }

    public static IReadOnlyList<string> ParentsUnderRoot(
        IReadOnlyList<TagMembership> memberships,
        string childId,
        string rootId)
    {
        var underRoot = new HashSet<string>(StringComparer.Ordinal) { rootId };
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in memberships)
        {
            if (!children.TryGetValue(edge.ParentId, out var list))
            {
                list = [];
                children[edge.ParentId] = list;
            }

            list.Add(edge.ChildId);
        }

        var stack = new Stack<string>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!children.TryGetValue(current, out var kids))
            {
                continue;
            }

            foreach (var kid in kids)
            {
                if (underRoot.Add(kid))
                {
                    stack.Push(kid);
                }
            }
        }

        return memberships
            .Where(m => m.ChildId == childId && underRoot.Contains(m.ParentId))
            .Select(m => m.ParentId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
