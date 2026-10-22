using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class MosaicRowsPackingTests
{
    [Fact]
    public void ClampTileAspect_EnforcesPortraitMinimum()
    {
        Assert.Equal(0.6, MosaicRows.ClampTileAspect(0.3));
        Assert.Equal(0.6, MosaicRows.ClampTileAspect(0.6));
        Assert.Equal(1.5, MosaicRows.ClampTileAspect(1.5));
    }

    [Fact]
    public void Layout_PortraitMinWidth_IsPointSixTimesRowHeight()
    {
        var items = new MosaicSpec[]
        {
            new(MosaicSlotKind.Asset, 0.3)
        };

        var slots = MosaicRows.Layout(items, width: 800, rowHeight: 100, spacing: 8, headerHeight: 34);
        Assert.Single(slots);
        Assert.Equal(60, slots[0].Width, 0.01); // 0.6 × 100
    }

    [Fact]
    public void Layout_PacksConsecutiveSmallSectionsSideBySide()
    {
        // Three tiny sections (1–3 squares) on a wide row → one packed band.
        var items = new List<MosaicSpec>
        {
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 80),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 80),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 80),
            new(MosaicSlotKind.Asset, 1)
        };

        var slots = MosaicRows.Layout(items, width: 1200, rowHeight: 140, spacing: 8, headerHeight: 34);

        Assert.Equal(9, slots.Count);
        Assert.True(slots[0].IsPacked);
        Assert.True(slots[4].IsPacked);
        Assert.True(slots[7].IsPacked);
        Assert.Equal(MosaicRows.MiniHeaderHeight, slots[0].Height);
        // Headers share the top of the packed band.
        Assert.Equal(slots[0].Y, slots[4].Y, 0.01);
        Assert.Equal(slots[0].Y, slots[7].Y, 0.01);
        Assert.True(slots[0].Width < 900);
        Assert.True(slots[4].X > slots[0].X);
        Assert.True(slots[7].X > slots[4].X);
        // Tiles sit under the mini labels in the same band.
        Assert.True(slots[1].Y > slots[0].Y);
        Assert.Equal(slots[1].Y, slots[5].Y, 0.01);
        Assert.Equal(slots[1].Y, slots[8].Y, 0.01);
        // Single gap between clusters (no double spacing).
        Assert.True(slots[4].X >= slots[3].X + slots[3].Width - 0.01);
        Assert.True(slots[4].X <= slots[3].X + slots[3].Width + 8 + 0.01);
    }

    [Fact]
    public void Layout_CollapsedSection_IsHeaderOnly()
    {
        var items = new MosaicSpec[]
        {
            new(MosaicSlotKind.FullHeader, 0, Collapsed: true, MiniWidth: 100),
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 100),
            new(MosaicSlotKind.Asset, 1.0)
        };

        var slots = MosaicRows.Layout(items, width: 400, rowHeight: 120, spacing: 8, headerHeight: 34);
        Assert.Equal(0, slots[0].X);
        Assert.Equal(400, slots[0].Width);
        Assert.Equal(34, slots[0].Height);
        Assert.False(slots[0].IsPacked);
        Assert.True(slots[1].Y >= slots[0].Y + slots[0].Height);
        Assert.True(slots[2].Y >= slots[1].Y + slots[1].Height);
    }

    [Fact]
    public void Layout_EmptySection_IsHeaderOnly()
    {
        var items = new MosaicSpec[]
        {
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 100),
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 100),
            new(MosaicSlotKind.Asset, 1.0)
        };

        var slots = MosaicRows.Layout(items, width: 500, rowHeight: 120, spacing: 8, headerHeight: 34);
        Assert.Equal(34, slots[0].Height);
        Assert.Equal(500, slots[0].Width);
        Assert.False(slots[0].IsPacked);
        // Empty section contributes no tiles; next header follows.
        Assert.True(slots[1].Y >= slots[0].Y + slots[0].Height);
    }

    [Fact]
    public void Layout_CollapsedBetweenSmallSections_DoesNotPackAcross()
    {
        var items = new List<MosaicSpec>
        {
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 80),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.FullHeader, 0, Collapsed: true, MiniWidth: 80),
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 80),
            new(MosaicSlotKind.Asset, 1)
        };

        var slots = MosaicRows.Layout(items, width: 1200, rowHeight: 140, spacing: 8, headerHeight: 34);
        // Collapsed breaks the pack chain → each small section keeps a full-width header.
        Assert.False(slots[0].IsPacked);
        Assert.Equal(0, slots[0].X);
        Assert.Equal(1200, slots[0].Width);
        Assert.False(slots[3].IsPacked);
        Assert.Equal(0, slots[3].X);
        Assert.Equal(1200, slots[3].Width);
        Assert.True(slots[3].Y > slots[0].Y);
    }

    [Fact]
    public void Layout_NarrowWidth_DoesNotPackSmallSections()
    {
        var items = new List<MosaicSpec>
        {
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 80),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 80),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.Asset, 1)
        };

        // Narrow: two clusters cannot share one row.
        var slots = MosaicRows.Layout(items, width: 200, rowHeight: 140, spacing: 8, headerHeight: 34);
        Assert.False(slots[0].IsPacked);
        Assert.False(slots[3].IsPacked);
        Assert.Equal(0, slots[0].X);
        Assert.Equal(200, slots[0].Width);
        Assert.True(slots[3].Y > slots[0].Y + 30);
    }

    [Theory]
    [InlineData(900)]
    [InlineData(1100)]
    [InlineData(1400)]
    public void Layout_LaptopWidths_PackConsecutiveSmallSections(double width)
    {
        // Two 2-tile sections stay under 60% of these widths → packed mini headers.
        var items = new List<MosaicSpec>
        {
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 80),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 80),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.Asset, 1)
        };

        var slots = MosaicRows.Layout(items, width, rowHeight: 140, spacing: 8, headerHeight: 34);
        Assert.True(slots[0].IsPacked);
        Assert.True(slots[3].IsPacked);
        Assert.Equal(MosaicRows.MiniHeaderHeight, slots[0].Height);
        Assert.True(slots[0].Width < width * 0.5);
        Assert.True(slots[3].X > slots[0].X);
    }

    [Fact]
    public void Layout_TrailingCollapsedHeader_ExtendsPastPriorSection()
    {
        // Collapse last section: header must remain in extent so scroll can reach it.
        var items = new MosaicSpec[]
        {
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 100),
            new(MosaicSlotKind.Asset, 1.5),
            new(MosaicSlotKind.Asset, 1.2),
            new(MosaicSlotKind.FullHeader, 0, Collapsed: true, MiniWidth: 100)
        };

        var slots = MosaicRows.Layout(items, width: 800, rowHeight: 140, spacing: 8, headerHeight: 34);
        Assert.Equal(4, slots.Count);
        Assert.False(slots[3].IsPacked);
        Assert.Equal(34, slots[3].Height);
        Assert.True(slots[3].Y > slots[1].Y);
        var extent = MosaicRows.ExtentHeight(slots, 8);
        Assert.True(extent >= slots[3].Y + slots[3].Height - 0.01);
    }

    [Fact]
    public void Layout_SingleSmallSection_KeepsFullWidthHeader()
    {
        var items = new MosaicSpec[]
        {
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 90),
            new(MosaicSlotKind.Asset, 1.0)
        };

        var slots = MosaicRows.Layout(items, width: 800, rowHeight: 140, spacing: 8, headerHeight: 34);
        Assert.Equal(0, slots[0].X);
        Assert.Equal(800, slots[0].Width);
        Assert.Equal(34, slots[0].Height);
        Assert.False(slots[0].IsPacked);
    }

    [Fact]
    public void Layout_PackedRow_MayGrowUpToOnePointFifteen()
    {
        // Two one-tile sections on a wide row: leftover width may grow height ≤ 1.15×.
        var items = new List<MosaicSpec>
        {
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 72),
            new(MosaicSlotKind.Asset, 1),
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 72),
            new(MosaicSlotKind.Asset, 1)
        };

        var slots = MosaicRows.Layout(items, width: 900, rowHeight: 100, spacing: 8, headerHeight: 34);
        Assert.True(slots[0].IsPacked);
        var tileHeight = slots[1].Height; // captionHeight 0
        Assert.True(tileHeight >= 100 - 0.01);
        Assert.True(tileHeight <= 100 * MosaicRows.PackedJustifyMaxScale + 0.01);
    }

    [Fact]
    public void FindStickyHeaderIndex_SkipsPackedAndBinarySearches()
    {
        var headers = new MosaicHeaderSlot[]
        {
            new(0, 0, 0, 400, 34, IsPacked: false),
            new(5, 0, 200, 400, 34, IsPacked: false),
            new(10, 0, 400, 400, 34, IsPacked: false)
        };
        Assert.Equal(0, MosaicRows.FindStickyHeaderIndex(headers, offset: 10));
        Assert.Equal(1, MosaicRows.FindStickyHeaderIndex(headers, offset: 250));
        Assert.Equal(2, MosaicRows.FindStickyHeaderIndex(headers, offset: 500));
        Assert.Equal(-1, MosaicRows.FindStickyHeaderIndex(headers, offset: -5));

        var pinnable = MosaicRows.PinnableHeaderSlots(
        [
            new(0, 0, 0, 80, 22, IsPacked: true),
            new(3, 0, 100, 400, 34, IsPacked: false)
        ]);
        Assert.Single(pinnable);
        Assert.Equal(3, pinnable[0].Index);
    }
}
