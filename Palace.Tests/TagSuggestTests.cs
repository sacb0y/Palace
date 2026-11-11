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

    [Fact]
    public void CreateSuggestText_SortsAfterNameMatches()
    {
        var create = TagSuggest.CreateSuggestText("Create tag “vis”");
        Assert.StartsWith("\uFFFF", create, StringComparison.Ordinal);
        // Exact query "vis" as TextMemberPath would sort before "vista"; Create must not.
        Assert.True(string.CompareOrdinal(create, "vis") > 0);
        Assert.True(string.CompareOrdinal(create, "vista") > 0);
        Assert.True(string.CompareOrdinal(create, "visual development") > 0);
    }

    [Fact]
    public void WithCreateLast_AppendsCreateAndDropsDupes()
    {
        var ordered = TagSuggest.WithCreateLast(
            [
                (Name: "vista", Create: false),
                (Name: "bogus-create", Create: true),
                (Name: "visor", Create: false),
            ],
            (Name: "vis", Create: true),
            r => r.Create);

        Assert.Equal(["vista", "visor", "vis"], ordered.Select(r => r.Name).ToArray());
        Assert.True(ordered[^1].Create);
        Assert.DoesNotContain(ordered.Take(ordered.Count - 1), r => r.Create);
        Assert.True(TagSuggest.IsCreateLast(ordered, r => r.Create));
    }

    [Fact]
    public void IsCreateLast_RejectsCreateBeforeMatches()
    {
        Assert.False(TagSuggest.IsCreateLast(
            [
                (Name: "Create", Create: true),
                (Name: "landscape", Create: false),
            ],
            r => r.Create));

        // Simulated ASB "popup above" reverse of WithCreateLast — must fail the invariant.
        var source = TagSuggest.WithCreateLast(
            [(Name: "landscape", Create: false), (Name: "vista", Create: false)],
            (Name: "land", Create: true),
            r => r.Create);
        var reversed = source.Reverse().ToList();
        Assert.True(TagSuggest.IsCreateLast(source, r => r.Create));
        Assert.False(TagSuggest.IsCreateLast(reversed, r => r.Create));
    }

    [Fact]
    public void IsCreateLast_EmptyOrMatchesOnly_Passes()
    {
        Assert.True(TagSuggest.IsCreateLast(Array.Empty<(string Name, bool Create)>(), r => r.Create));
        Assert.True(TagSuggest.IsCreateLast(
            [(Name: "landscape", Create: false)],
            r => r.Create));
    }
}
