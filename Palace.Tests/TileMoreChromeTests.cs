using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TileMoreChromeTests
{
    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, false, true, false, true)]
    [InlineData(false, false, false, true, true)]
    [InlineData(true, true, true, true, true)]
    public void ShouldShow_MatchesHoverFocusSelectedFlyout(
        bool pointerOver,
        bool focused,
        bool selected,
        bool flyoutOpen,
        bool expected) =>
        Assert.Equal(
            expected,
            TileMoreChrome.ShouldShow(pointerOver, focused, selected, flyoutOpen));

    [Fact]
    public void StateName_And_TabStop_FollowShow()
    {
        Assert.Equal(TileMoreChrome.HiddenState, TileMoreChrome.StateName(show: false));
        Assert.Equal(TileMoreChrome.ShownState, TileMoreChrome.StateName(show: true));
        Assert.False(TileMoreChrome.IsTabStop(show: false));
        Assert.True(TileMoreChrome.IsTabStop(show: true));
    }
}
