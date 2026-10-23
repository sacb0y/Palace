namespace Palace.Helpers;

/// <summary>One mosaic slot. Off WinUI so Palace.Tests can cover header vs tile layout.</summary>
public readonly record struct MosaicSlot(
    double X,
    double Y,
    double Width,
    double Height,
    bool IsPacked = false);

/// <summary>Pinned / sticky header candidate from the last layout arrange.</summary>
public readonly record struct MosaicHeaderSlot(
    int Index,
    double X,
    double Y,
    double Width,
    double Height,
    bool IsPacked);

/// <summary>Kind of mosaic list entry for <see cref="MosaicRows"/>.</summary>
public enum MosaicSlotKind
{
    Asset,
    FullHeader
}

/// <summary>Layout input for one mosaic item.</summary>
public readonly record struct MosaicSpec(
    MosaicSlotKind Kind,
    double Aspect,
    bool Collapsed = false,
    double MiniWidth = 0);

/// <summary>
/// Library mosaic packing: assets share justified rows; folder headers are compact
/// full-width caption rows, never the asset tile aspect / line height. Small
/// consecutive sections pack side by side with mini labels.
/// </summary>
public static class MosaicRows
{
    public const double MiniHeaderHeight = 22;
    public const double MiniHeaderMinWidth = 72;
    public const double MiniHeaderMaxWidth = 160;
    public const double MiniHeaderCharWidth = 7.5;

    /// <summary>Packed rows may grow up to this × target row height to fill width.</summary>
    public const double PackedJustifyMaxScale = 1.15;

    public static double ClampTileAspect(double aspect) =>
        Math.Max(FolderSectionPrefs.MinPortraitAspect, aspect);

    public static double MiniHeaderWidth(string? title, int count)
    {
        var label = (title ?? "").Trim();
        var countPart = count > 0 ? 4 + count.ToString().Length : 0;
        var chars = Math.Max(4, label.Length + countPart + 2);
        return Math.Clamp(chars * MiniHeaderCharWidth, MiniHeaderMinWidth, MiniHeaderMaxWidth);
    }

    public static IReadOnlyList<MosaicSlot> Layout(
        IReadOnlyList<(bool IsHeader, double Aspect)> items,
        double width,
        double rowHeight,
        double spacing,
        double headerHeight,
        double captionHeight = 0) =>
        Layout(
            items.Select(item => new MosaicSpec(
                item.IsHeader ? MosaicSlotKind.FullHeader : MosaicSlotKind.Asset,
                item.Aspect)).ToList(),
            width,
            rowHeight,
            spacing,
            headerHeight,
            captionHeight);

