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
        Assert.True(slots[1].Y >= slots[0].Y + slots[0].Height);
        Assert.True(slots[2].Y >= slots[1].Y + slots[1].Height);
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
    }
}
