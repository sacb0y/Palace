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
    public void NearestSource_AmongSeveral_PicksShortest()
    {
        // landscape and vista both imply environment; both applied → either is distance 1.
        var applied = new HashSet<string>(StringComparer.Ordinal) { "landscape", "vista" };
        var nearest = ImpliedNearest.NearestSourceId("environment", applied, Edges);
        Assert.True(nearest is "landscape" or "vista");
    }

    [Fact]
    public void NearestSource_NullWhenNoneApply()
    {
        var applied = new HashSet<string>(StringComparer.Ordinal) { "sketch" };
        Assert.Null(ImpliedNearest.NearestSourceId("figure", applied, Edges));
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

    [Fact]
    public void ImpliedTooltipWithEditHint_HasSecondLine()
    {
        var tip = ImpliedNearest.ImpliedTooltipWithEditHint("character");
        Assert.Contains("Implied by character", tip, StringComparison.Ordinal);
        Assert.Contains("Right-click to edit the rule", tip, StringComparison.Ordinal);
    }
}
