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
    public void Summary_KeepsCountAfterSelectModeExit()
    {
        // Esc exits select mode but keeps the selection — bar still shows N selected.
        Assert.Equal("7 selected", SelectMode.Summary(selectMode: false, selectedCount: 7));
        Assert.Equal(
            "7 selected · 2 in collapsed sections",
            SelectMode.Summary(selectMode: false, selectedCount: 7, collapsedSelectedCount: 2));
    }

    [Fact]
    public void CountCollapsedAmongTargets_UsesTargetIdsNotWholeSelection()
    {
        var a = new Item("a");
        var b = new Item("b");
        var c = new Item("c");
        var ordered = new List<Item> { a, b, c };
        var displayed = new HashSet<Item> { a }; // b,c hidden (collapsed)
        // Confirm targets are only b — not the whole selection {a,b,c}.
        Assert.Equal(
            1,
            SelectMode.CountCollapsedAmongTargets(
                ordered,
                _ => false,
                item => item.Id,
                displayed,
                ["b"]));
        Assert.Equal(
            2,
            SelectMode.CountCollapsedAmongTargets(
                ordered,
                _ => false,
                item => item.Id,
                displayed,
                ["a", "b", "c"]));
    }

    [Fact]
    public void ExitsSelectModeOnEscape_WhenSelectOnAndOverlayClosed()
    {
        Assert.True(SelectMode.ExitsSelectModeOnEscape(true, false));
        Assert.False(SelectMode.ExitsSelectModeOnEscape(true, true));
        Assert.False(SelectMode.ExitsSelectModeOnEscape(false, false));
    }

    private sealed class Item(string id)
    {
        public string Id { get; } = id;
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
