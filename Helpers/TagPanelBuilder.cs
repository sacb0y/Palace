using Palace.Models;

namespace Palace.Helpers;

public sealed class TagPanelChip
{
    public string TagId { get; init; } = "";
    public string Name { get; init; } = "";
    public string? EffectiveColor { get; init; }
    public bool IsStarred { get; init; }
}

public sealed class TagPanelGroup
{
    public string? GroupId { get; init; }
    public string Name { get; init; } = "";
    public string? Color { get; init; }
    public bool IsUngrouped { get; init; }
    public IReadOnlyList<TagPanelChip> Chips { get; init; } = [];
}

public sealed class TagPanelModel
{
    public IReadOnlyList<TagPanelChip> Starred { get; init; } = [];
    public IReadOnlyList<TagPanelChip> Recent { get; init; } = [];
    public IReadOnlyList<TagPanelChip> Recommended { get; init; } = [];
    public IReadOnlyList<TagPanelGroup> Groups { get; init; } = [];
}

public static class TagPanelBuilder
{
    public static TagPanelModel Build(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships,
        IReadOnlyList<string>? recentIds = null,
        IReadOnlyList<string>? recommendedIds = null,
        string? query = null,
        IReadOnlyDictionary<string, string?>? colors = null,
        TagScope scope = TagScope.All)
    {
        var byId = tags.ToDictionary(t => t.Id, StringComparer.Ordinal);
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

        TagPanelChip Chip(Tag tag) => new()
        {
            TagId = tag.Id,
            Name = tag.Name,
            EffectiveColor = colors is not null && colors.TryGetValue(tag.Id, out var mapped)
                ? mapped
                : string.IsNullOrWhiteSpace(tag.Color) ? null : tag.Color,
            IsStarred = tag.IsStarred
        };

        bool InScope(Tag tag) =>
            scope switch
            {
                TagScope.Starred => tag.IsStarred,
                TagScope.Ungrouped => !childIds.Contains(tag.Id) && !children.ContainsKey(tag.Id),
                _ => true
            };

        var q = (query ?? "").Trim();
        bool NameMatches(string name) =>
            q.Length == 0 || name.Contains(q, StringComparison.OrdinalIgnoreCase);

        var starred = tags
            .Where(t => t.IsStarred && InScope(t) && NameMatches(t.Name))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(Chip)
            .ToList();

        var recent = new List<TagPanelChip>();
        foreach (var id in recentIds ?? [])
        {
            if (byId.TryGetValue(id, out var tag) && InScope(tag) && NameMatches(tag.Name))
            {
                recent.Add(Chip(tag));
            }
        }

        var recommended = new List<TagPanelChip>();
        var seenRecommended = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in recommendedIds ?? [])
        {
            if (!seenRecommended.Add(id))
            {
                continue;
            }

            if (byId.TryGetValue(id, out var tag) && InScope(tag) && NameMatches(tag.Name))
            {
                recommended.Add(Chip(tag));
            }
        }

        var groups = new List<TagPanelGroup>();
        if (scope != TagScope.Starred)
        {
            foreach (var root in tags
                         .Where(t => !childIds.Contains(t.Id) && children.ContainsKey(t.Id))
                         .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(t => t.Name, StringComparer.Ordinal))
            {
                if (scope == TagScope.Ungrouped)
                {
                    continue;
                }

                var descendants = FlattenDescendants(root.Id, children)
                    .Where(InScope)
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Name, StringComparer.Ordinal)
                    .ToList();
                var groupMatches = NameMatches(root.Name);
                var chips = descendants
                    .Where(t => groupMatches || NameMatches(t.Name))
                    .Select(Chip)
                    .ToList();
                if (!groupMatches && chips.Count == 0 && q.Length > 0)
                {
                    continue;
                }

                if (q.Length > 0 && !groupMatches && chips.Count == 0)
                {
                    continue;
                }

                groups.Add(new TagPanelGroup
                {
                    GroupId = root.Id,
                    Name = root.Name,
                    Color = colors is not null && colors.TryGetValue(root.Id, out var groupColor)
                        ? groupColor
                        : string.IsNullOrWhiteSpace(root.Color) ? null : root.Color,
                    Chips = chips
                });
            }
        }

        if (scope != TagScope.Starred)
        {
            var ungrouped = tags
                .Where(t => !childIds.Contains(t.Id) && !children.ContainsKey(t.Id) && InScope(t))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var ungroupedGroupMatches = NameMatches("Ungrouped");
            var ungroupedChips = ungrouped
                .Where(t => ungroupedGroupMatches || NameMatches(t.Name))
                .Select(Chip)
                .ToList();
            if (ungroupedChips.Count > 0 || (q.Length == 0 && scope == TagScope.Ungrouped))
            {
                groups.Add(new TagPanelGroup
                {
                    Name = "Ungrouped",
                    IsUngrouped = true,
                    Chips = ungroupedChips
                });
            }
        }

        return new TagPanelModel
        {
            Starred = starred,
            Recent = recent,
            Recommended = recommended,
            Groups = groups
        };
    }

    private static IEnumerable<Tag> FlattenDescendants(string rootId, Dictionary<string, List<Tag>> children)
    {
        if (!children.TryGetValue(rootId, out var kids))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<Tag>(kids);
        while (stack.Count > 0)
        {
            var tag = stack.Pop();
            if (!seen.Add(tag.Id))
            {
                continue;
            }

            yield return tag;
            if (!children.TryGetValue(tag.Id, out var nested))
            {
                continue;
            }

            foreach (var kid in nested)
            {
                stack.Push(kid);
            }
        }
    }
}
