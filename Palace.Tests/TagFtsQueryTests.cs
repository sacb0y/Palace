using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>T4 follow-up N1/N4: quote FTS terms; multi-word alias spans.</summary>
public sealed class TagFtsQueryTests
{
    [Theory]
    [InlineData("c++", "\"c++\"*")]
    [InlineData("NOT", "\"NOT\"*")]
    [InlineData("foo-bar", "\"foo-bar\"*")]
    [InlineData("a:b", "\"a:b\"*")]
    [InlineData("x(y)", "\"x(y)\"*")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"*")]
    public void PrefixTerm_QuotesSpecialAndKeywords(string raw, string expected) =>
        Assert.Equal(expected, TagFtsQuery.PrefixTerm(raw));

    [Fact]
    public void Build_EscapesOperatorTokens_WithoutThrowingShape()
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        var q = TagFtsQuery.Build("NOT foo-bar c++", empty, empty, empty);
        Assert.Equal("\"NOT\"* AND \"foo\"* AND \"bar\"* AND \"c\"*", q);
    }

    [Fact]
    public void Build_MultiWordAlias_OrsIdealAsOneClause()
    {
        var alias = new Dictionary<string, string>(StringComparer.Ordinal) { ["a1"] = "i1" };
        var idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["blue doggy"] = "a1",
            ["large canine"] = "i1"
        };
        var nameById = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a1"] = "blue doggy",
            ["i1"] = "large canine"
        };

        var q = TagFtsQuery.Build("blue doggy", alias, idByName, nameById);
        Assert.Equal("(\"blue doggy\"* OR \"large canine\"*)", q);
    }

    [Fact]
    public void Build_TokenSequenceMatch_WorksWhenNameHasHyphen()
    {
        // "foo-bar" tokenizes to foo, bar — still matches the tag via token equality.
        var alias = new Dictionary<string, string>(StringComparer.Ordinal) { ["a1"] = "i1" };
        var idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ideal"] = "i1"
        };
        var nameById = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a1"] = "foo-bar",
            ["i1"] = "ideal"
        };

        var q = TagFtsQuery.Build("foo-bar", alias, idByName, nameById);
        Assert.Equal("(\"foo bar\"* OR \"ideal\"*)", q);
    }

    [Fact]
    public void Build_ParentTerm_OrsImplyingChildren()
    {
        var emptyAlias = new Dictionary<string, string>(StringComparer.Ordinal);
        var idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["canine"] = "p1",
            ["wolf"] = "c1"
        };
        var nameById = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["p1"] = "canine",
            ["c1"] = "wolf"
        };
        var implying = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["p1"] = new[] { "c1" }
        };

        var q = TagFtsQuery.Build("canine", emptyAlias, idByName, nameById, implying);
        Assert.Equal("(\"canine\"* OR \"wolf\"*)", q);
    }
}
