using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagPanelBuilderTests
{
    private static (List<Tag> Tags, List<TagMembership> Edges) Sample()
    {
        var tags = new List<Tag>
        {
            new() { Id = "character", Name = "Character", Priority = 10, Color = "#FF0078D4" },
            new() { Id = "sonic", Name = "Sonic", Priority = 5, IsStarred = true },
            new() { Id = "tails", Name = "Tails", Priority = 4 },
            new() { Id = "shot", Name = "Shot", Priority = 8 },
            new() { Id = "closeup", Name = "Closeup", Priority = 1 },
            new() { Id = "loose", Name = "Loose", Priority = 0 }
        };
        var edges = new List<TagMembership>
        {
            new() { ParentId = "character", ChildId = "sonic" },
            new() { ParentId = "character", ChildId = "tails" },
            new() { ParentId = "shot", ChildId = "closeup" }
        };
        return (tags, edges);
    }

    [Fact]
    public void Build_GroupsDescendantsUnderRoot_AndUngroupedBucket()
    {
        var (tags, edges) = Sample();
        var model = TagPanelBuilder.Build(tags, edges);

        Assert.Equal(2, model.Groups.Count(g => !g.IsUngrouped));
        var character = model.Groups.Single(g => g.Name == "Character");
        Assert.Equal(["Sonic", "Tails"], character.Chips.Select(c => c.Name).OrderBy(n => n).ToArray());
        Assert.Contains(model.Groups, g => g.IsUngrouped && g.Chips.Any(c => c.Name == "Loose"));
        Assert.Single(model.Starred);
        Assert.Equal("Sonic", model.Starred[0].Name);
    }

    [Fact]
    public void Build_SortsChipsAlphabetically_NotByPriority()
    {
        var (tags, edges) = Sample();
        tags.Add(new Tag { Id = "amy", Name = "Amy", Priority = 0 });
        edges.Add(new TagMembership { ParentId = "character", ChildId = "amy" });

        var model = TagPanelBuilder.Build(tags, edges);
        var character = model.Groups.Single(g => g.Name == "Character");
        Assert.Equal(["Amy", "Sonic", "Tails"], character.Chips.Select(c => c.Name).ToArray());
        Assert.Equal(["Character", "Shot"], model.Groups.Where(g => !g.IsUngrouped).Select(g => g.Name).ToArray());
    }

    [Fact]
    public void Build_NestedChip_KeepsImmediateParent()
    {
        var (tags, edges) = Sample();
        tags.Add(new Tag { Id = "hedgehog", Name = "Hedgehog", Priority = 3 });
        edges.Add(new TagMembership { ParentId = "character", ChildId = "hedgehog" });
        edges.Add(new TagMembership { ParentId = "hedgehog", ChildId = "sonic" });

        var model = TagPanelBuilder.Build(tags, edges);
        var character = model.Groups.Single(g => g.Name == "Character");
        var sonic = Assert.Single(character.Chips, c => c.Name == "Sonic");
        var hedgehog = Assert.Single(character.Chips, c => c.Name == "Hedgehog");
        Assert.Equal("hedgehog", sonic.ParentId);
        Assert.Equal("character", hedgehog.ParentId);
    }

    [Fact]
    public void Build_SearchByGroupName_ReturnsAllChips()
    {
        var (tags, edges) = Sample();
        var model = TagPanelBuilder.Build(tags, edges, query: "char");

        var character = Assert.Single(model.Groups, g => !g.IsUngrouped);
        Assert.Equal("Character", character.Name);
        Assert.Equal(2, character.Chips.Count);
        Assert.DoesNotContain(model.Groups, g => g.Name == "Shot");
    }

    [Fact]
    public void Build_SearchByTagName_KeepsParentGroup()
    {
        var (tags, edges) = Sample();
        var model = TagPanelBuilder.Build(tags, edges, query: "close");

        var shot = Assert.Single(model.Groups, g => !g.IsUngrouped);
        Assert.Equal("Shot", shot.Name);
        Assert.Equal("Closeup", Assert.Single(shot.Chips).Name);
    }

    [Fact]
    public void Build_SearchUngroupedGroupName_ReturnsLooseTags()
    {
        var (tags, edges) = Sample();
        var model = TagPanelBuilder.Build(tags, edges, query: "ungroup");

        var bucket = Assert.Single(model.Groups);
        Assert.True(bucket.IsUngrouped);
        Assert.Equal("Uncategorized", bucket.Name);
        Assert.Equal(["Loose"], bucket.Chips.Select(c => c.Name).ToArray());
        Assert.Equal("BtnTagGroup_Ungrouped", bucket.AutomationIdOverride);
        Assert.DoesNotContain(model.Groups, g => g.Name == "Character");
    }

    [Fact]
    public void Build_Uncategorized_KeepsStableAutomationId()
    {
        var (tags, edges) = Sample();
        var model = TagPanelBuilder.Build(tags, edges);
        var bucket = Assert.Single(model.Groups, g => g.IsUngrouped);
        Assert.Equal("Uncategorized", bucket.Name);
        Assert.Equal("BtnTagGroup_Ungrouped", bucket.AutomationIdOverride);
    }

    [Fact]
    public void BuildBoard_SearchAllChips_HaveNoParent()
    {
        var (tags, edges) = Sample();
        var board = TagPanelBuilder.BuildBoard(tags, edges, query: "sonic");

        var all = Assert.Single(board, g => g.ScopeKind == TagScope.All);
        var sonic = Assert.Single(all.Chips, c => c.Name == "Sonic");
        Assert.Null(sonic.ParentId);

        var character = Assert.Single(board, g => g.Name == "Character");
        var grouped = Assert.Single(character.Chips, c => c.Name == "Sonic");
        Assert.Equal("character", grouped.ParentId);
    }

    [Fact]
    public void BuildBoard_StartsWithAllUncategorizedStarred()
    {
        var (tags, edges) = Sample();
        var board = TagPanelBuilder.BuildBoard(tags, edges);

        Assert.Equal(["All", "Uncategorized", "Starred", "Character", "Shot"], board.Select(g => g.Name).ToArray());
        Assert.Equal(TagScope.All, board[0].ScopeKind);
        Assert.Equal(["Character", "Closeup", "Loose", "Shot", "Sonic", "Tails"], board[0].Chips.Select(c => c.Name).ToArray());
        Assert.All(board[0].Chips, chip => Assert.Null(chip.ParentId));
        Assert.True(board[1].IsUngrouped);
        Assert.Equal("BtnTagGroup_Ungrouped", board[1].AutomationIdOverride);
        Assert.Equal(["Loose"], board[1].Chips.Select(c => c.Name).ToArray());
        Assert.Equal(["Sonic"], board[2].Chips.Select(c => c.Name).ToArray());
        Assert.Equal(["Sonic", "Tails"], board.Single(g => g.Name == "Character").Chips.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void Build_StarredScope_HidesGroups()
    {
        var (tags, edges) = Sample();
        var model = TagPanelBuilder.Build(tags, edges, scope: TagScope.Starred);

        Assert.Single(model.Starred);
        Assert.Empty(model.Groups);
    }

    [Fact]
    public void Build_UngroupedScope_OnlyLooseTags()
    {
        var (tags, edges) = Sample();
        var model = TagPanelBuilder.Build(tags, edges, scope: TagScope.Ungrouped);

        var bucket = Assert.Single(model.Groups);
        Assert.True(bucket.IsUngrouped);
        Assert.Equal(["Loose"], bucket.Chips.Select(c => c.Name).ToArray());
        Assert.Empty(model.Starred);
    }

    [Fact]
    public void Build_RecentAndRecommended_PreserveCallerOrder()
    {
        var (tags, edges) = Sample();
        var model = TagPanelBuilder.Build(
            tags,
            edges,
            recentIds: ["closeup", "missing", "sonic"],
            recommendedIds: ["tails", "tails", "loose"]);

        Assert.Equal(["Closeup", "Sonic"], model.Recent.Select(c => c.Name).ToArray());
        Assert.Equal(["Tails", "Loose"], model.Recommended.Select(c => c.Name).ToArray());
    }
}
