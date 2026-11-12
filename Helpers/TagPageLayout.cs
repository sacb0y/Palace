namespace Palace.Helpers;

/// <summary>
/// Tags page column proportions (Gooey dg-02 @ ~1024) plus narrow AdaptiveTrigger
/// breakpoints. Widths are applied as fixed pixel GridLengths from the shell Frame
/// slot so star columns never measure at ∞. Off WinUI for tests.
/// </summary>
public static class TagPageLayout
{
    /// <summary>Preferred Tags sidebar (GROUPS + fixed views). Mockup ~170–200.</summary>
    public const double SidebarWidthDip = 200;

    /// <summary>Sidebar when page width is below <see cref="NarrowBelowPageWidthDip"/>.</summary>
    public const double NarrowSidebarWidthDip = 180;

    /// <summary>Sidebar floor when the strip is very tight.</summary>
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
    /// Page/host width below this → narrow guard (sidebar 180, details 240).
    /// Matches TagsPage AdaptiveTrigger / ControlSizeTrigger.
    /// </summary>
    public const double NarrowBelowPageWidthDip = 760;

    /// <summary>
    /// Page/host width below this → stack details under the board (row swap).
    /// </summary>
    public const double StackedBelowPageWidthDip = 640;

    /// <summary>
    /// Expanded-nav content host at a 1024 DIP window (1024 − 248). Used by the
    /// fixed-chrome budget test (reserve ≥200 board).
    /// </summary>
    public const double HostWidthAt1024ExpandedNavDip =
        1024 - LibraryColumnLayout.ShellNavPaneWidthDip;

    /// <summary>Board strip reserved in the 776−200 fixed-chrome budget test.</summary>
    public const double BoardReserveForChromeBudgetDip = 200;

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

    /// <summary>Classify page/host width into Wide / Narrow / Stacked.</summary>
    public static TagPageLayoutMode ClassifyPageWidth(double pageWidthDip)
    {
        if (!double.IsFinite(pageWidthDip) || pageWidthDip <= 0)
        {
            return TagPageLayoutMode.Wide;
        }

        if (pageWidthDip < StackedBelowPageWidthDip)
        {
            return TagPageLayoutMode.Stacked;
        }

        if (pageWidthDip < NarrowBelowPageWidthDip)
        {
            return TagPageLayoutMode.Narrow;
        }

        return TagPageLayoutMode.Wide;
    }

    /// <summary>
    /// Fixed chrome (narrow sidebar + details min + pad + column gaps) for the
    /// Desktop host budget: must stay ≤ host − board reserve (776 − 200).
    /// </summary>
    public static double NarrowFixedChromeDip =>
        NarrowSidebarWidthDip
        + DetailsMinWidthDip
        + PagePaddingHorizontalDip
        + GapsWidth;

    /// <summary>
    /// Allocate from page/host width (before pad subtraction), applying Wide / Narrow /
    /// Stacked guards. Sum of columns + gaps equals content width (host − pad).
    /// </summary>
    public static (double Sidebar, double Board, double Details, TagPageLayoutMode Mode) AllocateForPage(
        double pageWidthDip,
        bool showDetails = true)
    {
        var mode = ClassifyPageWidth(pageWidthDip);
        var content = ContentWidthFromHostDip(pageWidthDip);
        if (content <= 0)
        {
            return (0, 0, 0, mode);
        }

        if (!showDetails)
        {
            var (side, board, _) = AllocateWide(content, showDetails: false);
            return (side, board, 0, mode);
        }

        return mode switch
        {
            TagPageLayoutMode.Stacked => AllocateStacked(content),
            TagPageLayoutMode.Narrow => AllocateNarrow(content),
            _ => AppendMode(AllocateWide(content, showDetails: true), TagPageLayoutMode.Wide),
        };
    }

    /// <summary>
    /// Allocate sidebar / board / details for a content strip width (page pad already
    /// removed). Prefer <see cref="AllocateForPage"/> when page/host width is known.
    /// </summary>
    public static (double Sidebar, double Board, double Details) Allocate(
        double contentWidth,
        bool showDetails = true)
    {
        var (sidebar, board, details, _) = AppendMode(
            AllocateWide(contentWidth, showDetails),
            TagPageLayoutMode.Wide);
        return (sidebar, board, details);
    }

    private static (double Sidebar, double Board, double Details, TagPageLayoutMode Mode) AppendMode(
        (double Sidebar, double Board, double Details) widths,
        TagPageLayoutMode mode) =>
        (widths.Sidebar, widths.Board, widths.Details, mode);

    private static (double Sidebar, double Board, double Details, TagPageLayoutMode Mode) AllocateNarrow(
        double contentWidth)
    {
        // AdaptiveTrigger narrow: sidebar 180, details 240, board gets the rest.
        var available = Math.Max(0, contentWidth - GapsWidth);
        var sidebar = Math.Min(NarrowSidebarWidthDip, available);
        var rest = Math.Max(0, available - sidebar);
        var details = rest >= DetailsMinWidthDip
            ? Math.Min(DetailsMinWidthDip, rest)
            : rest;
        var board = Math.Max(0, rest - details);
        return (sidebar, board, details, TagPageLayoutMode.Narrow);
    }

    private static (double Sidebar, double Board, double Details, TagPageLayoutMode Mode) AllocateStacked(
        double contentWidth)
    {
        // Two columns (one gap): sidebar | board; details sits under the board row.
        var available = Math.Max(0, contentWidth - ColumnSpacingDip);
        var sidebar = Math.Min(NarrowSidebarWidthDip, available);
        if (available < NarrowSidebarWidthDip + BoardSoftMinWidthDip)
        {
            sidebar = Math.Clamp(
                available - BoardSoftMinWidthDip,
                0,
                Math.Min(NarrowSidebarWidthDip, available));
        }

        var board = Math.Max(0, available - sidebar);
        // Details row uses the board column width (full under-board strip).
        return (sidebar, board, board, TagPageLayoutMode.Stacked);
    }

    private static (double Sidebar, double Board, double Details) AllocateWide(
        double contentWidth,
        bool showDetails)
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

/// <summary>Tags page width mode for AdaptiveTrigger / ControlSizeTrigger guards.</summary>
public enum TagPageLayoutMode
{
    Wide = 0,
    Narrow = 1,
    Stacked = 2
}
