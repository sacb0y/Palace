namespace Palace.Helpers;

public enum RangeSelectPointer
{
    Mouse,
    Touch,
    Pen,
    Other
}

/// <summary>
/// Select-mode range drag for Library and Tags mosaics. Not a marquee:
/// the selection is the contiguous mosaic span from the press tile through
/// the last highlighted tile, skipping folder headers. Touch waits for a
/// press-and-hold so overlay / mosaic pan is not stolen. Off WinUI.
/// </summary>
public static class RangeSelect
{
    public const int TouchHoldMs = 500;
    public const double MoveSlop = 8;

    public static bool AllowsGesture(bool selectMode, bool overlayOpen, bool isFolderHeader) =>
        selectMode && !overlayOpen && !isFolderHeader;

    public static bool BeginsOnPress(RangeSelectPointer pointer) =>
        pointer is RangeSelectPointer.Mouse or RangeSelectPointer.Pen;

    public static bool RequiresHold(RangeSelectPointer pointer) =>
        pointer is RangeSelectPointer.Touch;

    public static bool HoldReady(int elapsedMs) => elapsedMs >= TouchHoldMs;

    public static bool ExceedsSlop(double dx, double dy) =>
        (dx * dx) + (dy * dy) >= MoveSlop * MoveSlop;

    /// <summary>
    /// Touch movement before the hold is pan, not a range.
    /// </summary>
    public static bool CancelsHold(RangeSelectPointer pointer, int elapsedMs, double dx, double dy) =>
        RequiresHold(pointer) && !HoldReady(elapsedMs) && ExceedsSlop(dx, dy);

    public static bool ActivatesDrag(RangeSelectPointer pointer, int elapsedMs, double dx, double dy)
    {
        if (!ExceedsSlop(dx, dy))
        {
            return false;
        }

        if (RequiresHold(pointer))
        {
            return HoldReady(elapsedMs);
        }

        return BeginsOnPress(pointer);
    }

    /// <summary>
    /// Inclusive mosaic indexes from <paramref name="from"/> through
    /// <paramref name="to"/> in list order, skipping headers.
    /// </summary>
    public static IReadOnlyList<int> ContiguousIndexes(IReadOnlyList<bool> isHeader, int from, int to)
    {
        if (isHeader.Count == 0
            || from < 0
            || to < 0
            || from >= isHeader.Count
            || to >= isHeader.Count)
        {
            return [];
        }

        var lo = Math.Min(from, to);
        var hi = Math.Max(from, to);
        var indexes = new List<int>(hi - lo + 1);
        for (var i = lo; i <= hi; i++)
        {
            if (!isHeader[i])
            {
                indexes.Add(i);
            }
        }

        return indexes;
    }
}

/// <summary>
/// One press → optional hold → drag gesture. Pages own pointer capture.
/// </summary>
public sealed class RangeSelectSession
{
    public RangeSelectPointer Pointer { get; }
    public int AnchorIndex { get; }
    public int EndIndex { get; private set; }
    public bool IsActive { get; private set; }

    public RangeSelectSession(RangeSelectPointer pointer, int anchorIndex)
    {
        Pointer = pointer;
        AnchorIndex = anchorIndex;
        EndIndex = anchorIndex;
    }

    public bool TryActivate(int elapsedMs, double dx, double dy)
    {
        if (IsActive || RangeSelect.CancelsHold(Pointer, elapsedMs, dx, dy))
        {
            return false;
        }

        if (!RangeSelect.ActivatesDrag(Pointer, elapsedMs, dx, dy))
        {
            return false;
        }

        IsActive = true;
        return true;
    }

    public bool TryActivateHold(int elapsedMs)
    {
        if (IsActive || !RangeSelect.RequiresHold(Pointer) || !RangeSelect.HoldReady(elapsedMs))
        {
            return false;
        }

        IsActive = true;
        return true;
    }

    public IReadOnlyList<int> Highlight(IReadOnlyList<bool> isHeader, int hoverIndex)
    {
        if (hoverIndex >= 0 && hoverIndex < isHeader.Count)
        {
            EndIndex = hoverIndex;
        }

        return RangeSelect.ContiguousIndexes(isHeader, AnchorIndex, EndIndex);
    }
}
