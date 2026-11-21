using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// WinUI-free drag / Add-to-group membership rules (dg-01…05). Classify edges, status
/// copy, drag hints, and menu picks. Journal only <see cref="MembershipAddResult.Added"/>.
/// </summary>
public static class TagMembershipAdd
{
    public const string DataKey = "PalaceTagIds";

    /// <summary>Classify a prospective parent←child edge against an in-memory graph.</summary>
    public static MembershipAddResult Classify(
        string parentId,
        string childId,
        IReadOnlyList<TagMembership> memberships)
    {
        if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(childId))
        {
            return MembershipAddResult.CycleRefuse;
        }

        if (string.Equals(parentId, childId, StringComparison.Ordinal))
        {
            return MembershipAddResult.CycleRefuse;
        }

        if (TagGroups.DescendantsIncludingSelf(memberships, childId).Contains(parentId))
        {
            return MembershipAddResult.CycleRefuse;
        }

        foreach (var edge in memberships)
        {
            if (string.Equals(edge.ParentId, parentId, StringComparison.Ordinal)
                && string.Equals(edge.ChildId, childId, StringComparison.Ordinal))
            {
                return MembershipAddResult.AlreadyMember;
            }
        }

        return MembershipAddResult.Added;
    }

    public static (int Added, int AlreadyMember, int CycleRefuse) CountResults(
        string parentId,
        IEnumerable<string> childIds,
        IReadOnlyList<TagMembership> memberships)
    {
        var added = 0;
        var already = 0;
        var cycle = 0;
        foreach (var childId in childIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal))
        {
            switch (Classify(parentId, childId, memberships))
            {
                case MembershipAddResult.Added:
                    added++;
                    break;
                case MembershipAddResult.AlreadyMember:
                    already++;
                    break;
                case MembershipAddResult.CycleRefuse:
                    cycle++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return (added, already, cycle);
    }

    /// <summary>
    /// Cursor hint while hovering a group row. Mixed selections that can still add are
    /// allowed ("Add 2 · 1 already in group"); pure already / cycle refuse.
    /// </summary>
    public static string DragHint(
        string? groupName,
        int added,
        int alreadyMember,
        int cycleRefuse)
    {
        var name = string.IsNullOrWhiteSpace(groupName) ? "group" : groupName.Trim();
        if (added > 0)
        {
            if (alreadyMember == 0 && cycleRefuse == 0)
            {
                return $"Add to {name}";
            }

            var parts = new List<string> { $"Add {added}" };
            if (alreadyMember > 0)
            {
                parts.Add($"{alreadyMember} already in group");
            }

            if (cycleRefuse > 0)
            {
                parts.Add($"{cycleRefuse} refused (cycle)");
            }

            return string.Join(" · ", parts);
        }

        if (cycleRefuse > 0 && alreadyMember == 0)
        {
            return $"Can't add {name} to itself";
        }

        if (alreadyMember > 0 && cycleRefuse == 0)
        {
            return $"Already in {name}";
        }

        if (alreadyMember > 0 && cycleRefuse > 0)
        {
            return $"Already in {name}";
        }

        return $"Can't add to {name}";
    }

    /// <summary>True when a drop commits at least one new edge.</summary>
    public static bool AllowsDrop(int added) => added > 0;

    public static string MultiBadge(int count) =>
        count <= 1 ? "" : $"{count} tags";

    public static IReadOnlyList<string> PayloadIds(
        string chipId,
        bool chipSelected,
        IEnumerable<string> selectedIds) =>
        AssetDrag.Ids(chipId, chipSelected, selectedIds);

    public static string Serialize(IEnumerable<string> ids) => AssetDrag.Serialize(ids);

    public static IReadOnlyList<string> Parse(object? value) => AssetDrag.Parse(value);

    /// <summary>
    /// Add-to-group menu: every root group with colour, member/cycle flags.
    /// Already-member rows stay listed (checked + disabled in UI). Move still uses
    /// <see cref="TagGroups.Destinations"/>.
    /// </summary>
    public static IReadOnlyList<TagGroupMenuPick> AddMenuPicks(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships,
        string tagId)
    {
        var blocked = TagGroups.DescendantsIncludingSelf(memberships, tagId);
        return TagGroups.Root(tags, memberships)
            .Select(group =>
            {
                var isCycle = blocked.Contains(group.Id);
                var isMember = !isCycle
                    && TagSiblings.ParentsUnderRoot(memberships, tagId, group.Id).Count > 0;
                return new TagGroupMenuPick
                {
                    TagId = group.Id,
                    Name = group.Name,
                    Color = group.Color,
                    IsMember = isMember,
                    IsCycle = isCycle
                };
            })
            .ToList();
    }

    /// <summary>
    /// Status after a batch. Journals only Added; extras describe no-ops.
    /// dg-04: "✓ Added 3 tags to Character"; mixed appends · clauses.
    /// </summary>
    public static string StatusMessage(
        int added,
        int alreadyMember,
        int cycleRefuse,
        string? groupName,
        bool createdGroup = false)
    {
        var name = string.IsNullOrWhiteSpace(groupName) ? "group" : groupName.Trim();
        if (added <= 0 && !createdGroup)
        {
            if (cycleRefuse > 0 && alreadyMember == 0)
            {
                return $"Can't add tags to {name} (cycle).";
            }

            if (alreadyMember > 0 && cycleRefuse == 0)
            {
                return alreadyMember == 1
                    ? $"Already in {name}."
                    : $"Already in {name} ({alreadyMember}).";
            }

            return $"Nothing added to {name}.";
        }

        string core;
        if (createdGroup && added > 0)
        {
            core = added == 1
                ? $"Created {name} and added 1 tag"
                : $"Created {name} and added {added} tags";
        }
        else if (createdGroup)
        {
            core = $"Created group {name}";
        }
        else
        {
            core = added == 1
                ? $"Added 1 tag to {name}"
                : $"Added {added} tags to {name}";
        }

        var parts = new List<string> { core };
        if (alreadyMember > 0)
        {
            parts.Add($"{alreadyMember} already in group");
        }

        if (cycleRefuse > 0)
        {
            parts.Add($"{cycleRefuse} refused (cycle)");
        }

        return TagStatusBar.WithCheck(string.Join(" · ", parts));
    }

    /// <summary>Journal summary (no leading checkmark) for FileOpBatch.Summary.</summary>
    public static string JournalSummary(
        int added,
        string? groupName,
        bool createdGroup = false)
    {
        var status = StatusMessage(added, 0, 0, groupName, createdGroup);
        return status.StartsWith(TagStatusBar.CheckMark, StringComparison.Ordinal)
            ? status[TagStatusBar.CheckMark.Length..].TrimStart()
            : status;
    }
}

/// <summary>One root group row in the chip Add to group › submenu (dg-05).</summary>
public sealed class TagGroupMenuPick
{
    public string TagId { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Color { get; init; }
    public bool IsMember { get; init; }
    public bool IsCycle { get; init; }
    public bool IsEnabled => !IsMember && !IsCycle;

    public override string ToString() => Name;
}
