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

    [Fact]
    public void OpensBatchContextFromHold_OnlyInSelectMode()
    {
        Assert.False(SelectMode.OpensBatchContextFromHold(false, false, false));
        Assert.True(SelectMode.OpensBatchContextFromHold(true, false, false));
        Assert.False(SelectMode.OpensBatchContextFromHold(true, true, false));
        Assert.False(SelectMode.OpensBatchContextFromHold(true, false, true));
    }

    [Fact]
    public void AddsPressedToSelection_NeverReplacesInSelectMode()
    {
        Assert.True(SelectMode.AddsPressedToSelection(true, alreadySelected: false));
        Assert.False(SelectMode.AddsPressedToSelection(true, alreadySelected: true));
        Assert.False(SelectMode.AddsPressedToSelection(false, alreadySelected: false));
        Assert.True(SelectMode.ReplacesSelectionForContext(false));
        Assert.False(SelectMode.ReplacesSelectionForContext(true));
    }

    [Theory]
    [InlineData(0, "0 selected")]
    [InlineData(1, "1 selected")]
    [InlineData(5, "5 selected")]
    [InlineData(-1, "0 selected")]
    public void BatchMenuHeader_ShowsCount(int count, string expected) =>
        Assert.Equal(expected, SelectMode.BatchMenuHeader(count));

    [Fact]
    public void EscAndClear_ExitSelectMode()
    {
        Assert.True(SelectMode.ExitsSelectModeOnEscape(true, overlayOpen: false));
        Assert.False(SelectMode.ExitsSelectModeOnEscape(true, overlayOpen: true));
        Assert.False(SelectMode.ExitsSelectModeOnEscape(false, overlayOpen: false));
        Assert.True(SelectMode.ExitsSelectModeOnClear(true));
        Assert.False(SelectMode.ExitsSelectModeOnClear(false));
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
