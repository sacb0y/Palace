using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TileMoreChromeTests
{
    [Theory]
    [InlineData(false, false, false, false, false, 0, false)]
    [InlineData(true, false, false, false, false, 0, true)]
    [InlineData(false, true, false, false, false, 0, true)]
    [InlineData(false, false, false, true, false, 0, true)]
    [InlineData(true, true, true, true, true, 4, true)]
    // Browse single-select: selected alone shows ⋮.
    [InlineData(false, false, true, false, false, 1, true)]
    // Select mode: selected alone never shows ⋮ (hover/focus/flyout only).
    [InlineData(false, false, true, false, true, 1, false)]
    [InlineData(false, false, true, false, true, 4, false)]
    [InlineData(true, false, true, false, true, 4, true)]
    // After ExitSelectMode with multi selection kept: no sticky ⋮ from selected.
    [InlineData(false, false, true, false, false, 4, false)]
    [InlineData(false, false, true, false, false, 2, false)]
    [InlineData(false, false, true, false, false, 0, false)]
    public void ShouldShow_MatchesHoverFocusSelectedFlyoutSelectMode(
        bool pointerOver,
        bool focused,
        bool selected,
        bool flyoutOpen,
        bool selectMode,
        int selectionCount,
        bool expected) =>
        Assert.Equal(
            expected,
            TileMoreChrome.ShouldShow(
                pointerOver,
                focused,
                selected,
                flyoutOpen,
                selectMode,
                selectionCount));

    [Theory]
    [InlineData(true, false, 1, true)]
    [InlineData(true, true, 1, false)]
    [InlineData(true, false, 2, false)]
    [InlineData(false, false, 1, false)]
    public void CountsSelected_OnlyBrowseSingleSelect(
        bool selected,
        bool selectMode,
        int selectionCount,
        bool expected) =>
        Assert.Equal(
            expected,
            TileMoreChrome.CountsSelected(selected, selectMode, selectionCount));

    [Fact]
    public void Opacity_And_TabStop_FollowShow()
    {
        Assert.Equal(0, TileMoreChrome.Opacity(show: false));
        Assert.Equal(1, TileMoreChrome.Opacity(show: true));
        Assert.False(TileMoreChrome.IsTabStop(show: false));
        Assert.True(TileMoreChrome.IsTabStop(show: true));
    }
}
