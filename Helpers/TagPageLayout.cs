namespace Palace.Helpers;

/// <summary>
/// Tags page column proportions (Gooey dg-02 @ ~1024). Sidebar fixed, details
/// capped, chip board takes the remainder with a narrow floor. Off WinUI for tests.
/// </summary>
public static class TagPageLayout
{
    /// <summary>Fixed Tags sidebar (GROUPS + fixed views). Mockup ~170–200.</summary>
    public const double SidebarWidthDip = 200;

    /// <summary>Chip board floor so wrap stays usable at narrow windows.</summary>
    public const double BoardMinWidthDip = 320;

    /// <summary>Details pane floor — Name / Groups / actions must fit @ 150%.</summary>
    public const double DetailsMinWidthDip = 240;

    /// <summary>Details pane cap (~24% of a 1024 content strip ≈ 245–300).</summary>
    public const double DetailsMaxWidthDip = 300;

    /// <summary>~6 DIP chip wrap spacing (both axes).</summary>
    public const double ChipWrapSpacingDip = 6;

    /// <summary>Horizontal gaps between the three Tags columns.</summary>
    public const double ColumnSpacingDip = 16;

    public const int ColumnGapCount = 2;

    public static double GapsWidth => ColumnSpacingDip * ColumnGapCount;

    /// <summary>
    /// Allocate sidebar / board / details for a content strip width (page pad already
    /// removed). Mosaic assets stay a sibling product column when present — this
    /// triple is the Gooey dg-02 frame.
    /// </summary>
    public static (double Sidebar, double Board, double Details) Allocate(double contentWidth)
    {
        if (contentWidth <= 0 || double.IsNaN(contentWidth) || double.IsInfinity(contentWidth))
        {
            return (SidebarWidthDip, BoardMinWidthDip, DetailsMinWidthDip);
        }

        var available = Math.Max(0, contentWidth - GapsWidth);
        var sidebar = Math.Min(SidebarWidthDip, available);
        var rest = Math.Max(0, available - sidebar);

        var details = Math.Clamp(rest * 0.24, DetailsMinWidthDip, DetailsMaxWidthDip);
        if (details > rest)
        {
            details = rest;
        }

        var board = Math.Max(0, rest - details);
        if (board < BoardMinWidthDip && rest >= BoardMinWidthDip + DetailsMinWidthDip)
        {
            board = BoardMinWidthDip;
            details = Math.Max(DetailsMinWidthDip, rest - board);
            details = Math.Min(details, DetailsMaxWidthDip);
            board = Math.Max(0, rest - details);
        }

        return (sidebar, board, details);
    }

    /// <summary>True when details width can host Name + Groups chips + action row.</summary>
    public static bool DetailsFitsActions(double detailsWidthDip) =>
        detailsWidthDip + 0.01 >= DetailsMinWidthDip;
}
