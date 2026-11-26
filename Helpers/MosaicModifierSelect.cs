namespace Palace.Helpers;

/// <summary>
/// Pure Explorer-style mosaic modifier / keyboard selection math (off WinUI).
/// Reading-order ranges skip folder headers; Shift does not move the anchor.
/// Up/Down use justified row stride when slots are provided.
/// </summary>
public static class MosaicModifierSelect
{
    public const double RowYEpsilon = 1.0;

    /// <summary>Shift / Ctrl / Ctrl+Shift click should auto-enter select mode.</summary>
    public static bool IsModifierClick(bool ctrl, bool shift) => ctrl || shift;

    /// <summary>Ctrl+Shift adds the range; Shift alone replaces with the range.</summary>
    public static bool AddsRangeToSelection(bool ctrl, bool shift) => ctrl && shift;

    /// <summary>Ctrl alone (no Shift) toggles one tile and moves the anchor.</summary>
    public static bool TogglesOneTile(bool ctrl, bool shift) => ctrl && !shift;

    /// <summary>
    /// Desired selected state for Ctrl+click from the selection <em>model</em>
    /// (not ItemsView's in-flight pointer toggle). Invert once — never flip twice.
    /// </summary>
    public static bool CtrlClickWantsSelected(bool modelSelected) => !modelSelected;

    /// <summary>
    /// Modifier suppress must outlive PointerPressed into the matching
    /// PointerReleased / CaptureLost (one dispatcher turn is too early —
    /// ItemsView Multiple toggles on Released).
    /// </summary>
    public static bool EndsModifierSuppress(uint? suppressPointerId, uint endedPointerId) =>
        suppressPointerId is uint id && id == endedPointerId;

    /// <summary>Shift alone (no Ctrl) ranges from anchor without moving it.</summary>
    public static bool RangesFromAnchor(bool ctrl, bool shift) => shift && !ctrl;

    /// <summary>
    /// Contiguous reading-order indexes from anchor through target (headers skipped).
    /// Returns empty when either end is missing.
    /// </summary>
    public static IReadOnlyList<int> RangeIndexes(
        IReadOnlyList<bool> isHeader,
        int anchorIndex,
        int targetIndex) =>
        RangeSelect.ContiguousIndexes(isHeader, anchorIndex, targetIndex);

    /// <summary>
    /// Union prior selection indexes with a range (Ctrl+Shift). Order follows
    /// <paramref name="isHeader"/> reading order.
    /// </summary>
    public static IReadOnlyList<int> UnionRange(
        IReadOnlyList<bool> isHeader,
        IEnumerable<int> priorSelectedIndexes,
        int anchorIndex,
        int targetIndex)
    {
        var want = new HashSet<int>(priorSelectedIndexes.Where(i =>
            i >= 0 && i < isHeader.Count && !isHeader[i]));
        foreach (var i in RangeIndexes(isHeader, anchorIndex, targetIndex))
        {
            want.Add(i);
        }

        if (want.Count == 0)
        {
            return [];
        }

        var list = new List<int>(want.Count);
        for (var i = 0; i < isHeader.Count; i++)
        {
            if (want.Contains(i))
            {
                list.Add(i);
            }
        }

        return list;
    }

    /// <summary>Shift+click keeps the prior anchor; Ctrl / plain set it to the clicked tile.</summary>
    public static bool MovesAnchor(bool ctrl, bool shift) =>
        !RangesFromAnchor(ctrl, shift) && !AddsRangeToSelection(ctrl, shift);

    /// <summary>Arrow keys used for focus / extend.</summary>
    public static bool IsArrowKey(int virtualKey) =>
        virtualKey is 37 or 38 or 39 or 40; // Left Up Right Down

    public static bool IsVerticalArrow(int virtualKey) => virtualKey is 38 or 40; // Up Down

    public static bool IsHorizontalArrow(int virtualKey) => virtualKey is 37 or 39; // Left Right

    /// <summary>Shift+arrow (no Ctrl) extends selection from the anchor through the new focus.</summary>
    public static bool ExtendsWithShiftArrow(bool shift, bool ctrl, int virtualKey) =>
        shift && !ctrl && IsArrowKey(virtualKey);

    /// <summary>Ctrl+Shift+arrow adds the range from anchor through the new focus.</summary>
    public static bool AddsRangeWithCtrlShiftArrow(bool shift, bool ctrl, int virtualKey) =>
        shift && ctrl && IsArrowKey(virtualKey);