    public static IReadOnlyList<MosaicSlot> Layout(
        IReadOnlyList<MosaicSpec> items,
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
        var sections = ParseSections(items);

        var y = 0d;
        var i = 0;
        while (i < sections.Count)
        {
            var section = sections[i];
            var canPack = section.HeaderIndex >= 0
                && !section.Collapsed
                && IsSmall(section, usable, targetHeight, spacing);

            if (!canPack)
            {
                PlaceFullSection(slots, section, usable, header, targetHeight, spacing, captionHeight, ref y);
                i++;
                continue;
            }

            var pack = new List<Section>(4) { section };
            var packWidth = EstimatePackedWidth(section, targetHeight, spacing);
            i++;
            while (i < sections.Count)
            {
                var next = sections[i];
                if (next.HeaderIndex < 0
                    || next.Collapsed
                    || !IsSmall(next, usable, targetHeight, spacing))
                {
                    break;
                }

                var nextWidth = EstimatePackedWidth(next, targetHeight, spacing);
                if (pack.Count > 0 && packWidth + spacing + nextWidth > usable)
                {
                    break;
                }

                pack.Add(next);
                packWidth += spacing + nextWidth;
                i++;
            }

            if (pack.Count == 1)
            {
                // One small section alone: still use a full header so the divider stays readable.
                PlaceFullSection(slots, pack[0], usable, header, targetHeight, spacing, captionHeight, ref y);
            }
            else
            {
                PlacePacked(slots, pack, usable, targetHeight, spacing, captionHeight, ref y);
            }
        }

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

    /// <summary>
    /// Last full (non-packed) header whose top is at or above <paramref name="offset"/>.
    /// Packed mini headers are never pinned.
    /// </summary>
    public static int FindStickyHeaderIndex(
        IReadOnlyList<MosaicHeaderSlot> headers,
        double offset)
    {
        var lo = 0;
        var hi = headers.Count - 1;
        var best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var slot = headers[mid];
            if (slot.IsPacked)
            {
                // Packed headers are omitted from the cache; keep search stable.
                hi = mid - 1;
                continue;
            }

            if (slot.Y <= offset + 1)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return best;
    }

    public static IReadOnlyList<MosaicHeaderSlot> CollectHeaderSlots(
        IReadOnlyList<MosaicSlot> slots,
        Func<int, bool> isHeader)
    {
        var headers = new List<MosaicHeaderSlot>();
        for (var i = 0; i < slots.Count; i++)
        {
            if (!isHeader(i))
            {
                continue;
            }

            var slot = slots[i];
            headers.Add(new MosaicHeaderSlot(
                i,
                slot.X,
                slot.Y,
                slot.Width,
                slot.Height,
                slot.IsPacked));
        }

        return headers;
    }

    /// <summary>
    /// Pinnable sticky candidates only (full-width headers), sorted by Y.
    /// </summary>
    public static IReadOnlyList<MosaicHeaderSlot> PinnableHeaderSlots(
        IReadOnlyList<MosaicHeaderSlot> headers)
    {
        var list = new List<MosaicHeaderSlot>(headers.Count);
        foreach (var header in headers)
        {
            if (!header.IsPacked)
            {
                list.Add(header);
            }
        }

        return list;
    }

    private static bool IsSmall(Section section, double usable, double rowHeight, double spacing) =>
        section.Assets.Count > 0
        && FolderSectionPrefs.IsSmallSection(
            section.Assets.Select(a => a.Aspect).ToList(),
            usable,
            rowHeight,
            spacing);

    /// <summary>
    /// Natural width of one packed cluster: mini label + gap + tiles
    /// (matches <see cref="PlacePacked"/> fixed parts at <paramref name="rowHeight"/>).
    /// </summary>
    private static double EstimatePackedWidth(Section section, double rowHeight, double spacing)
    {
        var tiles = FolderSectionPrefs.SectionNaturalWidth(
            section.Assets.Select(a => a.Aspect).ToList(),
            rowHeight,
            spacing);
        // label + (label→tiles gap when there are tiles) + tiles
        return section.MiniWidth
            + (section.Assets.Count > 0 ? spacing : 0)
            + tiles;
    }

    private static void PlaceFullSection(
        MosaicSlot[] slots,
        Section section,
        double usable,
        double headerHeight,
        double targetHeight,
        double spacing,
        double captionHeight,
        ref double y)
    {
        if (section.HeaderIndex >= 0)
        {
            slots[section.HeaderIndex] = new MosaicSlot(0, y, usable, headerHeight, IsPacked: false);
            y += headerHeight + spacing;
        }

        if (section.Collapsed || section.Assets.Count == 0)
        {
            return;
        }

        var row = new List<(int Index, double Aspect)>();
        var rowAspect = 0d;
        var cursorY = y;

        foreach (var (index, aspect) in section.Assets)
        {
            var tileAspect = ClampTileAspect(aspect);
            var nextAspect = rowAspect + tileAspect;
            var nextWidth = targetHeight * nextAspect + spacing * row.Count;
            if (row.Count > 0 && nextWidth > usable)
            {
                cursorY = FlushRow(slots, row, rowAspect, usable, targetHeight, spacing, captionHeight, cursorY, justify: true);
                rowAspect = 0;
            }

            row.Add((index, tileAspect));
            rowAspect += tileAspect;
        }

        y = FlushRow(slots, row, rowAspect, usable, targetHeight, spacing, captionHeight, cursorY, justify: false);
    }

    private static double FlushRow(
        MosaicSlot[] slots,
        List<(int Index, double Aspect)> row,
        double rowAspect,
        double usable,
        double targetHeight,
        double spacing,
        double captionHeight,
        double y,
        bool justify)
    {
        if (row.Count == 0)
        {
            return y;
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

        row.Clear();
        return y + height + captionHeight + spacing;
    }

    private static void PlacePacked(
        MosaicSlot[] slots,
        IReadOnlyList<Section> pack,
        double usable,
        double targetHeight,
        double spacing,
        double captionHeight,
        ref double y)
    {
        var labelH = MiniHeaderHeight;
        var clusters = pack
            .Select(section =>
            {
                var aspects = section.Assets.Select(a => ClampTileAspect(a.Aspect)).ToList();
                return (
                    section.HeaderIndex,
                    section.MiniWidth,
                    section.Assets,
                    TileAspectSum: aspects.Sum(),
                    Aspects: aspects);
            })
            .ToList();

        var labelWidths = clusters.Sum(c => c.MiniWidth);
        var clusterGaps = spacing * Math.Max(0, clusters.Count - 1);
        var tileGaps = clusters.Sum(c => spacing * Math.Max(0, c.Assets.Count - 1));
        // One gap between each label and its first tile.
        var labelTileGaps = clusters.Count(c => c.Assets.Count > 0) * spacing;
        var aspectSum = clusters.Sum(c => c.TileAspectSum);
        var fixedWidth = labelWidths + clusterGaps + tileGaps + labelTileGaps;
        var height = targetHeight;
        if (aspectSum > 0)
        {
            var fillHeight = (usable - fixedWidth) / aspectSum;
            // Shrink to fit, or grow up to 1.15× to fill leftover width. No 48px floor.
            height = fillHeight > targetHeight
                ? Math.Min(fillHeight, targetHeight * PackedJustifyMaxScale)
                : Math.Max(1, Math.Min(fillHeight, targetHeight));
        }

        var tileWidth = height * aspectSum + tileGaps + labelTileGaps;
        var labelBudget = Math.Max(MiniHeaderMinWidth * clusters.Count, usable - clusterGaps - tileWidth);
        var labelScale = labelWidths > 0 ? Math.Min(1, labelBudget / labelWidths) : 1;

        var x = 0d;
        for (var c = 0; c < clusters.Count; c++)
        {
            var cluster = clusters[c];
            var labelW = Math.Max(MiniHeaderMinWidth, cluster.MiniWidth * labelScale);
            if (cluster.HeaderIndex >= 0)
            {
                slots[cluster.HeaderIndex] = new MosaicSlot(x, y, labelW, labelH, IsPacked: true);
            }

            var tileX = x + labelW + (cluster.Assets.Count > 0 ? spacing : 0);
            var tileY = y + labelH + 2;
            foreach (var (index, aspect) in cluster.Assets)
            {
                var tileAspect = ClampTileAspect(aspect);
                var itemWidth = Math.Max(1, height * tileAspect);
                slots[index] = new MosaicSlot(tileX, tileY, itemWidth, height + captionHeight);
                tileX += itemWidth + spacing;
            }

            // Next cluster starts after the last tile's trailing spacing (one gap), not two.
            x = cluster.Assets.Count > 0
                ? tileX
                : x + labelW + spacing;
        }

        y += labelH + 2 + height + captionHeight + spacing;
    }

    private static List<Section> ParseSections(IReadOnlyList<MosaicSpec> items)
    {
        var sections = new List<Section>();
        Section? current = null;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Kind == MosaicSlotKind.FullHeader)
            {
                current = new Section
                {
                    HeaderIndex = i,
                    Collapsed = item.Collapsed,
                    MiniWidth = item.MiniWidth > 0
                        ? Math.Clamp(item.MiniWidth, MiniHeaderMinWidth, MiniHeaderMaxWidth)
                        : MiniHeaderMinWidth
                };
                sections.Add(current);
                continue;
            }

            if (current is null)
            {
                current = new Section { HeaderIndex = -1 };
                sections.Add(current);
            }

            current.Assets.Add((i, item.Aspect));
        }

        return sections;
    }

    private sealed class Section
    {
        public int HeaderIndex { get; init; } = -1;
        public bool Collapsed { get; init; }
        public double MiniWidth { get; init; } = MiniHeaderMinWidth;
        public List<(int Index, double Aspect)> Assets { get; } = [];
    }
}
