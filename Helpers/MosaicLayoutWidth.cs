namespace Palace.Helpers;

/// <summary>
/// Resolve mosaic arrange width. WinUI VirtualizingLayout sometimes measures with a
/// tiny or non-finite available width during resize; using that produces 1px
/// slivers and skips packing. The visible mosaic column (<paramref name="hostWidth"/>)
/// is the ceiling — never pack wider than the viewport or the last tile column
/// clips at the window edge. Off WinUI for Palace.Tests.
/// </summary>
public static class MosaicLayoutWidth
{
    /// <summary>Below this, treat available width as unknown and fall back.</summary>
    public const double MinUsable = 64;

    /// <summary>First-layout fallback when no prior good width exists.</summary>
    public const double Fallback = 640;

    public static double Resolve(double available, double lastGood, double hostWidth = double.NaN)
    {
        var availOk = double.IsFinite(available) && available >= MinUsable;
        var hostOk = double.IsFinite(hostWidth) && hostWidth >= MinUsable;

        // Visible mosaic column is the ceiling. Measure available can be the prior
        // extent or a wide probe; packing to that clips tiles at the window edge.
        if (availOk && hostOk)
        {
            return Math.Min(available, hostWidth);
        }

        if (hostOk)
        {
            return hostWidth;
        }

        if (availOk)
        {
            return available;
        }

        if (double.IsFinite(lastGood) && lastGood >= MinUsable)
        {
            return lastGood;
        }

        return Fallback;
    }
}