    /// <summary>Ctrl+arrow (no Shift) moves focus only (selection unchanged).</summary>
    public static bool MovesFocusOnly(bool shift, bool ctrl, int virtualKey) =>
        ctrl && !shift && IsArrowKey(virtualKey);

    /// <summary>Ctrl+Space toggles the focused tile.</summary>
    public static bool TogglesWithCtrlSpace(bool ctrl, bool shift, int virtualKey) =>
        ctrl && !shift && virtualKey == 32; // Space

    /// <summary>Shift+Space selects anchor..focus.</summary>
    public static bool RangesWithShiftSpace(bool ctrl, bool shift, int virtualKey) =>
        shift && !ctrl && virtualKey == 32;

    /// <summary>
    /// Next non-header index in reading order from <paramref name="from"/> by
    /// <paramref name="delta"/> (+1 / -1). Returns -1 when none.
    /// </summary>
    public static int StepFocus(IReadOnlyList<bool> isHeader, int from, int delta)
    {
        if (isHeader.Count == 0 || delta == 0)
        {
            return -1;
        }

        var i = from;
        while (true)
        {
            i += delta;
            if ((uint)i >= (uint)isHeader.Count)
            {
                return -1;
            }

            if (!isHeader[i])
            {
                return i;
            }
        }
    }

    /// <summary>
    /// Arrow step: Left/Right stay reading-order; Up/Down use justified row
    /// stride from <paramref name="slots"/> (nearest X in the adjacent row).
    /// When slots are missing or lengths disagree, falls back to reading order.
    /// </summary>
    public static int StepFocus(
        IReadOnlyList<bool> isHeader,
        IReadOnlyList<MosaicSlot>? slots,
        int from,
        int virtualKey)
    {
        if (!IsArrowKey(virtualKey) || (uint)from >= (uint)isHeader.Count)
        {
            return -1;
        }

        if (IsHorizontalArrow(virtualKey)
            || slots is null
            || slots.Count != isHeader.Count)
        {
            return StepFocus(isHeader, from, ArrowDelta(virtualKey));
        }

        return StepFocusVertical(isHeader, slots, from, up: virtualKey == 38);
    }

    /// <summary>Arrow virtual-key → reading-order delta (+1 forward, -1 back).</summary>
    public static int ArrowDelta(int virtualKey) => virtualKey switch
    {
        37 or 38 => -1, // Left / Up (Up only when no slots)
        39 or 40 => 1,  // Right / Down
        _ => 0,
    };

    private static int StepFocusVertical(
        IReadOnlyList<bool> isHeader,
        IReadOnlyList<MosaicSlot> slots,
        int from,
        bool up)
    {
        if (isHeader[from])
        {
            return -1;
        }

        var fromSlot = slots[from];
        var fromCenterX = fromSlot.X + (fromSlot.Width * 0.5);
        var fromY = fromSlot.Y;

        // Distinct row tops for non-header tiles, sorted.
        var rows = new List<double>();
        for (var i = 0; i < slots.Count; i++)
        {
            if (isHeader[i])
            {
                continue;
            }

            var y = slots[i].Y;
            var found = false;
            for (var r = 0; r < rows.Count; r++)
            {
                if (Math.Abs(rows[r] - y) <= RowYEpsilon)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                rows.Add(y);
            }
        }

        rows.Sort();

        var rowIndex = -1;
        for (var r = 0; r < rows.Count; r++)
        {
            if (Math.Abs(rows[r] - fromY) <= RowYEpsilon)
            {
                rowIndex = r;
                break;
            }
        }

        if (rowIndex < 0)
        {
            return StepFocus(isHeader, from, up ? -1 : 1);
        }

        var nextRow = up ? rowIndex - 1 : rowIndex + 1;
        if (nextRow < 0 || nextRow >= rows.Count)
        {
            return -1;
        }

        var targetY = rows[nextRow];
        var best = -1;
        var bestDist = double.MaxValue;
        for (var i = 0; i < slots.Count; i++)
        {
            if (isHeader[i] || Math.Abs(slots[i].Y - targetY) > RowYEpsilon)
            {
                continue;
            }

            var cx = slots[i].X + (slots[i].Width * 0.5);
            var dist = Math.Abs(cx - fromCenterX);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        return best;
    }
}
