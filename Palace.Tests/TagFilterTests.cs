using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagFilterTests
{
    [Fact]
    public void Expand_IncludesSelfAndDescendants()
    {
        var edges = new List<TagMembership>
        {
            new() { ParentId = "character", ChildId = "sonic" },
            new() { ParentId = "sonic", ChildId = "classic" },
            new() { ParentId = "shot", ChildId = "closeup" }
        };

        var expanded = TagFilter.Expand("character", edges);
        Assert.Contains("character", expanded);
        Assert.Contains("sonic", expanded);
        Assert.Contains("classic", expanded);
        Assert.DoesNotContain("closeup", expanded);
        Assert.DoesNotContain("shot", expanded);
    }

    [Fact]
    public void Match_Any_UnionsExpandedSets()
    {
        var sets = new List<IReadOnlySet<string>>
        {
            new HashSet<string>(StringComparer.Ordinal) { "sonic", "classic" },
            new HashSet<string>(StringComparer.Ordinal) { "closeup" }
        };
        var tags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["a"] = ["sonic"],
            ["b"] = ["closeup"],
            ["c"] = ["other"]
        };

        var hit = TagFilter.MatchAssetIds(TagFilterMode.Any, sets, tags);
        Assert.Equal(["a", "b"], hit.OrderBy(x => x));
    }

    [Fact]
    public void Match_All_RequiresEverySet()
    {
        var sets = new List<IReadOnlySet<string>>
        {
            new HashSet<string>(StringComparer.Ordinal) { "sonic", "classic" },
            new HashSet<string>(StringComparer.Ordinal) { "closeup" }
        };
        var tags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["both"] = ["classic", "closeup"],
            ["onlySonic"] = ["sonic"],
            ["onlyClose"] = ["closeup"]
        };

        var hit = TagFilter.MatchAssetIds(TagFilterMode.All, sets, tags);
        Assert.Equal(["both"], hit.OrderBy(x => x));
    }

    [Fact]
    public void Match_None_ExcludesHitsFromUniverse()
    {
        var sets = new List<IReadOnlySet<string>>
        {
            new HashSet<string>(StringComparer.Ordinal) { "sonic" }
        };
        var tags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["tagged"] = ["sonic"],
            ["other"] = ["closeup"]
        };
        var universe = new HashSet<string>(StringComparer.Ordinal) { "tagged", "other", "plain" };

        var hit = TagFilter.MatchAssetIds(TagFilterMode.None, sets, tags, universe);
        Assert.Equal(["other", "plain"], hit.OrderBy(x => x));
    }

    [Fact]
    public void Match_EmptySelection_ReturnsUniverse()
    {
        var tags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["a"] = ["sonic"]
        };
        var universe = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };
        var hit = TagFilter.MatchAssetIds(TagFilterMode.All, [], tags, universe);
        Assert.Equal(["a", "b"], hit.OrderBy(x => x));
    }

    [Fact]
    public void Expand_AliasAndIdeal_MatchSameMembershipSet()
    {
        var memberships = new List<TagMembership>
        {
            new() { ParentId = "dog", ChildId = "puppy" }
        };
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["doggy"] = "dog"
        };

        var viaAlias = TagFilter.Expand("doggy", memberships, aliases);
        var viaIdeal = TagFilter.Expand("dog", memberships, aliases);

        Assert.Contains("dog", viaAlias);
        Assert.Contains("puppy", viaAlias);
        Assert.Contains("doggy", viaAlias); // sibling / selected stale-row defense
        Assert.Equal(viaIdeal.OrderBy(x => x), viaAlias.OrderBy(x => x));
    }

    [Fact]
    public void Expand_Alias_DoesNotAddImplicationClosure()
    {
        // Filter expand is membership-only even when aliases exist — unlike TagFtsQuery.
        var memberships = new List<TagMembership>();
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["doggy"] = "dog"
        };

        var expanded = TagFilter.Expand("doggy", memberships, aliases);
        Assert.Contains("dog", expanded);
        Assert.Contains("doggy", expanded);
        Assert.DoesNotContain("animal", expanded);
    }
}
