using System.Collections.Specialized;

namespace Palace.Helpers;

/// <summary>
/// WinUI-free rules for <c>JustifiedMosaicLayout</c> measure/arrange cache.
/// Width+count alone miss same-count Relayout (sort/filter/aspect/headers).
/// </summary>
public static class MosaicLayoutCache
{
    public const double WidthEpsilon = 0.5;

    /// <summary>
    /// Recompute when dirty (Relayout / items change) or count/width diverge.
    /// </summary>
    public static bool NeedsRecalculate(
        bool dirty,
        int rectCount,
        int itemCount,
        double laidOutWidth,
        double width) =>
        dirty
        || rectCount != itemCount
        || Math.Abs(laidOutWidth - width) > WidthEpsilon;

    /// <summary>
    /// Mark dirty on Reset / Replace / Add / Remove so Clear+Add mosaics and
    /// in-place edits refresh even when the visible count stays the same.
    /// </summary>
    public static bool MarksDirtyOnItemsChange(NotifyCollectionChangedAction action) =>
        action is NotifyCollectionChangedAction.Reset
            or NotifyCollectionChangedAction.Replace
            or NotifyCollectionChangedAction.Add
            or NotifyCollectionChangedAction.Remove;
}
