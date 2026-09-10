using System.Collections.ObjectModel;
using Palace.Models;

namespace Palace.Helpers;

public static class TagTreeBuilder
{
    public static void Replace(
        ObservableCollection<TagTreeNode> target,
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships)
    {
        target.Clear();
        foreach (var node in Build(tags, memberships))
        {
            target.Add(node);
        }
    }

    public static IReadOnlyList<TagTreeNode> Build(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships)
    {
        var byId = tags.ToDictionary(t => t.Id);
        var children = new Dictionary<string, List<Tag>>(StringComparer.Ordinal);
        var childIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in memberships)
        {
            if (!byId.TryGetValue(edge.ChildId, out var child))
            {
                continue;
            }

            childIds.Add(edge.ChildId);
            if (!children.TryGetValue(edge.ParentId, out var list))
            {
                list = [];
                children[edge.ParentId] = list;
            }

            list.Add(child);
        }

        var roots = new List<TagTreeNode>();
        foreach (var root in tags.Where(t => !childIds.Contains(t.Id) && children.ContainsKey(t.Id))
                     .OrderByDescending(t => t.Priority)
                     .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(BuildNode(root, children, []));
        }

        var ungrouped = tags.Where(t => !childIds.Contains(t.Id) && !children.ContainsKey(t.Id))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ungrouped.Count > 0)
        {
            var bucket = new TagTreeNode { Name = "Ungrouped", IsUngroupedBucket = true };
            foreach (var tag in ungrouped)
            {
                bucket.Children.Add(new TagTreeNode
                {
                    TagId = tag.Id,
                    Name = tag.Name,
                    IsStarred = tag.IsStarred
                });
            }

            roots.Add(bucket);
        }

        return roots;
    }

    public static TagTreeNode? Find(IEnumerable<TagTreeNode> nodes, string tagId)
    {
        foreach (var node in nodes)
        {
            if (node.TagId == tagId)
            {
                return node;
            }

            var child = Find(node.Children, tagId);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static TagTreeNode BuildNode(Tag tag, Dictionary<string, List<Tag>> children, HashSet<string> trail)
    {
        var node = new TagTreeNode { TagId = tag.Id, Name = tag.Name, IsStarred = tag.IsStarred };
        if (!trail.Add(tag.Id))
        {
            return node;
        }

        if (children.TryGetValue(tag.Id, out var kids))
        {
            foreach (var kid in kids.OrderByDescending(t => t.Priority).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                node.Children.Add(BuildNode(kid, children, [.. trail]));
            }
        }

        return node;
    }
}
