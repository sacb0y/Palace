namespace Palace.Helpers;

/// <summary>
/// Resolve mosaic arrange width. WinUI VirtualizingLayout sometimes measures with a
/// tiny or non-finite available width during resize; using that produces 1px
/// slivers and skips packing. Prefer the host's <paramref name="hostWidth"/>
/// (ItemsView ActualWidth) over a stale last-good so column count tracks the
/// real mosaic column. Off WinUI for Palace.Tests.
/// </summary>
public static class MosaicLayoutWidth
{
    /// <summary>Below this, treat available width as unknown and fall back.</summary>
    public const double MinUsable = 64;

    /// <summary>First-layout fallback when no prior good width exists.</summary>
    public const double Fallback = 640;

    public static double Resolve(double available, double lastGood, double hostWidth = double.NaN)
    {
        if (double.IsFinite(available) && available >= MinUsable)
        {
            return available;
        }

        // Actual mosaic host beats a stale last-good (narrow resize otherwise
        // packs for the previous wide width and clips the last tile column).
        if (double.IsFinite(hostWidth) && hostWidth >= MinUsable)
        {
            return hostWidth;
        }

        if (double.IsFinite(lastGood) && lastGood >= MinUsable)
        {
            return lastGood;
        }

        return Fallback;
    }
}
