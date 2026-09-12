using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagBoardExpandTests
{
    private static IReadOnlyList<TagBoardExpand.Row> Board() =>
    [
        new("all", null, ["character", "sonic", "loose"]),
        new("ungrouped", null, ["loose"]),
        new("starred", null, ["sonic"]),
        new("character", "character", ["sonic"])
    ];

    [Fact]
    public void RevealKey_KeepsNamedGroupWhenHeaderIsSelected()
    {
        var key = TagBoardExpand.RevealKey("character", "character", Board());
        Assert.Equal("character", key);
    }

    [Fact]
    public void RevealKey_KeepsNamedGroupWhenChipIsSelected()
    {
        var key = TagBoardExpand.RevealKey("character", "sonic", Board());
        Assert.Equal("character", key);
    }

    [Fact]
    public void RevealKey_OpensUngroupedForNewLooseTag()
    {
        var key = TagBoardExpand.RevealKey("character", "loose", Board());
        Assert.Equal("ungrouped", key);
    }

    [Fact]
    public void RevealKey_KeepsAllWhenCurrentAllShowsTag()
    {
        var key = TagBoardExpand.RevealKey("all", "sonic", Board());
        Assert.Equal("all", key);
    }

    [Fact]
    public void RevealKey_MissingCurrent_UsesNamedGroup()
    {
        var key = TagBoardExpand.RevealKey("missing", "sonic", Board());
        Assert.Equal("character", key);
    }
}
