using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ChipWrapTests
{
    [Fact]
    public void Layout_WrapsWhenRowWouldOverflow()
    {
        var slots = ChipWrap.Layout(
            [(80, 24), (80, 24), (80, 24)],
            width: 180,
            horizontalSpacing: 6,
            verticalSpacing: 6);

        Assert.Equal(3, slots.Count);
        Assert.Equal(0, slots[0].Y);
        Assert.Equal(slots[0].Y, slots[1].Y);
        Assert.True(slots[2].Y >= slots[0].Y + slots[0].Height);
        Assert.Equal(0, slots[2].X);
        Assert.Equal(24 + 6 + 24, ChipWrap.ExtentHeight(slots));
    }

    [Fact]
    public void Layout_EmptyOrZeroWidthIsEmpty()
    {
        Assert.Empty(ChipWrap.Layout([], 180, 6, 6));
        Assert.Empty(ChipWrap.Layout([(40, 20)], 0, 6, 6));
    }
}
