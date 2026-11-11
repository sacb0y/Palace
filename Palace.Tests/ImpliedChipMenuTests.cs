using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ImpliedChipMenuTests
{
    [Theory]
    [InlineData(null, "Implied by tag rules")]
    [InlineData("", "Implied by tag rules")]
    [InlineData("character", "Implied by character")]
    public void HeaderText_MatchesNearestTooltip(string? source, string expected) =>
        Assert.Equal(expected, ImpliedChipMenu.HeaderText(source));

    [Theory]
    [InlineData(null, "Filter by tag")]
    [InlineData("", "Filter by tag")]
    [InlineData("figure", "Filter by figure")]
    public void FilterByLabel_UsesImpliedName(string? name, string expected) =>
        Assert.Equal(expected, ImpliedChipMenu.FilterByLabel(name));

    [Fact]
    public void Labels_MatchMockup()
    {
        Assert.Equal("Edit implication rule…", ImpliedChipMenu.EditRuleLabel);
        Assert.Equal("Add as manual tag", ImpliedChipMenu.AddAsManualLabel);
        Assert.Equal("Go to tag", ImpliedChipMenu.GoToTagLabel);
    }

    [Fact]
    public void CanEditRule_RequiresSourceId()
    {
        Assert.False(ImpliedChipMenu.CanEditRule(null));
        Assert.False(ImpliedChipMenu.CanEditRule(""));
        Assert.True(ImpliedChipMenu.CanEditRule("character"));
    }

    [Fact]
    public void CanActOnImpliedTag_RequiresTagId()
    {
        Assert.False(ImpliedChipMenu.CanActOnImpliedTag(null));
        Assert.False(ImpliedChipMenu.CanActOnImpliedTag("  "));
        Assert.True(ImpliedChipMenu.CanActOnImpliedTag("figure"));
    }
}
