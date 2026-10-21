using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class SelectCommandBarTests
{
    [Theory]
    [InlineData(false, "Select")]
    [InlineData(true, "Done")]
    public void ToggleLabel_DoneWhileSelecting(bool mode, string expected) =>
        Assert.Equal(expected, SelectCommandBar.ToggleLabel(mode));

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    public void ToolbarAddToRoom_DimmedInSelectMode(bool selectMode, bool hasSelection, bool expected) =>
        Assert.Equal(expected, SelectCommandBar.ToolbarAddToRoomEnabled(selectMode, hasSelection));

    [Fact]
    public void BarAddToRoom_DisabledUntilRoomsReady()
    {
        Assert.False(SelectCommandBar.RoomsBatchEnabled);
        Assert.False(SelectCommandBar.BarAddToRoomEnabled(hasSelection: true));
        Assert.False(SelectCommandBar.BarAddToRoomEnabled(hasSelection: false));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void MultiSelectPreview_NeedsTwoOrMore(int count, bool expected) =>
        Assert.Equal(expected, SelectCommandBar.ShowMultiSelectPreview(count));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(9, 5)]
    public void StackedThumbs_CapsAtFive(int count, int expected) =>
        Assert.Equal(expected, SelectCommandBar.StackedThumbCount(count));

    [Fact]
    public void Overflow_FoldsFromRight()
    {
        var full = SelectCommandBar.ResolveOverflow(1000);
        Assert.True(full.ShowMove && full.ShowAddToRoom && full.ShowTagSelected);
        Assert.False(SelectCommandBar.OverflowHasFoldedActions(full));

        var noMove = SelectCommandBar.ResolveOverflow(850);
        Assert.False(noMove.ShowMove);
        Assert.True(noMove.ShowAddToRoom && noMove.ShowTagSelected);

        var noRoom = SelectCommandBar.ResolveOverflow(750);
        Assert.False(noRoom.ShowMove);
        Assert.False(noRoom.ShowAddToRoom);
        Assert.True(noRoom.ShowTagSelected);

        var tight = SelectCommandBar.ResolveOverflow(500);
        Assert.False(tight.ShowMove || tight.ShowAddToRoom || tight.ShowTagSelected);
        Assert.True(SelectCommandBar.OverflowHasFoldedActions(tight));
    }

    [Fact]
    public void MultiSelectDetail_TypeCountsAndSize()
    {
        var kinds = new[] { AssetKind.Image, AssetKind.Image, AssetKind.Gif, AssetKind.Video };
        var sizes = new long?[] { 1024, 2048, 512, 1024 * 1024 };
        var detail = SelectCommandBar.MultiSelectDetail(kinds, sizes);
        Assert.Equal("3 images · 1 video · 1 MB", detail);
    }

    [Fact]
    public void Rating_MixedWhenValuesDiffer()
    {
        Assert.False(SelectCommandBar.RatingIsMixed([3]));
        Assert.False(SelectCommandBar.RatingIsMixed([2, 2, 2]));
        Assert.True(SelectCommandBar.RatingIsMixed([2, 3]));
        Assert.True(SelectCommandBar.RatingIsMixed([null, 1]));
        Assert.Equal(0, SelectCommandBar.RatingDisplayValue([2, 5]));
        Assert.Equal(4, SelectCommandBar.RatingDisplayValue([4, 4]));
        Assert.Equal("Mixed", SelectCommandBar.RatingCaption(true));
        Assert.Equal("Rating", SelectCommandBar.RatingCaption(false));
    }

    [Fact]
    public void DeleteConfirm_CopyMatchesSpec()
    {
        Assert.Equal("Delete 1 item?", SelectCommandBar.DeleteTitle(1));
        Assert.Equal("Delete 4 items?", SelectCommandBar.DeleteTitle(4));
        Assert.Contains("Recycle Bin", SelectCommandBar.DeleteBody(4));
        Assert.Contains("leave the catalog", SelectCommandBar.DeleteBody(4));
        Assert.Equal("Delete", SelectCommandBar.DeletePrimary);
    }

    [Fact]
    public void TargetHeight_Is44() =>
        Assert.Equal(44, SelectCommandBar.TargetHeightDip);
}
