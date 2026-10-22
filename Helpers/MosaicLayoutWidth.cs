namespace Palace.Helpers;

/// <summary>
/// Resolve mosaic arrange width. WinUI VirtualizingLayout sometimes measures with a
/// tiny or non-finite available width during resize; using that produces 1px
/// slivers and skips packing. Off WinUI for Palace.Tests.
/// </summary>
public static class MosaicLayoutWidth
{
    /// <summary>Below this, treat available width as unknown and fall back.</summary>
    public const double MinUsable = 64;

    /// <summary>First-layout fallback when no prior good width exists.</summary>
    public const double Fallback = 640;

    public static double Resolve(double available, double lastGood)
    {
        if (double.IsFinite(available) && available >= MinUsable)
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
