using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagGroupsTests
{
    private static (List<Tag> Tags, List<TagMembership> Edges) Sample()
    {
        var tags = new List<Tag>
        {
            new() { Id = "character", Name = "Character" },
            new() { Id = "hedgehog", Name = "Hedgehog" },
            new() { Id = "sonic", Name = "Sonic" },
            new() { Id = "shot", Name = "Shot" },
            new() { Id = "closeup", Name = "Closeup" },
            new() { Id = "loose", Name = "Loose" }
        };
        var edges = new List<TagMembership>
        {
            new() { ParentId = "character", ChildId = "hedgehog" },
            new() { ParentId = "hedgehog", ChildId = "sonic" },
            new() { ParentId = "character", ChildId = "sonic" },
            new() { ParentId = "shot", ChildId = "closeup" }
        };
        return (tags, edges);
    }

    [Fact]
    public void Root_ListsTopLevelGroupsOnly()
    {
        var (tags, edges) = Sample();
        var roots = TagGroups.Root(tags, edges).Select(t => t.Name).ToArray();

        Assert.Equal(["Character", "Shot"], roots);
        Assert.DoesNotContain("Hedgehog", roots);
        Assert.DoesNotContain("Loose", roots);
    }

    [Fact]
    public void Destinations_Add_SkipsCurrentGroupAndDescendants()
    {
        var (tags, edges) = Sample();
        var add = TagGroups.Destinations(tags, edges, "sonic", "character", add: true)
            .Select(t => t.Name)
            .ToArray();

        Assert.Equal(["Shot"], add);
    }

    [Fact]
    public void Destinations_Move_SkipsCurrentRootAndSelfCycle()
    {
        var (tags, edges) = Sample();
        var move = TagGroups.Destinations(tags, edges, "sonic", "character", add: false)
            .Select(t => t.Name)
            .ToArray();

        Assert.Equal(["Shot"], move);

        var fromGroup = TagGroups.Destinations(tags, edges, "character", null, add: true)
            .Select(t => t.Id)
            .ToArray();
        Assert.Equal(["shot"], fromGroup);
    }

    [Fact]
    public void Destinations_Ungrouped_CanAddToAnyRoot()
    {
        var (tags, edges) = Sample();
        var add = TagGroups.Destinations(tags, edges, "loose", null, add: true)
            .Select(t => t.Name)
            .ToArray();

        Assert.Equal(["Character", "Shot"], add);
    }
}
