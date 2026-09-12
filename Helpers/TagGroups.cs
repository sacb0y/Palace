using Palace.Models;

namespace Palace.Helpers;

public static class TagGroups
{
    public static IReadOnlyList<Tag> Root(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships)
    {
        var childIds = memberships.Select(m => m.ChildId).ToHashSet(StringComparer.Ordinal);
        var parentIds = memberships.Select(m => m.ParentId).ToHashSet(StringComparer.Ordinal);
        return tags
            .Where(t => !childIds.Contains(t.Id) && parentIds.Contains(t.Id))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<Tag> Destinations(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships,
        string tagId,
        string? currentRootId,
        bool add)
    {
        var blocked = DescendantsIncludingSelf(memberships, tagId);
        return Root(tags, memberships)
            .Where(group =>
            {
                if (blocked.Contains(group.Id))
                {
                    return false;
                }

                if (add)
                {
                    return TagSiblings.ParentsUnderRoot(memberships, tagId, group.Id).Count == 0;
                }

                return !string.Equals(group.Id, currentRootId, StringComparison.Ordinal);
            })
            .ToList();
    }

    public static HashSet<string> DescendantsIncludingSelf(
        IReadOnlyList<TagMembership> memberships,
        string tagId)
    {
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

        var seen = new HashSet<string>(StringComparer.Ordinal) { tagId };
        var stack = new Stack<string>();
        stack.Push(tagId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!children.TryGetValue(current, out var kids))
            {
                continue;
            }

            foreach (var kid in kids)
            {
                if (seen.Add(kid))
                {
                    stack.Push(kid);
                }
            }
        }

        return seen;
    }
}
