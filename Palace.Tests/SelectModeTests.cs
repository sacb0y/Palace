using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SelectModeTests
{
    [Fact]
    public void ClickTogglesTile_IsTheOnlyMultiSelectPath()
    {
        Assert.False(SelectMode.ClickTogglesTile(false));
        Assert.True(SelectMode.ClickTogglesTile(true));
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
