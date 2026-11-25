namespace Palace.Helpers;

/// <summary>
/// Pure Explorer-style mosaic modifier / keyboard selection math (off WinUI).
/// Reading-order ranges skip folder headers; Shift does not move the anchor.
/// </summary>
public static class MosaicModifierSelect
{
    /// <summary>Shift / Ctrl / Ctrl+Shift click should auto-enter select mode.</summary>
    public static bool IsModifierClick(bool ctrl, bool shift) => ctrl || shift;

    /// <summary>Ctrl+Shift adds the range; Shift alone replaces with the range.</summary>
    public static bool AddsRangeToSelection(bool ctrl, bool shift) => ctrl && shift;

    /// <summary>Ctrl alone (no Shift) toggles one tile and moves the anchor.</summary>
    public static bool TogglesOneTile(bool ctrl, bool shift) => ctrl && !shift;

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

    /// <summary>Shift+arrow extends selection from the anchor through the new focus.</summary>
    public static bool ExtendsWithShiftArrow(bool shift, bool ctrl, int virtualKey) =>
        shift && !ctrl && IsArrowKey(virtualKey);

    /// <summary>Ctrl+arrow moves focus only (selection unchanged).</summary>
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

    /// <summary>Arrow virtual-key → reading-order delta (+1 forward, -1 back).</summary>
    public static int ArrowDelta(int virtualKey) => virtualKey switch
    {
        37 or 38 => -1, // Left / Up
        39 or 40 => 1,  // Right / Down
        _ => 0,
    };
}
