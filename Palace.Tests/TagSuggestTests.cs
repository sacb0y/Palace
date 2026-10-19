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
}
