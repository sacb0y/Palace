namespace Palace.Helpers;

/// <summary>
/// WinUI-free TagChip face-width policy after one <c>GrdRoot</c> measure.
/// Desired width comes from <c>BrdChip.DesiredSize</c> (Left <c>GrdFace</c>), never from
/// measuring the face grandchild out of band then remeasuring the root at that width
/// (that squeezed the face against the Right star and clipped labels — Wimbly #211).
/// </summary>
public static class TagChipMeasure
{
    /// <summary>
    /// Face desired width from BrdChip after measuring GrdRoot once.
    /// Infinity / non-positive available → content width floored at 1 (UIA non-zero).
    /// Finite available → Min(content, available) so long labels clip only when narrow.
    /// </summary>
    public static double FaceWidth(double availableWidth, double brdChipDesiredWidth)
    {
        var content = Math.Max(0, brdChipDesiredWidth);
        if (!ChipWrap.IsFinitePositive(availableWidth))
        {
            return Math.Max(1, content);
        }

        return Math.Min(content, availableWidth);
    }

    /// <summary>
    /// True when Stretch-at-Infinity left BrdChip at zero and a second unconstrained
    /// GrdRoot measure (Width=Infinity) should refresh BrdChip before FaceWidth.
    /// </summary>
    public static bool NeedsUnconstrainedRemeasure(double availableWidth, double brdChipDesiredWidth) =>
        !ChipWrap.IsFinitePositive(availableWidth) && brdChipDesiredWidth <= 0;
}
