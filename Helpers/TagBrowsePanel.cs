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
    /// </summary>
    public static IReadOnlyList<TagBrowsePanelSection> Build(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships,
        IReadOnlyList<string>? recentIds = null,
        string? query = null,
        IReadOnlyDictionary<string, string?>? colors = null,
        IReadOnlySet<string>? collapsedKeys = null)
    {
        var model = TagPanelBuilder.Build(
            tags,
            memberships,
            recentIds: recentIds,
            query: query,
            colors: colors);

        var collapsed = collapsedKeys ?? new HashSet<string>(StringComparer.Ordinal);
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

        foreach (var group in model.Groups)
        {
            var key = group.IsUngrouped
                ? "ungrouped"
                : group.GroupId ?? group.Name;
            sections.Add(new TagBrowsePanelSection
            {
                Key = key,
                Name = group.Name,
                Color = group.Color,
                IsUngrouped = group.IsUngrouped,
                IsExpanded = !collapsed.Contains(key),
                Count = group.Chips.Count,
                Chips = group.Chips
            });
        }

        return sections;
    }

    /// <summary>How many chips to show before a "Show N more" affordance.</summary>
    public static int VisibleChipCount(int total, bool showAll, int limit = DefaultVisiblePerGroup) =>
        showAll || total <= limit ? Math.Max(0, total) : Math.Max(0, limit);

    public static string ShowMoreLabel(int hidden) =>
        hidden <= 0 ? "" : $"Show {hidden} more";
}
