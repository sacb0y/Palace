namespace Palace.Helpers;

/// <summary>
/// Variable-width chip wrapping. Off WinUI so Palace.Tests can cover it.
/// Library Tags browse uses this instead of WASDK 2.4-internal WrapLayout.
/// </summary>
public static class ChipWrap
{
    public static IReadOnlyList<MosaicSlot> Layout(
        IReadOnlyList<(double Width, double Height)> sizes,
        double width,
        double horizontalSpacing,
        double verticalSpacing,
        bool reserveTrailingGap = false)
    {
        if (sizes.Count == 0 || width <= 0 || double.IsNaN(width))
        {
            return [];
        }

        var slots = new MosaicSlot[sizes.Count];
        var usable = Math.Max(1, width);
        var gap = Math.Max(0, horizontalSpacing);
        var x = 0d;
        var y = 0d;
        var rowHeight = 0d;
        for (var i = 0; i < sizes.Count; i++)
        {
            var w = Math.Max(1, sizes[i].Width);
            var h = Math.Max(1, sizes[i].Height);
            // When arranging into the trailing wrap gap (Tags board star), reserve
            // that gap in the wrap test so the last chip's star is not clamped
            // onto the face (x + w + gap would otherwise equal final − x ≈ w).
            var need = reserveTrailingGap ? w + gap : w;
            if (x > 0 && x + need > usable)
            {
                y += rowHeight + verticalSpacing;
                x = 0;
                rowHeight = 0;
            }

            slots[i] = new MosaicSlot(x, y, w, h);
            x += w + horizontalSpacing;
            rowHeight = Math.Max(rowHeight, h);
        }

        return slots;
    }

    public static double ExtentHeight(IReadOnlyList<MosaicSlot> slots)
    {
        var bottom = 0d;
        foreach (var slot in slots)
        {
            bottom = Math.Max(bottom, slot.Y + slot.Height);
        }

        return Math.Max(0, bottom);
    }

    /// <summary>Right edge of the widest laid-out row.</summary>
    public static double ExtentWidth(IReadOnlyList<MosaicSlot> slots)
    {
        var right = 0d;
        foreach (var slot in slots)
        {
            right = Math.Max(right, slot.X + slot.Width);
        }

        return Math.Max(0, right);
    }

    /// <summary>One-row natural width (sum of chips + gaps) — infinite-measure path.</summary>
    public static double NaturalContentWidth(
        IReadOnlyList<(double Width, double Height)> sizes,
        double horizontalSpacing)
    {
        if (sizes.Count == 0)
        {
            return 0;
        }

        var total = 0d;
        for (var i = 0; i < sizes.Count; i++)
        {
            total += Math.Max(1, sizes[i].Width);
            if (i > 0)
            {
                total += Math.Max(0, horizontalSpacing);
            }
        }

        return total;
    }

    /// <summary>
    /// Measure desired width: widest laid-out row, capped at finite available.
    /// Infinite / non-positive available → natural content width (never a prior arrange width).
    /// </summary>
    public static double MeasureDesiredWidth(
        IReadOnlyList<(double Width, double Height)> sizes,
        double availableWidth,
        double horizontalSpacing,
        double verticalSpacing,
        bool reserveTrailingGap = false)
    {
        if (sizes.Count == 0)
        {
            return 0;
        }

        if (!IsFinitePositive(availableWidth))
        {
            return NaturalContentWidth(sizes, horizontalSpacing);
        }

        var slots = Layout(
            sizes,
            availableWidth,
            horizontalSpacing,
            verticalSpacing,
            reserveTrailingGap);
        return Math.Min(availableWidth, ExtentWidth(slots));
    }

    public static bool IsFinitePositive(double value) =>
        double.IsFinite(value) && value > 0;

    /// <summary>
    /// Arrange width for one chip slot. When <paramref name="arrangeIntoTrailingGap"/>,
    /// include horizontal spacing (star-in-gap) and clamp so the last chip on a row
    /// does not extend past <paramref name="finalSizeWidth"/>.
    /// </summary>
    public static double ArrangeSlotWidth(
        double slotWidth,
        double slotX,
        double horizontalSpacing,
        double finalSizeWidth,
        bool arrangeIntoTrailingGap)
    {
        if (!arrangeIntoTrailingGap)
        {
            return slotWidth;
        }

        return Math.Min(slotWidth + horizontalSpacing, finalSizeWidth - slotX);
    }
}
