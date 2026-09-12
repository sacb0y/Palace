using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagMosaicGroupsTests
{
    private static (List<Tag> Tags, List<TagMembership> Edges) Sonic()
    {
        var tags = new List<Tag>
        {
            new() { Id = "sonic", Name = "Sonic the Hedgehog", Priority = 20 },
            new() { Id = "character", Name = "Character", Priority = 10 },
            new() { Id = "female", Name = "Female", Priority = 5 },
            new() { Id = "male", Name = "Male", Priority = 4 },
            new() { Id = "shot", Name = "Shot", Priority = 8 }
        };
        var edges = new List<TagMembership>
        {
            new() { ParentId = "sonic", ChildId = "character" },
            new() { ParentId = "character", ChildId = "female" },
            new() { ParentId = "character", ChildId = "male" },
            new() { ParentId = "sonic", ChildId = "shot" }
        };
        return (tags, edges);
    }

    [Fact]
    public void PathFor_SelectedOnly_IsTheGroup()
    {
        var (tags, edges) = Sonic();
        var path = TagMosaicGroups.LabelFor("sonic", ["sonic"], tags, edges);
        Assert.Equal("Sonic the Hedgehog", path);
    }

    [Fact]
    public void PathFor_WalksSelectedThenChildren()
    {
        var (tags, edges) = Sonic();
        var path = TagMosaicGroups.LabelFor("sonic", ["sonic", "character", "female"], tags, edges);
        Assert.Equal("Sonic the Hedgehog / Character / Female", path);
    }

    [Fact]
    public void PathFor_PrefersDeepestAssignedChild()
    {
        var (tags, edges) = Sonic();
        var path = TagMosaicGroups.LabelFor("sonic", ["character", "female", "shot"], tags, edges);
        Assert.Equal("Sonic the Hedgehog / Character / Female", path);
    }

    [Fact]
    public void PathFor_SameDepth_UsesPriority()
    {
        var (tags, edges) = Sonic();
        var path = TagMosaicGroups.LabelFor("sonic", ["female", "male"], tags, edges);
        Assert.Equal("Sonic the Hedgehog / Character / Female", path);
    }

    [Fact]
    public void Group_OrdersByPriorityThenShorterPath()
    {
        var (tags, edges) = Sonic();
        var assigned = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["a"] = ["sonic"],
            ["b"] = ["female"],
            ["c"] = ["shot"]
        };
        var groups = TagMosaicGroups.Group(
            "sonic",
            ["a", "b", "c"],
            id => id,
            assigned,
            tags,
            edges);

        Assert.Equal(
            [
                "Sonic the Hedgehog",
                "Sonic the Hedgehog / Character / Female",
                "Sonic the Hedgehog / Shot"
            ],
            groups.Select(g => g.Header).ToArray());
        Assert.Equal(["a"], groups[0].Items);
        Assert.Equal(["b"], groups[1].Items);
        Assert.Equal(["c"], groups[2].Items);
    }

    [Fact]
    public void PathFor_UnknownSelected_IsEmpty()
    {
        var (tags, edges) = Sonic();
        var path = TagMosaicGroups.PathFor("missing", ["female"], tags, edges);
        Assert.Empty(path);
    }

    [Fact]
    public void PathFor_MultipleParents_UsesHighestPriorityUnderRoot()
    {
        var (tags, edges) = Sonic();
        tags.Add(new Tag { Id = "species", Name = "Species", Priority = 3 });
        edges.Add(new TagMembership { ParentId = "sonic", ChildId = "species" });
        edges.Add(new TagMembership { ParentId = "species", ChildId = "female" });
        var path = TagMosaicGroups.LabelFor("sonic", ["female"], tags, edges);
        Assert.Equal("Sonic the Hedgehog / Character / Female", path);
    }

    [Fact]
    public void ComparePaths_HigherPriorityChildComesFirst()
    {
        var character = new Tag { Id = "c", Name = "Character", Priority = 10 };
        var shot = new Tag { Id = "s", Name = "Shot", Priority = 8 };
        var root = new Tag { Id = "r", Name = "Sonic the Hedgehog", Priority = 20 };
        var left = new[] { root, character };
        var right = new[] { root, shot };
        Assert.True(TagMosaicGroups.ComparePaths(left, right) < 0);
    }
}
