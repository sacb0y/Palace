using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class AssetDragTests
{
    [Theory]
    [InlineData(true, false, false, false, false, true)]   // mouse, browse mode
    [InlineData(false, false, false, false, false, false)] // touch: hold selects instead
    [InlineData(true, true, false, false, false, false)]   // select mode, unselected tile: range select
    [InlineData(true, true, true, false, false, true)]     // select mode, selected tile: drag selection
    [InlineData(true, false, false, true, false, false)]   // folder header
    [InlineData(true, false, false, false, true, false)]   // overlay open
    public void CanStart(bool mouse, bool selectMode, bool selected, bool header, bool overlay, bool expected) =>
        Assert.Equal(expected, AssetDrag.CanStart(mouse, selectMode, selected, header, overlay));

    [Fact]
    public void Ids_take_the_selection_only_when_the_tile_is_in_it()
    {
        Assert.Equal(["x"], AssetDrag.Ids("x", false, ["a", "b"]));
        Assert.Equal(["a", "b"], AssetDrag.Ids("a", true, ["a", "b", "b", ""]));
        Assert.Equal(["x", "a"], AssetDrag.Ids("x", true, ["a"]));
        Assert.Empty(AssetDrag.Ids("", false, []));
    }

    [Fact]
    public void Round_trips_and_ignores_junk()
    {
        Assert.Equal(["a", "b"], AssetDrag.Parse(AssetDrag.Serialize(["a", "b", "a"])));
        Assert.Empty(AssetDrag.Parse(42));
        Assert.Empty(AssetDrag.Parse(null));
        Assert.Equal("Move 3 files to Shots", AssetDrag.Caption(3, "Shots"));
        Assert.Equal("Move to Shots", AssetDrag.Caption(1, "Shots"));
    }

    [Fact]
    public void Range_select_yields_only_for_mouse_on_selected_tiles()
    {
        Assert.True(AssetDrag.YieldsRangeSelect(true, true));
        Assert.False(AssetDrag.YieldsRangeSelect(true, false));
        Assert.False(AssetDrag.YieldsRangeSelect(false, true));
    }
}
