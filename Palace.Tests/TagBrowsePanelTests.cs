using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagBrowsePanelTests
{
    [Fact]
    public void Build_PinsStarredRecentAndGroupsHeader()
    {
        var tags = new List<Tag>
        {
            new() { Id = "subject", Name = "Subject", Color = "#14B8A6" },
            new() { Id = "landscape", Name = "landscape", IsStarred = true },
            new() { Id = "wip", Name = "wip", IsStarred = true },
            new() { Id = "sketch", Name = "sketch" }
        };
        var edges = new List<TagMembership>
        {
            new() { ParentId = "subject", ChildId = "landscape" }
        };

        var sections = TagBrowsePanel.Build(
            tags,
            edges,
            recentIds: ["sketch", "landscape"]);

        Assert.Equal(["starred", "recent", "groups-header", "subject", "ungrouped"],
            sections.Select(s => s.Key).ToArray());
        Assert.Equal(["landscape", "wip"], sections[0].Chips.Select(c => c.Name).ToArray());
        Assert.Equal(["sketch", "landscape"], sections[1].Chips.Select(c => c.Name).ToArray());
        Assert.True(sections[2].IsGroupsHeader);
        Assert.Equal("Subject", sections[3].Name);
        Assert.Equal(1, sections[3].Count);
    }

    [Fact]
    public void Build_FilterMatchesGroupAndTagNames()
    {
        var tags = new List<Tag>
        {
            new() { Id = "medium", Name = "Medium", Color = "#A855F7" },
            new() { Id = "sketch", Name = "sketch" },
            new() { Id = "wip", Name = "wip" }
        };
        var edges = new List<TagMembership>
        {
            new() { ParentId = "medium", ChildId = "sketch" }
        };

        var byGroup = TagBrowsePanel.Build(tags, edges, query: "med");
        Assert.Contains(byGroup, s => s.Key == "medium" && s.Chips.Any(c => c.Name == "sketch"));

        var byTag = TagBrowsePanel.Build(tags, edges, query: "wip");
        Assert.Contains(byTag, s => s.IsUngrouped && s.Chips.Any(c => c.Name == "wip"));
        Assert.DoesNotContain(byTag, s => s.Key == "medium" && s.Chips.Count > 0);
    }

    [Fact]
    public void VisibleChipCount_AndShowMore()
    {
        Assert.Equal(4, TagBrowsePanel.VisibleChipCount(10, showAll: false));
        Assert.Equal(10, TagBrowsePanel.VisibleChipCount(10, showAll: true));
        Assert.Equal(3, TagBrowsePanel.VisibleChipCount(3, showAll: false));
        Assert.Equal(6, TagBrowsePanel.HiddenChipCount(10, showAll: false));
        Assert.Equal(0, TagBrowsePanel.HiddenChipCount(10, showAll: true));
        Assert.Equal(0, TagBrowsePanel.HiddenChipCount(3, showAll: false));
        Assert.Equal("Show 6 more", TagBrowsePanel.ShowMoreLabel(6));
        Assert.Equal("", TagBrowsePanel.ShowMoreLabel(0));
    }

    [Fact]
    public void Build_GroupCount_IsFullSize_NotVisibleChipLimit()
    {
        var tags = new List<Tag>
        {
            new() { Id = "subject", Name = "Subject", Color = "#14B8A6" },
            new() { Id = "a", Name = "alpha" },
            new() { Id = "b", Name = "bravo" },
            new() { Id = "c", Name = "charlie" },
            new() { Id = "d", Name = "delta" },
            new() { Id = "e", Name = "echo" },
            new() { Id = "f", Name = "foxtrot" }
        };
        var edges = tags.Skip(1)
            .Select(t => new TagMembership { ParentId = "subject", ChildId = t.Id })
            .ToList();

        var subject = TagBrowsePanel.Build(tags, edges).Single(s => s.Key == "subject");
        Assert.Equal(6, subject.Count);
        Assert.Equal(6, subject.Chips.Count);
        Assert.Equal(4, TagBrowsePanel.VisibleChipCount(subject.Count, showAll: false));
        Assert.Equal(2, TagBrowsePanel.HiddenChipCount(subject.Count, showAll: false));
        Assert.Equal("Show 2 more", TagBrowsePanel.ShowMoreLabel(
            TagBrowsePanel.HiddenChipCount(subject.Count, showAll: false)));
    }

    [Fact]
    public void FilterPlaceholder_MatchesMockup() =>
        Assert.Equal("Filter tags or groups", TagBrowsePanel.FilterPlaceholder);

    [Fact]
    public void Build_ExtraRootGroupIds_ShowsEmptyGroup_NotUncategorized()
    {
        var tags = new List<Tag>
        {
            new() { Id = "subject", Name = "Subject", Color = "#14B8A6" },
            new() { Id = "landscape", Name = "landscape" },
            new() { Id = "new-group", Name = "New group" }
        };
        var edges = new List<TagMembership>
        {
            new() { ParentId = "subject", ChildId = "landscape" }
        };

        var sections = TagBrowsePanel.Build(
            tags,
            edges,
            extraRootGroupIds: ["new-group"]);

        Assert.Contains(sections, s => s.Key == "new-group" && s.Count == 0 && !s.IsUngrouped);
        var uncategorized = sections.SingleOrDefault(s => s.IsUngrouped);
        Assert.True(uncategorized is null || uncategorized.Chips.All(c => c.TagId != "new-group"));
    }
}
