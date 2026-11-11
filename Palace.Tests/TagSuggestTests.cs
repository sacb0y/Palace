using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagSuggestTests
{
    [Fact]
    public void Rank_PutsPrefixMatchBeforeSubstring()
    {
        var ranked = TagSuggest.Rank(
        [
            new("land", "Landscape", "Landscape (Subject)"),
            new("scarf", "Scarf", "Scarf (Clothing)"),
            new("scale", "Scale", "Scale"),
        ], "sca");

        // Prefix ties (Scarf/Scale) sort by name; Landscape is substring-only and comes last.
        Assert.Equal(["Scale", "Scarf", "Landscape"], ranked.Select(c => c.Name).ToArray());
        Assert.Equal(1, TagSuggest.Score("Scarf", "Scarf (Clothing)", "sca"));
        Assert.Equal(2, TagSuggest.Score("Landscape", "Landscape (Subject)", "sca"));
    }

    [Fact]
    public void Rank_ExcludesAlreadyAssignedTags()
    {
        var ranked = TagSuggest.Rank(
        [
            new("scarf", "Scarf", "Scarf"),
            new("scale", "Scale", "Scale"),
        ], "sca", excludeTagIds: ["scarf"]);

        Assert.Equal(["Scale"], ranked.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void Rank_EmptyQuery_ReturnsNone() =>
        Assert.Empty(TagSuggest.Rank([new("a", "Scarf", "Scarf")], "  "));

    [Fact]
    public void SplitMatch_PrefixAndMid()
    {
        var prefix = TagSuggest.SplitMatch("visual development", "vis");
        Assert.Equal("", prefix.Before);
        Assert.Equal("vis", prefix.Match);
        Assert.Equal("ual development", prefix.After);

        var mid = TagSuggest.SplitMatch("revision needed", "vis");
        Assert.Equal("re", mid.Before);
        Assert.Equal("vis", mid.Match);
        Assert.Equal("ion needed", mid.After);
    }

    [Fact]
    public void SplitMatch_Missing_KeepsWholeName()
    {
        var span = TagSuggest.SplitMatch("landscape", "zzz");
        Assert.Equal("", span.Before);
        Assert.Equal("landscape", span.Match);
        Assert.Equal("", span.After);
    }
}
