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

    /// <summary>
    /// Inset from the mosaic column edge so justified tiles do not sit under the
    /// overlay vertical scrollbar. Packing must use the <b>allocated</b> mosaic
    /// width (after nav / page pad / Preview), never a stale wider ActualWidth.
    /// </summary>
    public const double ViewportGutterDip = 16;

    /// <summary>Packing width from a measured column / ItemsView width.</summary>
    public static double ApplyViewportGutter(double columnWidth)
    {
        if (!double.IsFinite(columnWidth) || columnWidth <= 0)
        {
            return 0;
        }

        if (columnWidth < MinUsable)
        {
            return columnWidth;
        }

        return Math.Max(MinUsable, columnWidth - ViewportGutterDip);
    }

    /// <summary>
    /// Cap a measured column / ItemsView width to the last <see cref="LibraryColumnLayout.Allocate"/>
    /// mosaic width so a stale wide ActualWidth cannot raise HostWidth past the viewport.
    /// </summary>
    public static double CapToAllocatedMosaic(double measured, double allocatedMosaic)
    {
        if (!double.IsFinite(measured) || measured <= 0)
        {
            return double.IsFinite(allocatedMosaic) && allocatedMosaic > 0 ? allocatedMosaic : 0;
        }

        if (double.IsFinite(allocatedMosaic) && allocatedMosaic >= MinUsable)
        {
            return Math.Min(measured, allocatedMosaic);
        }

        return measured;
    }

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
