using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class MosaicRowsTests
{
    [Fact]
    public void Layout_HeadersAreCompactFullWidthRows()
    {
        var items = new (bool IsHeader, double Aspect)[]
        {
            (true, 1.5),
            (false, 1.0),
            (false, 1.0),
            (true, 1.5),
            (false, 2.0)
        };

        var slots = MosaicRows.Layout(items, width: 400, rowHeight: 120, spacing: 8, headerHeight: 28);

        Assert.Equal(5, slots.Count);
        Assert.Equal(0, slots[0].X);
        Assert.Equal(400, slots[0].Width);
        Assert.Equal(28, slots[0].Height);
        Assert.True(MosaicRows.IsCompactHeader(slots[0].Height, 120));
        Assert.True(slots[1].Y >= slots[0].Y + slots[0].Height);
        Assert.Equal(slots[1].Y, slots[2].Y);
        Assert.True(slots[3].Y >= slots[1].Y + slots[1].Height);
        Assert.Equal(400, slots[3].Width);
        Assert.Equal(28, slots[3].Height);
        Assert.True(slots[4].Y >= slots[3].Y + slots[3].Height);
        Assert.True(slots[4].Height >= 96);
    }

    [Fact]
    public void Layout_FlushesLeftoverTilesBeforeHeader()
    {
        var items = new (bool IsHeader, double Aspect)[]
        {
            (false, 1.0),
            (true, 1.0),
            (false, 1.0)
        };

        var slots = MosaicRows.Layout(items, width: 400, rowHeight: 100, spacing: 8, headerHeight: 28);

        Assert.True(slots[0].Y + slots[0].Height <= slots[1].Y + 0.01);
        Assert.Equal(0, slots[1].X);
        Assert.Equal(400, slots[1].Width);
        Assert.True(slots[2].Y >= slots[1].Y + slots[1].Height);
    }

    [Fact]
    public void Layout_EmptyOrZeroWidthIsEmpty()
    {
        Assert.Empty(MosaicRows.Layout([], 400, 120, 8, 28));
        Assert.Empty(MosaicRows.Layout([(false, 1.0)], 0, 120, 8, 28));
    }
}
