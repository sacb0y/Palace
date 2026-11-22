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

    [Fact]
    public void Layout_SevenChipsAt1000_IsOneRow()
    {
        // Wimbly wrap FAIL: 7 content-sized chips (~90×32) must pack on one row @ 1000 DIP.
        var sizes = Enumerable.Repeat((90d, 32d), 7).ToArray();
        var slots = ChipWrap.Layout(sizes, width: 1000, horizontalSpacing: 6, verticalSpacing: 6);
        Assert.Equal(7, slots.Count);
        Assert.All(slots, s => Assert.Equal(0, s.Y));
        Assert.Equal(32, ChipWrap.ExtentHeight(slots));
        // Row span: 7*90 + 6*6 = 666 < 1000.
        Assert.True(slots[^1].X + slots[^1].Width < 1000);
    }

    [Fact]
    public void MeasureDesiredWidth_FiniteAvail_CapsAtAvailable()
    {
        // Chips that would be ~900 on one row; avail 400 → wrap; desired ≤ 400.
        var sizes = Enumerable.Repeat((100d, 32d), 9).ToArray();
        Assert.True(ChipWrap.NaturalContentWidth(sizes, 6) > 400);
        var desired = ChipWrap.MeasureDesiredWidth(sizes, availableWidth: 400, 6, 6);
        Assert.True(desired <= 400 + 0.01);
        Assert.True(desired > 0);
    }

    [Fact]
    public void MeasureDesiredWidth_Infinity_ReturnsNaturalNotPriorArrange()
    {
        // After a wide arrange (1200), infinite measure must report natural content
        // width — never the prior arrange / _arrangeWidth ratchet.
        var sizes = new (double Width, double Height)[] { (80, 32), (90, 32), (70, 32) };
        var natural = ChipWrap.NaturalContentWidth(sizes, 6);
        Assert.Equal(80 + 6 + 90 + 6 + 70, natural, 0.01);
        Assert.True(natural < 1200);

        var desired = ChipWrap.MeasureDesiredWidth(
            sizes,
            availableWidth: double.PositiveInfinity,
            horizontalSpacing: 6,
            verticalSpacing: 6);
        Assert.Equal(natural, desired, 0.01);
        Assert.NotEqual(1200, desired);
    }

    [Fact]
    public void ArrangeSlotWidth_Off_ReturnsSlotOnly()
    {
        Assert.Equal(
            80,
            ChipWrap.ArrangeSlotWidth(
                slotWidth: 80,
                slotX: 100,
                horizontalSpacing: 26,
                finalSizeWidth: 400,
                arrangeIntoTrailingGap: false));
    }

    [Fact]
    public void ArrangeSlotWidth_On_AddsSpacing()
    {
        Assert.Equal(
            106,
            ChipWrap.ArrangeSlotWidth(
                slotWidth: 80,
                slotX: 0,
                horizontalSpacing: 26,
                finalSizeWidth: 400,
                arrangeIntoTrailingGap: true));
    }

    [Fact]
    public void ArrangeSlotWidth_On_ClampsLastChipToFinalWidth()
    {
        // Last chip near row end: slot 80 @ x=360, spacing 26 → 106 would past 400.
        Assert.Equal(
            40,
            ChipWrap.ArrangeSlotWidth(
                slotWidth: 80,
                slotX: 360,
                horizontalSpacing: 26,
                finalSizeWidth: 400,
                arrangeIntoTrailingGap: true));
    }

    [Fact]
    public void Layout_ReserveTrailingGap_WrapsBeforeStarWouldClamp()
    {
        // Face 80 + gap 26 needs 106. At width 100, second chip must wrap so
        // ArrangeIntoTrailingGap can place the star in empty margin (not on face).
        var slots = ChipWrap.Layout(
            [(80, 24), (80, 24)],
            width: 100,
            horizontalSpacing: 26,
            verticalSpacing: 6,
            reserveTrailingGap: true);

        Assert.Equal(2, slots.Count);
        Assert.Equal(0, slots[0].Y);
        Assert.True(slots[1].Y > slots[0].Y);
        Assert.Equal(0, slots[1].X);

        // Without reserve, both fit on one row (80+26+80 = 186 needs wrap at 100 anyway
        // for the second chip's face alone). Use a width where face fits but face+gap does not.
        var faceFits = ChipWrap.Layout(
            [(80, 24), (80, 24)],
            width: 186,
            horizontalSpacing: 26,
            verticalSpacing: 6,
            reserveTrailingGap: false);
        Assert.All(faceFits, s => Assert.Equal(0, s.Y));

        var gapReserved = ChipWrap.Layout(
            [(80, 24), (80, 24)],
            width: 186,
            horizontalSpacing: 26,
            verticalSpacing: 6,
            reserveTrailingGap: true);
        // First chip at 0 needs 80+26=106; second at 106 needs 106+80=186 OK for face,
        // but reserve checks 106+80+26=212 > 186 → wrap.
        Assert.True(gapReserved[1].Y > gapReserved[0].Y);
    }
}
