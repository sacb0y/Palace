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
