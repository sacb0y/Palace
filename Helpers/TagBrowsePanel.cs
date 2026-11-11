using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// T8 Eagle-style Library ▸ Tags panel sections (Starred / Recent / Groups).
/// Pure — off WinUI. Counts on groups are tag counts; asset counts are optional.
/// </summary>
public sealed class TagBrowsePanelSection
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Color { get; init; }
    public bool IsStarredSection { get; init; }
    public bool IsRecentSection { get; init; }
    public bool IsGroupsHeader { get; init; }
    public bool IsUngrouped { get; init; }
    public bool IsExpanded { get; init; } = true;
    public int Count { get; init; }
    public IReadOnlyList<TagPanelChip> Chips { get; init; } = [];
}

public static class TagBrowsePanel
{
    public const string FilterPlaceholder = "Filter tags or groups";

    public const int DefaultVisiblePerGroup = 4;

    /// <summary>
    /// Build Starred, Recent, GROUPS label, then collapsible groups from
    /// <see cref="TagPanelBuilder.Build"/>. Filter matches tag and group names.
    /// <paramref name="extraRootGroupIds"/> pins empty roots under GROUPS (t8-01 +)
    /// and keeps them out of Uncategorized.
    /// </summary>
    public static IReadOnlyList<TagBrowsePanelSection> Build(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships,
        IReadOnlyList<string>? recentIds = null,
        string? query = null,
        IReadOnlyDictionary<string, string?>? colors = null,
        IReadOnlySet<string>? collapsedKeys = null,
        IReadOnlyList<string>? extraRootGroupIds = null)
    {
        var model = TagPanelBuilder.Build(
            tags,
            memberships,
            recentIds: recentIds,
            query: query,
            colors: colors);

        var collapsed = collapsedKeys ?? new HashSet<string>(StringComparer.Ordinal);
        var extra = new HashSet<string>(
            (extraRootGroupIds ?? []).Where(id => !string.IsNullOrEmpty(id)),
            StringComparer.Ordinal);
        var byId = tags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var q = (query ?? "").Trim();
        bool NameMatches(string name) =>
            q.Length == 0 || name.Contains(q, StringComparison.OrdinalIgnoreCase);

        var sections = new List<TagBrowsePanelSection>
        {
            new()
            {
                Key = "starred",
                Name = "Starred",
                IsStarredSection = true,
                IsExpanded = !collapsed.Contains("starred"),
                Count = model.Starred.Count,
                Chips = model.Starred
            },
            new()
            {
                Key = "recent",
                Name = "Recent",
                IsRecentSection = true,
                IsExpanded = !collapsed.Contains("recent"),
                Count = model.Recent.Count,
                Chips = model.Recent
            },
            new()
            {
                Key = "groups-header",
                Name = "GROUPS",
                IsGroupsHeader = true
            }
        };

        var knownGroupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in model.Groups)
        {
            IReadOnlyList<TagPanelChip> chips = group.Chips;
            if (group.IsUngrouped && extra.Count > 0)
            {
                chips = group.Chips.Where(c => !extra.Contains(c.TagId)).ToList();
            }

            if (group.IsUngrouped && chips.Count == 0)
            {
                continue;
            }

            var key = group.IsUngrouped
                ? "ungrouped"
                : group.GroupId ?? group.Name;
            if (!group.IsUngrouped && group.GroupId is { } gid)
            {
                knownGroupIds.Add(gid);
            }

            sections.Add(new TagBrowsePanelSection
            {
                Key = key,
                Name = group.Name,
                Color = group.Color,
                IsUngrouped = group.IsUngrouped,
                IsExpanded = !collapsed.Contains(key),
                Count = chips.Count,
                Chips = chips
            });
        }

        if (extra.Count > 0)
        {
            var inserted = new List<TagBrowsePanelSection>();
            foreach (var id in extra)
            {
                if (!knownGroupIds.Add(id) || !byId.TryGetValue(id, out var root))
                {
                    continue;
                }

                if (q.Length > 0 && !NameMatches(root.Name))
                {
                    continue;
                }

                var key = root.Id;
                inserted.Add(new TagBrowsePanelSection
                {
                    Key = key,
                    Name = root.Name,
                    Color = colors is not null && colors.TryGetValue(root.Id, out var mapped)
                        ? mapped
                        : string.IsNullOrWhiteSpace(root.Color) ? null : root.Color,
                    IsExpanded = !collapsed.Contains(key),
                    Count = 0,
                    Chips = []
                });
            }

            if (inserted.Count > 0)
            {
                // Keep user groups A–Z after GROUPS header (pinned sections stay first).
                var headerIndex = sections.FindIndex(s => s.IsGroupsHeader);
                var before = headerIndex < 0 ? sections.Count : headerIndex + 1;
                var user = sections.Skip(before).ToList();
                user.AddRange(inserted);
                user = user
                    .OrderBy(s => s.IsUngrouped ? 1 : 0)
                    .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(s => s.Name, StringComparer.Ordinal)
                    .ToList();
                sections = sections.Take(before).Concat(user).ToList();
            }
        }

        return sections;
    }

    /// <summary>How many chips to show before a "Show N more" affordance.</summary>
    public static int VisibleChipCount(int total, bool showAll, int limit = DefaultVisiblePerGroup) =>
        showAll || total <= limit ? Math.Max(0, total) : Math.Max(0, limit);

    /// <summary>Tags hidden while a browse group is truncated (group count stays <paramref name="total"/>).</summary>
    public static int HiddenChipCount(int total, bool showAll, int limit = DefaultVisiblePerGroup) =>
        Math.Max(0, total - VisibleChipCount(total, showAll, limit));

    public static string ShowMoreLabel(int hidden) =>
        hidden <= 0 ? "" : $"Show {hidden} more";
}
