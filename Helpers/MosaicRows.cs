namespace Palace.Helpers;

/// <summary>One mosaic slot. Off WinUI so Palace.Tests can cover header vs tile layout.</summary>
public readonly record struct MosaicSlot(double X, double Y, double Width, double Height);

/// <summary>
/// Library mosaic packing: assets share justified rows; folder headers are compact
/// full-width caption rows, never the asset tile aspect / line height.
/// </summary>
public static class MosaicRows
{
    public static IReadOnlyList<MosaicSlot> Layout(
        IReadOnlyList<(bool IsHeader, double Aspect)> items,
        double width,
        double rowHeight,
        double spacing,
        double headerHeight,
        double captionHeight = 0)
    {
        if (items.Count == 0 || width <= 0)
        {
            return [];
        }

        var slots = new MosaicSlot[items.Count];
        var targetHeight = Math.Clamp(rowHeight, 96, 280);
        var header = Math.Max(1, headerHeight);
        var usable = Math.Max(1, width);
        var row = new List<(int Index, double Aspect)>();
        var rowAspect = 0d;
        var y = 0d;

        void Flush(bool justify)
        {
            if (row.Count == 0)
            {
                return;
            }

            var gaps = spacing * Math.Max(0, row.Count - 1);
            var height = justify
                ? (usable - gaps) / rowAspect
                : targetHeight;
            var natural = height * rowAspect + gaps;
            if (natural > usable)
            {
                height = (usable - gaps) / rowAspect;
            }

            var x = 0d;
            foreach (var (index, aspect) in row)
            {
                var itemWidth = Math.Max(1, height * aspect);
                slots[index] = new MosaicSlot(x, y, itemWidth, height + captionHeight);
                x += itemWidth + spacing;
            }

            y += height + captionHeight + spacing;
            row.Clear();
            rowAspect = 0;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var (isHeader, aspect) = items[i];
            if (isHeader)
            {
                Flush(justify: false);
                slots[i] = new MosaicSlot(0, y, usable, header);
                y += header + spacing;
                continue;
            }

            var tileAspect = Math.Max(0.15, aspect);
            var nextAspect = rowAspect + tileAspect;
            var nextWidth = targetHeight * nextAspect + spacing * row.Count;
            if (row.Count > 0 && nextWidth > usable)
            {
                Flush(justify: true);
            }

            row.Add((i, tileAspect));
            rowAspect += tileAspect;
        }

        Flush(justify: false);
        return slots;
    }

    public static double ExtentHeight(IReadOnlyList<MosaicSlot> slots, double spacing)
    {
        if (slots.Count == 0)
        {
            return 0;
        }

        var bottom = 0d;
        foreach (var slot in slots)
        {
            bottom = Math.Max(bottom, slot.Y + slot.Height);
        }

        return Math.Max(0, bottom);
    }

    public static bool IsCompactHeader(double height, double mosaicRowHeight) =>
        height > 0 && height < mosaicRowHeight;
}
