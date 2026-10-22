using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SelectModeTests
{
    [Fact]
    public void ClickTogglesTile_WhileSelectModeIsOn()
    {
        Assert.False(SelectMode.ClickTogglesTile(false));
        Assert.True(SelectMode.ClickTogglesTile(true));
    }

    [Fact]
    public void EnterFromHold_WhenOff_NotOverlay_NotHeader()
    {
        Assert.True(SelectMode.EnterFromHold(false, false, false));
        Assert.False(SelectMode.EnterFromHold(true, false, false));
        Assert.False(SelectMode.EnterFromHold(false, true, false));
        Assert.False(SelectMode.EnterFromHold(false, false, true));
        Assert.False(SelectMode.ViewerHoldTogglesSelection(true));
        Assert.True(SelectMode.ViewerHoldOpensContext(true, reviewMode: false));
        Assert.False(SelectMode.ViewerHoldOpensContext(true, reviewMode: true));
    }

    [Theory]
    [InlineData(false, 0, "")]
    [InlineData(true, 0, "Click or drag tiles to select")]
    [InlineData(true, 1, "1 selected")]
    [InlineData(true, 5, "5 selected")]
    [InlineData(false, 3, "3 selected")]
    [InlineData(true, -2, "Click or drag tiles to select")]
    public void Summary_ShowsCountOrHint(bool mode, int count, string expected) =>
        Assert.Equal(expected, SelectMode.Summary(mode, count));

    [Fact]
    public void Summary_IncludesCollapsedSectionCount()
    {
        Assert.Equal(
            "42 selected · 30 in collapsed sections",
            SelectMode.Summary(true, 42, 30));
        Assert.Equal(" · 30 in collapsed sections", SelectMode.CollapsedClause(30));
        Assert.Equal("", SelectMode.CollapsedClause(0));
    }

    [Fact]
    public void ExitsSelectModeOnEscape_WhenSelectOnAndOverlayClosed()
    {
        Assert.True(SelectMode.ExitsSelectModeOnEscape(true, false));
        Assert.False(SelectMode.ExitsSelectModeOnEscape(true, true));
        Assert.False(SelectMode.ExitsSelectModeOnEscape(false, false));
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(false, 3, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, true)]
    [InlineData(true, 5, true)]
    [InlineData(true, -1, false)]
    public void ShowBatchTagCta_NeedsSelectModeAndTiles(bool mode, int count, bool expected) =>
        Assert.Equal(expected, SelectMode.ShowBatchTagCta(mode, count));
}
