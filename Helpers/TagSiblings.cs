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
}
