using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class LibraryColumnLayoutTests
{
    [Fact]
    public void Allocate_WideWindow_KeepsDefaults()
    {
        // Comfortable: tree 260 + mosaic ≥560 + preview 340 + gaps.
        var w = LibraryColumnLayout.Allocate(1400);
        Assert.Equal(260, w.Tree);
        Assert.Equal(340, w.Preview);
        Assert.False(w.PreviewHidden);
        Assert.True(w.Mosaic >= LibraryColumnLayout.MosaicMinWidth);
        Assert.Equal(
            1400 - LibraryColumnLayout.GapsWidth - 260 - 340,
            w.Mosaic,
            0.01);
    }

    [Fact]
    public void Allocate_ShrinksTreeBeforePreview()
    {
        // Just under defaults: need a little from the tree, preview stays 340.
        var defaults = LibraryColumnLayout.TreeDefaultWidth
            + LibraryColumnLayout.MosaicMinWidth
            + LibraryColumnLayout.PreviewDefaultWidth
            + LibraryColumnLayout.GapsWidth;
        var w = LibraryColumnLayout.Allocate(defaults - 40);
        Assert.True(w.Tree < LibraryColumnLayout.TreeDefaultWidth);
        Assert.True(w.Tree >= LibraryColumnLayout.TreeMinWidth);
        Assert.Equal(LibraryColumnLayout.PreviewDefaultWidth, w.Preview);
        Assert.False(w.PreviewHidden);
        Assert.Equal(LibraryColumnLayout.MosaicMinWidth, w.Mosaic, 0.01);
    }

    [Fact]
    public void Allocate_ShrinksPreviewAfterTreeMin()
    {
        var atTreeMin = LibraryColumnLayout.TreeMinWidth
            + LibraryColumnLayout.MosaicMinWidth
            + LibraryColumnLayout.PreviewDefaultWidth
            + LibraryColumnLayout.GapsWidth;
        var w = LibraryColumnLayout.Allocate(atTreeMin - 30);
        Assert.Equal(LibraryColumnLayout.TreeMinWidth, w.Tree);
        Assert.True(w.Preview < LibraryColumnLayout.PreviewDefaultWidth);
        Assert.True(w.Preview >= LibraryColumnLayout.PreviewMinWidth);
        Assert.False(w.PreviewHidden);
        Assert.Equal(LibraryColumnLayout.MosaicMinWidth, w.Mosaic, 0.01);
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(1250)]
    [InlineData(1400)]
    public void Allocate_LaptopNarrowWidths_NeverStealBelowMosaicMinWhilePreviewCanHide(double width)
    {
        // Content widths in the range that previously crushed the grid to a sliver.
        var w = LibraryColumnLayout.Allocate(width);
        if (!w.PreviewHidden)
        {
            Assert.True(w.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01);
        }
        else
        {
            // Preview gone: mosaic gets the rest after tree (+ one gap).
            Assert.Equal(0, w.Preview);
            Assert.True(w.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01
                || w.Tree <= LibraryColumnLayout.TreeMinWidth + 0.01);
            Assert.True(w.Mosaic > 100); // not a 31px sliver
        }
    }

    [Fact]
    public void Allocate_HidesPreviewWhenGridWouldDropBelow560()
    {
        // Tree at min + mosaic 560 + preview at min + 2 gaps — one DIP less → hide.
        var tight = LibraryColumnLayout.TreeMinWidth
            + LibraryColumnLayout.MosaicMinWidth
            + LibraryColumnLayout.PreviewMinWidth
            + LibraryColumnLayout.GapsWidth;
        var w = LibraryColumnLayout.Allocate(tight - 1);
        Assert.True(w.PreviewHidden);
        Assert.Equal(0, w.Preview);
        Assert.True(w.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01);
    }

    [Fact]
    public void Allocate_VeryNarrow_KeepsTreeMinAndGivesRestToMosaic()
    {
        var w = LibraryColumnLayout.Allocate(500);
        Assert.Equal(LibraryColumnLayout.TreeMinWidth, w.Tree);
        Assert.True(w.PreviewHidden);
        Assert.True(w.Mosaic > 0);
        // Window smaller than tree-min + 560: mosaic may be under floor, but not a sliver from sides.
        Assert.Equal(
            500 - LibraryColumnLayout.GapsWidth - LibraryColumnLayout.TreeMinWidth,
            w.Mosaic,
            0.01);
        // Column MinWidth must not demand more than allocated (would clip).
        Assert.Equal(w.Mosaic, LibraryColumnLayout.MosaicColumnMinWidth(w.Mosaic), 0.01);
        Assert.True(LibraryColumnLayout.MosaicColumnMinWidth(w.Mosaic) < LibraryColumnLayout.MosaicMinWidth);
    }

    [Fact]
    public void MosaicColumnMinWidth_CapsAt560AndNeverExceedsAllocated()
    {
        Assert.Equal(560, LibraryColumnLayout.MosaicColumnMinWidth(800));
        Assert.Equal(316, LibraryColumnLayout.MosaicColumnMinWidth(316));
        Assert.Equal(0, LibraryColumnLayout.MosaicColumnMinWidth(0));
        Assert.Equal(0, LibraryColumnLayout.MosaicColumnMinWidth(-10));
    }

    [Fact]
    public void MinWindowWidthDip_CoversTreeMinPlusMosaicMinPlusGapsAndPad()
    {
        // ~160+560+24 gaps + ~24 page pad ≈ 768; floor is 770.
        var contentFloor = LibraryColumnLayout.TreeMinWidth
            + LibraryColumnLayout.MosaicMinWidth
            + LibraryColumnLayout.GapsWidth;
        Assert.True(LibraryColumnLayout.MinWindowWidthDip >= contentFloor);
        Assert.Equal(770, LibraryColumnLayout.MinWindowWidthDip);
        Assert.True(LibraryColumnLayout.LibraryToolbarOverflowBelowDip > LibraryColumnLayout.MinWindowWidthDip);
        Assert.Equal(920, LibraryColumnLayout.LibraryToolbarOverflowBelowDip);
        Assert.Equal(800, LibraryColumnLayout.LibraryToolbarTightBelowDip);
        Assert.Equal(24, LibraryColumnLayout.PagePaddingHorizontalDip);
        Assert.Equal(12, LibraryColumnLayout.PagePaddingEdgeDip);
        Assert.Equal(248, LibraryColumnLayout.ShellNavPaneWidthDip);
        Assert.True(LibraryColumnLayout.MosaicToolbarFlattenBelowDip > LibraryColumnLayout.MosaicToolbarSortCompactBelowDip);
        Assert.True(LibraryColumnLayout.MosaicToolbarSortCompactBelowDip > LibraryColumnLayout.MosaicToolbarSortOverflowBelowDip);
    }
}
