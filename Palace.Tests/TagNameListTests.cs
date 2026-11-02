using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagNameListTests
{
    [Fact]
    public void Split_Empty_ReturnsNone()
    {
        Assert.Empty(TagNameList.Split(null));
        Assert.Empty(TagNameList.Split(""));
        Assert.Empty(TagNameList.Split("  , , \n "));
    }

    [Fact]
    public void Split_CommaAndNewline_TrimsAndKeepsOrder()
    {
        var names = TagNameList.Split("character, clothing\nshot,");
        Assert.Equal(["character", "clothing", "shot"], names);
    }

    [Fact]
    public void Split_Semicolon_TrimsAndKeepsOrder()
    {
        var names = TagNameList.Split("character; clothing;shot;");
        Assert.Equal(["character", "clothing", "shot"], names);
    }

    [Fact]
    public void Split_FullWidthCommasAndSemicolon_TrimsAndKeepsOrder()
    {
        // U+FF0C ，  U+3001 、  U+FF1B ；
        var names = TagNameList.Split("character，clothing、shot；pose");
        Assert.Equal(["character", "clothing", "shot", "pose"], names);
    }

    [Fact]
    public void Split_MixedSeparators_DedupesAcrossKinds()
    {
        var names = TagNameList.Split("Shot; shot，SHOT、closeup\npose");
        Assert.Equal(["Shot", "closeup", "pose"], names);
    }

    [Fact]
    public void Split_DedupesCaseInsensitive_KeepsFirstSpelling()
    {
        var names = TagNameList.Split("Shot, shot, SHOT, closeup");
        Assert.Equal(["Shot", "closeup"], names);
    }

    [Fact]
    public void Split_SingleName_Unchanged()
    {
        Assert.Equal(["Sonic"], TagNameList.Split("Sonic"));
        Assert.Equal(["Sonic"], TagNameList.Split("  Sonic  "));
    }
}
