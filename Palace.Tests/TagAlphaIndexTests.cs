using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagAlphaIndexTests
{
    [Fact]
    public void LetterOf_UsesInitialLetterOrHash()
    {
        Assert.Equal('S', TagAlphaIndex.LetterOf("sonic"));
        Assert.Equal('S', TagAlphaIndex.LetterOf(" Sonic"));
        Assert.Equal('#', TagAlphaIndex.LetterOf("12-tone"));
        Assert.Equal('#', TagAlphaIndex.LetterOf(""));
        Assert.Equal('#', TagAlphaIndex.LetterOf(null));
    }

    [Fact]
    public void GroupByLetter_SortsAndBuckets()
    {
        var names = new[] { "Tails", "sonic", "12-tone", "Amy", "Shot" };
        var groups = TagAlphaIndex.GroupByLetter(names, n => n);

        Assert.Equal(["A", "S", "T", "#"], groups.Select(g => g.Letter).ToArray());
        Assert.Equal("A (1)", groups[0].Header);
        Assert.Equal(["Amy"], groups[0].Items);
        Assert.Equal(["Shot", "sonic"], groups[1].Items);
        Assert.Equal(["Tails"], groups[2].Items);
        Assert.Equal(["12-tone"], groups[3].Items);
    }
}
