using Palace.Models;

namespace Palace.Helpers;

public sealed class TagPanelChip
{
    public string TagId { get; init; } = "";
    public string Name { get; init; } = "";
    public string? EffectiveColor { get; init; }
    public bool IsStarred { get; init; }
    public string? ParentId { get; init; }
}

public sealed class TagPanelGroup
{
    public string? GroupId { get; init; }
    public string Name { get; init; } = "";
    public string? Color { get; init; }
    public bool IsUngrouped { get; init; }
    public TagScope? ScopeKind { get; init; }
    public string? AutomationIdOverride { get; init; }
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

        TagPanelChip Chip(Tag tag, string? parentId = null) => new()
        {
            TagId = tag.Id,
            Name = tag.Name,
            EffectiveColor = colors is not null && colors.TryGetValue(tag.Id, out var mapped)
                ? mapped
                : string.IsNullOrWhiteSpace(tag.Color) ? null : tag.Color,
            IsStarred = tag.IsStarred,
            ParentId = parentId
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
        bool UncategorizedMatches() =>
            NameMatches("Uncategorized") || NameMatches("Ungrouped");

        var starred = tags
            .Where(t => t.IsStarred && InScope(t) && NameMatches(t.Name))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => Chip(t))
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
                    .Where(d => InScope(d.Tag))
                    .OrderBy(d => d.Tag.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(d => d.Tag.Name, StringComparer.Ordinal)
                    .ToList();
                var groupMatches = NameMatches(root.Name);
                var chips = descendants
                    .Where(d => groupMatches || NameMatches(d.Tag.Name))
                    .Select(d => Chip(d.Tag, d.ParentId))
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
            var ungroupedGroupMatches = UncategorizedMatches();
            var ungroupedChips = ungrouped
                .Where(t => ungroupedGroupMatches || NameMatches(t.Name))
                .Select(t => Chip(t))
                .ToList();
            if (ungroupedChips.Count > 0 || (q.Length == 0 && scope == TagScope.Ungrouped))
            {
                groups.Add(new TagPanelGroup
                {
                    Name = "Uncategorized",
                    IsUngrouped = true,
                    ScopeKind = TagScope.Ungrouped,
                    AutomationIdOverride = "BtnTagGroup_Ungrouped",
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

    public static IReadOnlyList<TagPanelGroup> BuildBoard(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships,
        string? query = null,
        IReadOnlyDictionary<string, string?>? colors = null)
    {
        var model = Build(tags, memberships, query: query, colors: colors, scope: TagScope.All);
        var q = (query ?? "").Trim();
        bool NameMatches(string name) =>
            q.Length == 0 || name.Contains(q, StringComparison.OrdinalIgnoreCase);

        var byId = tags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        TagPanelChip Chip(Tag tag) => new()
        {
            TagId = tag.Id,
            Name = tag.Name,
            EffectiveColor = colors is not null && colors.TryGetValue(tag.Id, out var mapped)
                ? mapped
                : string.IsNullOrWhiteSpace(tag.Color) ? null : tag.Color,
            IsStarred = tag.IsStarred
        };

        var all = new List<TagPanelChip>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(TagPanelChip chip)
        {
            if (seen.Add(chip.TagId))
            {
                all.Add(chip);
            }
        }

        if (q.Length == 0)
        {
            foreach (var tag in tags
                         .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(t => t.Name, StringComparer.Ordinal))
            {
                Add(Chip(tag));
            }
        }
        else
        {
            foreach (var group in model.Groups)
            {
                foreach (var chip in group.Chips)
                {
                    Add(WithoutParent(chip));
                }

                if (group.GroupId is { } id && NameMatches(group.Name) && byId.TryGetValue(id, out var root))
                {
                    Add(Chip(root));
                }
            }

            foreach (var tag in tags.Where(t => NameMatches(t.Name)))
            {
                Add(Chip(tag));
            }

            all = all
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToList();
        }

        var uncategorized = model.Groups.FirstOrDefault(g => g.IsUngrouped);
        // All / Uncategorized / Starred stay pinned at the top of the group column.
        var sections = new List<TagPanelGroup>
        {
            new()
            {
                Name = "All",
                ScopeKind = TagScope.All,
                AutomationIdOverride = "SelTagScopeAll",
                Chips = all
            },
            new()
            {
                Name = "Uncategorized",
                IsUngrouped = true,
                ScopeKind = TagScope.Ungrouped,
                AutomationIdOverride = uncategorized?.AutomationIdOverride ?? "BtnTagGroup_Ungrouped",
                Chips = uncategorized?.Chips ?? []
            },
            new()
            {
                Name = "Starred",
                ScopeKind = TagScope.Starred,
                AutomationIdOverride = "SelTagScopeStarred",
                Chips = model.Starred
            }
        };
        sections.AddRange(model.Groups.Where(g => !g.IsUngrouped));
        return sections;
    }

    public static string Key(TagPanelGroup group) =>
        group.ScopeKind switch
        {
            TagScope.All => "all",
            TagScope.Ungrouped => "ungrouped",
            TagScope.Starred => "starred",
            _ => group.GroupId ?? "ungrouped"
        };

    public static TagPanelGroup? GroupByKey(IReadOnlyList<TagPanelGroup> board, string key) =>
        board.FirstOrDefault(group => string.Equals(Key(group), key, StringComparison.Ordinal));

    private static TagPanelChip WithoutParent(TagPanelChip chip) => new()
    {
        TagId = chip.TagId,
        Name = chip.Name,
        EffectiveColor = chip.EffectiveColor,
        IsStarred = chip.IsStarred
    };

    private static IEnumerable<(Tag Tag, string ParentId)> FlattenDescendants(
        string rootId,
        Dictionary<string, List<Tag>> children)
    {
        if (!children.TryGetValue(rootId, out var kids))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<(Tag Tag, string ParentId)>();
        foreach (var kid in kids)
        {
            stack.Push((kid, rootId));
        }

        while (stack.Count > 0)
        {
            var (tag, parentId) = stack.Pop();
            if (!seen.Add(tag.Id))
            {
                continue;
            }

            yield return (tag, parentId);
            if (!children.TryGetValue(tag.Id, out var nested))
            {
                continue;
            }

            foreach (var kid in nested)
            {
                stack.Push((kid, tag.Id));
            }
        }
    }
}
