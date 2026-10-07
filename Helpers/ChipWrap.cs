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
        double verticalSpacing)
    {
        if (sizes.Count == 0 || width <= 0)
        {
            return [];
        }

        var slots = new MosaicSlot[sizes.Count];
        var usable = Math.Max(1, width);
        var x = 0d;
        var y = 0d;
        var rowHeight = 0d;
        for (var i = 0; i < sizes.Count; i++)
        {
            var w = Math.Max(1, sizes[i].Width);
            var h = Math.Max(1, sizes[i].Height);
            if (x > 0 && x + w > usable)
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
}
