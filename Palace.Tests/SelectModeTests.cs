using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SelectModeTests
{
    [Fact]
    public void ClickTogglesTile_FollowsMode()
    {
        Assert.True(SelectMode.ClickTogglesTile(true));
        Assert.False(SelectMode.ClickTogglesTile(false));
    }

    [Theory]
    [InlineData(false, 0, "")]
    [InlineData(true, 0, "Click tiles to select")]
    [InlineData(true, 1, "1 selected")]
    [InlineData(true, 5, "5 selected")]
    [InlineData(false, 3, "3 selected")]
    [InlineData(true, -2, "Click tiles to select")]
    public void Summary_ShowsCountOrHint(bool mode, int count, string expected) =>
        Assert.Equal(expected, SelectMode.Summary(mode, count));
}
