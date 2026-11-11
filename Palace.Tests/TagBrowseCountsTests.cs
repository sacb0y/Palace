using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagBrowseCountsTests
{
    [Fact]
    public void ForTags_CountsDirectAssignments()
    {
        var pairs = new (string, string)[]
        {
            ("a1", "landscape"),
            ("a2", "landscape"),
            ("a3", "wip")
        };

        var counts = TagBrowseCounts.ForTags(pairs, implications: null, ["landscape", "wip", "missing"]);
        Assert.Equal(2, counts["landscape"]);
        Assert.Equal(1, counts["wip"]);
        Assert.Equal(0, counts["missing"]);
    }

    [Fact]
    public void ForTags_IncludesImplyingSources()
    {
        // landscape implies environment → environment row includes landscape assets.
        var pairs = new (string, string)[]
        {
            ("a1", "landscape"),
            ("a2", "environment")
        };
        var edges = new List<TagImplication>
        {
            new() { TagId = "landscape", ImpliedTagId = "environment" }
        };

        var counts = TagBrowseCounts.ForTags(pairs, edges, ["environment", "landscape"]);
        Assert.Equal(2, counts["environment"]);
        Assert.Equal(1, counts["landscape"]);
    }

    [Fact]
    public void ForTags_DedupesOverlappingImplyingAssets()
    {
        var pairs = new (string, string)[]
        {
            ("a1", "landscape"),
            ("a1", "environment")
        };
        var edges = new List<TagImplication>
        {
            new() { TagId = "landscape", ImpliedTagId = "environment" }
        };

        var counts = TagBrowseCounts.ForTags(pairs, edges, ["environment"]);
        Assert.Equal(1, counts["environment"]);
    }

    [Fact]
    public void Face_EmptyWhenZero()
    {
        Assert.Equal("184", TagBrowseCounts.Face(184));
        Assert.Equal("", TagBrowseCounts.Face(0));
    }
}
