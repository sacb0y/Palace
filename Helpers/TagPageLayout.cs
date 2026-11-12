namespace Palace.Helpers;

/// <summary>
/// Tags page column proportions (Gooey dg-02 @ ~1024). Sidebar preferred,
/// details capped 240–300, chip board takes the remainder. Widths are applied as
/// fixed pixel GridLengths from the shell Frame slot so star columns never
/// measure at ∞ and push details past the HWND. Off WinUI for tests.
/// </summary>
public static class TagPageLayout
{
    /// <summary>Preferred Tags sidebar (GROUPS + fixed views). Mockup ~170–200.</summary>
    public const double SidebarWidthDip = 200;

    /// <summary>Sidebar floor when the strip is tight (shrink before clipping details).</summary>
    public const double SidebarMinWidthDip = 160;

    /// <summary>Soft board preference when the strip is wide enough (not an XAML MinWidth).</summary>
    public const double BoardMinWidthDip = 320;

    /// <summary>Soft board floor while shrinking sidebar at narrow widths.</summary>
    public const double BoardSoftMinWidthDip = 120;

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
    /// Tags root grid <c>Padding="16"</c> left+right. Keep in sync with TagsPage.xaml.
    /// </summary>
    public const double PagePaddingHorizontalDip = 32;

    /// <summary>
    /// Shell <c>NavMain.OpenPaneLength</c> when the Left pane is expanded
    /// (<see cref="LibraryColumnLayout.ShellNavPaneWidthDip"/>).
    /// </summary>
    public const double ShellNavPaneWidthDip = LibraryColumnLayout.ShellNavPaneWidthDip;

    /// <summary>
    /// Tags columns content width from a window client DIP size minus the live nav
    /// pane, then page pad.
    /// </summary>
    public static double ContentWidthFromClientDip(
        double windowClientDip,
        double navPaneWidthDip = ShellNavPaneWidthDip)
    {
        if (!double.IsFinite(windowClientDip) || windowClientDip <= 0)
        {
            return 0;
        }

        var pane = double.IsFinite(navPaneWidthDip) && navPaneWidthDip > 0
            ? navPaneWidthDip
            : 0;
        return ContentWidthFromHostDip(Math.Max(0, windowClientDip - pane));
    }

    /// <summary>
    /// Tags columns content width from the shell content slot ActualWidth
    /// (Frame — already excludes the nav pane).
    /// </summary>
    public static double ContentWidthFromHostDip(double hostDip)
    {
        if (!double.IsFinite(hostDip) || hostDip <= 0)
        {
            return 0;
        }

        return Math.Max(0, hostDip - PagePaddingHorizontalDip);
    }

    /// <summary>
    /// Allocate sidebar / board / details for a content strip width (page pad already
    /// removed). Sum equals <paramref name="contentWidth"/> (gaps included). When
    /// <paramref name="showDetails"/> is false, details is 0 and board takes the rest.
    /// </summary>
    public static (double Sidebar, double Board, double Details) Allocate(
        double contentWidth,
        bool showDetails = true)
    {
        if (contentWidth <= 0 || !double.IsFinite(contentWidth))
        {
            return (0, 0, 0);
        }

        var available = Math.Max(0, contentWidth - GapsWidth);
        if (available <= 0)
        {
            return (0, 0, 0);
        }

        if (!showDetails)
        {
            var sidebarOnly = Math.Min(SidebarWidthDip, available);
            return (sidebarOnly, Math.Max(0, available - sidebarOnly), 0);
        }

        // Shrink sidebar when preferred sidebar + details min + soft board won't fit.
        var sidebar = SidebarWidthDip;
        var tightNeed = SidebarWidthDip + DetailsMinWidthDip + BoardSoftMinWidthDip;
        if (available < tightNeed)
        {
            sidebar = Math.Max(
                SidebarMinWidthDip,
                available - DetailsMinWidthDip - BoardSoftMinWidthDip);
            sidebar = Math.Clamp(sidebar, 0, Math.Min(SidebarWidthDip, available));
        }
        else
        {
            sidebar = Math.Min(SidebarWidthDip, available);
        }

        var rest = Math.Max(0, available - sidebar);
        double details;
        if (rest >= DetailsMinWidthDip)
        {
            details = Math.Clamp(rest * 0.24, DetailsMinWidthDip, DetailsMaxWidthDip);
            details = Math.Min(details, rest);
        }
        else
        {
            details = rest;
        }

        var board = Math.Max(0, rest - details);

        // Wide strip: prefer board ≥ BoardMinWidthDip by trimming details toward min.
        if (board < BoardMinWidthDip && rest >= BoardMinWidthDip + DetailsMinWidthDip)
        {
            details = Math.Min(DetailsMaxWidthDip, Math.Max(DetailsMinWidthDip, rest - BoardMinWidthDip));
            board = Math.Max(0, rest - details);
        }

        return (sidebar, board, details);
    }

    /// <summary>True when details width can host Name + Groups chips + action row.</summary>
    public static bool DetailsFitsActions(double detailsWidthDip) =>
        detailsWidthDip + 0.01 >= DetailsMinWidthDip;
}
