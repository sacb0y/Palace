using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagSiblingsTests
{
    private static (List<Tag> Tags, List<TagMembership> Edges) Sample()
    {
        var tags = new List<Tag>
        {
            new() { Id = "character", Name = "Character", Priority = 10 },
            new() { Id = "sonic", Name = "Sonic", Priority = 5 },
            new() { Id = "tails", Name = "Tails", Priority = 4 },
            new() { Id = "shot", Name = "Shot", Priority = 8 },
            new() { Id = "loose", Name = "Loose", Priority = 0 }
        };
        var edges = new List<TagMembership>
        {
            new() { ParentId = "character", ChildId = "sonic" },
            new() { ParentId = "character", ChildId = "tails" }
        };
        return (tags, edges);
    }

    [Fact]
    public void Of_RootIncludesGroupsAndUngrouped()
    {
        var (tags, edges) = Sample();
        var roots = TagSiblings.Of(tags, edges, null)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(["Character", "Loose", "Shot"], roots);
        Assert.DoesNotContain("Sonic", roots);
    }

    [Fact]
    public void Of_ParentListsDirectChildrenOnly()
    {
        var (tags, edges) = Sample();
        var kids = TagSiblings.Of(tags, edges, "character")
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(["Sonic", "Tails"], kids);
    }

    [Fact]
    public void ParentsUnderRoot_IncludesEveryPathIntoTheGroup()
    {
        var edges = new List<TagMembership>
        {
            new() { ParentId = "character", ChildId = "sonic" },
            new() { ParentId = "character", ChildId = "hedgehog" },
            new() { ParentId = "hedgehog", ChildId = "sonic" },
            new() { ParentId = "shot", ChildId = "sonic" }
        };

        var parents = TagSiblings.ParentsUnderRoot(edges, "sonic", "character")
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["character", "hedgehog"], parents);
    }
}
