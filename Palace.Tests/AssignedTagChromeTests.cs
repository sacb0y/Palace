using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class AssignedTagChromeTests
{
    [Theory]
    [InlineData(false, TagSource.Manual, 1.0)]
    [InlineData(true, TagSource.Manual, 0.75)]
    [InlineData(false, TagSource.Implied, 1.0)]
    [InlineData(true, TagSource.Implied, 1.0)]
    [InlineData(false, TagSource.Prompt, 1.0)]
    public void Opacity_MutesPartialManual_ImpliedFull(bool partial, TagSource source, double expected) =>
        Assert.Equal(expected, AssignedTagChrome.Opacity(partial, source));

    [Theory]
    [InlineData("Implied", "", "Implied")]
    [InlineData("Manual", "", "Manual")]
    [InlineData("Implied", "1 of 2", "Implied (1 of 2)")]
    [InlineData("Mixed", "1 of 3", "Mixed (1 of 3)")]
    [InlineData("", "1 of 2", "1 of 2")]
    [InlineData("  Manual  ", "  ", "Manual")]
    [InlineData(null, null, "")]
    public void Tooltip_SourceAndOptionalCount(string? source, string? count, string expected) =>
        Assert.Equal(expected, AssignedTagChrome.Tooltip(source, count));

    [Fact]
    public void ChipTooltip_ImpliedUsesNearest_NoRightClickPromise()
    {
        var tip = AssignedTagChrome.ChipTooltip(
            TagSource.Implied,
            isAlsoImplied: false,
            nearestImpliedByName: "character",
            aliasNames: null,
            isPartial: false,
            countLabel: null,
            tagName: "figure");
        Assert.Equal("Implied by character", tip);
        Assert.DoesNotContain("Right-click", tip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChipTooltip_AlsoImpliedAndAliases()
    {
        var tip = AssignedTagChrome.ChipTooltip(
            TagSource.Manual,
            isAlsoImplied: true,
            nearestImpliedByName: "knight",
            aliasNames: ["scenery", "vista"],
            isPartial: false,
            countLabel: null,
            tagName: "character");
        Assert.Contains("Applied · also implied by knight", tip, StringComparison.Ordinal);
        Assert.Contains("also: scenery, vista", tip, StringComparison.Ordinal);
    }

    [Fact]
    public void ChipTooltip_ManualRemove()
    {
        Assert.Equal(
            "Remove sketch",
            AssignedTagChrome.ChipTooltip(
                TagSource.Manual,
                isAlsoImplied: false,
                nearestImpliedByName: null,
                aliasNames: null,
                isPartial: false,
                countLabel: null,
                tagName: "sketch"));
    }
}
