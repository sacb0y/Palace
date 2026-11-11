using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class TagFilterStripTests
{
    [Fact]
    public void MatchLabel_AllAnyNone()
    {
        Assert.Equal("Match all", TagFilterStrip.MatchLabel(TagFilterMode.All));
        Assert.Equal("Match any", TagFilterStrip.MatchLabel(TagFilterMode.Any));
        Assert.Equal("Match none", TagFilterStrip.MatchLabel(TagFilterMode.None));
    }

    [Fact]
    public void StripLead_Pluralizes()
    {
        Assert.Equal("1 file with", TagFilterStrip.StripLead(1));
        Assert.Equal("41 files with", TagFilterStrip.StripLead(41));
        Assert.Equal("0 files with", TagFilterStrip.StripLead(0));
    }

    [Fact]
    public void StatusLine_MatchAll_UsesAnd()
    {
        Assert.Equal(
            "41 of 412 files · landscape AND wip",
            TagFilterStrip.StatusLine(41, 412, ["landscape", "wip"], TagFilterMode.All));
    }

    [Fact]
    public void StatusLine_MatchAny_UsesOr()
    {
        Assert.Equal(
            "10 of 100 files · a OR b",
            TagFilterStrip.StatusLine(10, 100, ["a", "b"], TagFilterMode.Any));
    }

    [Fact]
    public void StatusLine_EmptyNames_OmitsJoiner()
    {
        Assert.Equal("0 of 5 files", TagFilterStrip.StatusLine(0, 5, [], TagFilterMode.All));
    }
}
