using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class MosaicLayoutWidthTests
{
    [Theory]
    [InlineData(1400, 800, 1400)]
    [InlineData(1100, 800, 1100)]
    [InlineData(64, 800, 64)]
    [InlineData(63, 800, 800)] // tiny probe → last good
    [InlineData(1, 1200, 1200)]
    [InlineData(0, 900, 900)]
    [InlineData(-5, 900, 900)]
    public void Resolve_PrefersUsableAvailableElseLastGood(double available, double last, double expected) =>
        Assert.Equal(expected, MosaicLayoutWidth.Resolve(available, last));

    [Fact]
    public void Resolve_FallsBackWhenNothingUsable()
    {
        Assert.Equal(MosaicLayoutWidth.Fallback, MosaicLayoutWidth.Resolve(double.NaN, 0));
        Assert.Equal(MosaicLayoutWidth.Fallback, MosaicLayoutWidth.Resolve(double.PositiveInfinity, 10));
        Assert.Equal(MosaicLayoutWidth.Fallback, MosaicLayoutWidth.Resolve(8, double.NaN));
    }

    [Fact]
    public void Resolve_NeverPacksWiderThanHostViewport()
    {
        // Measure available can be a prior wide extent; host is the visible column.
        Assert.Equal(483, MosaicLayoutWidth.Resolve(2, lastGood: 1100, hostWidth: 483));
        Assert.Equal(
            578,
            MosaicLayoutWidth.Resolve(double.PositiveInfinity, lastGood: 1400, hostWidth: 578));
        // Available 728 with host 483 → pack to visible 483 (was returning 728 and clipping).
        Assert.Equal(483, MosaicLayoutWidth.Resolve(728, lastGood: 1400, hostWidth: 483));
        Assert.Equal(752, MosaicLayoutWidth.Resolve(900, lastGood: 1400, hostWidth: 752));
        Assert.Equal(602, MosaicLayoutWidth.Resolve(602, lastGood: 800, hostWidth: 900));
    }

    [Fact]
    public void ApplyViewportGutter_InsetsScrollbarAndEdge()
    {
        Assert.Equal(
            560 - MosaicLayoutWidth.ViewportGutterDip,
            MosaicLayoutWidth.ApplyViewportGutter(560),
            0.01);
        Assert.Equal(0, MosaicLayoutWidth.ApplyViewportGutter(0));
        Assert.Equal(32, MosaicLayoutWidth.ApplyViewportGutter(32)); // below MinUsable: unchanged
    }

    [Fact]
    public void Resolve_PreventsSliverWidthsUsedInPacking()
    {
        // Laptop resize sometimes measures with a few DIPs; packing + justify at
        // that width collapses every tile to ~1px. Fall back keeps a real row.
        var width = MosaicLayoutWidth.Resolve(available: 2, lastGood: 1100);
        Assert.Equal(1100, width);
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
        Assert.True(slots[0].Width >= MosaicRows.MiniHeaderMinWidth - 0.01);
        Assert.True(slots[1].Width > 20);
    }
}
