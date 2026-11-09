using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ImpliedNearestTests
{
    private static readonly (string TagId, string ImpliedTagId)[] Edges =
    [
        ("knight", "character"),
        ("character", "figure"),
        ("landscape", "environment"),
        ("vista", "environment")
    ];

    [Fact]
    public void NearestSource_PicksDirectParent()
    {
        var applied = new HashSet<string>(StringComparer.Ordinal) { "knight", "sketch" };
        Assert.Equal("knight", ImpliedNearest.NearestSourceId("character", applied, Edges));
        Assert.Equal("knight", ImpliedNearest.NearestSourceId("figure", applied, Edges));
    }

    [Fact]
    public void NearestSource_AmongSeveral_PicksOrdinalTieBreak()
    {
        // landscape and vista both imply environment at distance 1 → Ordinal "landscape".
        var applied = new HashSet<string>(StringComparer.Ordinal) { "vista", "landscape" };
        Assert.Equal("landscape", ImpliedNearest.NearestSourceId("environment", applied, Edges));
    }

    [Fact]
    public void NearestSource_NullWhenNoneApply()
    {
        var applied = new HashSet<string>(StringComparer.Ordinal) { "sketch" };
        Assert.Null(ImpliedNearest.NearestSourceId("figure", applied, Edges));
    }

    [Fact]
    public void NearestSource_CycleDoesNotHang_AndOrdinalTieBreakStillHolds()
    {
        (string TagId, string ImpliedTagId)[] cyclic =
        [
            ("a", "b"),
            ("b", "a"),
            ("vista", "environment"),
            ("landscape", "environment")
        ];

        // Pure cycle with no applied source — terminates, returns null.
        Assert.Null(ImpliedNearest.NearestSourceId(
            "a",
            new HashSet<string>(StringComparer.Ordinal) { "z" },
            cyclic));

        // Same-distance applied sources still Ordinal-stable with a cycle present.
        Assert.Equal(
            "landscape",
            ImpliedNearest.NearestSourceId(
                "environment",
                new HashSet<string>(StringComparer.Ordinal) { "vista", "landscape" },
                cyclic));
    }

    [Fact]
    public void BuildReverse_ReusableAcrossChips()
    {
        var reverse = ImpliedNearest.BuildReverse(Edges);
        var applied = new HashSet<string>(StringComparer.Ordinal) { "knight" };
        Assert.Equal("knight", ImpliedNearest.NearestSourceId("character", applied, reverse));
        Assert.Equal("knight", ImpliedNearest.NearestSourceId("figure", applied, reverse));
    }

    [Fact]
    public void IsAlsoImplied_WhenAnotherAppliedImpliesIt()
    {
        var applied = new[] { "knight", "character" };
        Assert.True(ImpliedNearest.IsAlsoImplied("character", applied, Edges));
        Assert.False(ImpliedNearest.IsAlsoImplied("knight", applied, Edges));
        Assert.False(ImpliedNearest.IsAlsoImplied("sketch", applied, Edges));
    }

    [Theory]
    [InlineData(null, "Implied by tag rules")]
    [InlineData("", "Implied by tag rules")]
    [InlineData("character", "Implied by character")]
    public void ImpliedTooltip_Copy(string? name, string expected) =>
        Assert.Equal(expected, ImpliedNearest.ImpliedTooltip(name));

    [Fact]
    public void AlsoImpliedTooltip_AndAliasLine()
    {
        Assert.Equal(
            "Applied · also implied by knight",
            ImpliedNearest.AlsoImpliedTooltip("knight"));
        Assert.Equal(
            "also: scenery, vista",
            ImpliedNearest.AliasAlsoLine(["vista", "scenery", " vista "]));
        Assert.Equal("", ImpliedNearest.AliasAlsoLine(null));
    }
}
