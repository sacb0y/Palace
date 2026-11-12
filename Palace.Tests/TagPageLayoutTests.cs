using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagPageLayoutTests
{
    [Fact]
    public void Allocate_At1024Content_MatchesGooeyProportions()
    {
        // ~1024 window with compact nav (~48) + page pad → content ≈ 940–960.
        var (sidebar, board, details) = TagPageLayout.Allocate(940);
        Assert.Equal(TagPageLayout.SidebarWidthDip, sidebar);
        Assert.True(board >= TagPageLayout.BoardMinWidthDip);
        Assert.InRange(details, TagPageLayout.DetailsMinWidthDip, TagPageLayout.DetailsMaxWidthDip);
        Assert.Equal(
            940,
            sidebar + board + details + TagPageLayout.GapsWidth,
            0.01);
        Assert.True(TagPageLayout.DetailsFitsActions(details));
    }

    [Fact]
    public void Allocate_Narrow_KeepsBoardFloorAndDetailsMin()
    {
        var need = TagPageLayout.SidebarWidthDip
            + TagPageLayout.BoardMinWidthDip
            + TagPageLayout.DetailsMinWidthDip
            + TagPageLayout.GapsWidth;
        var (sidebar, board, details) = TagPageLayout.Allocate(need);
        Assert.Equal(TagPageLayout.SidebarWidthDip, sidebar);
        Assert.Equal(TagPageLayout.BoardMinWidthDip, board, 0.01);
        Assert.Equal(TagPageLayout.DetailsMinWidthDip, details, 0.01);
    }

    [Fact]
    public void Allocate_Wide_CapsDetailsAt300()
    {
        var (_, board, details) = TagPageLayout.Allocate(1600);
        Assert.Equal(TagPageLayout.DetailsMaxWidthDip, details);
        Assert.True(board > 900);
    }

    [Fact]
    public void ChipWrapSpacing_IsSix() =>
        Assert.Equal(6, TagPageLayout.ChipWrapSpacingDip);

    [Fact]
    public void ResolvePageMeasureWidth_Infinity_UsesHostNotChipNatural()
    {
        Assert.Equal(726, TagPageLayout.ResolvePageMeasureWidth(double.PositiveInfinity, 726), 0.01);
        var fallback = TagPageLayout.ResolvePageMeasureWidth(double.PositiveInfinity, 0);
        Assert.True(fallback < 900);
        Assert.Equal(
            TagPageLayout.SidebarWidthDip
                + TagPageLayout.DetailsWidthDip
                + TagPageLayout.BoardSoftMinWidthDip
                + TagPageLayout.GapsWidth
                + TagPageLayout.PagePaddingHorizontalDip,
            fallback,
            0.01);
    }

    [Fact]
    public void ResolvePageMeasureWidth_LargeFinite_CapsToHost() =>
        Assert.Equal(726, TagPageLayout.ResolvePageMeasureWidth(1239, 726), 0.01);

    [Fact]
    public void ResolvePageMeasureWidth_FiniteNoHost_KeepsAvailable() =>
        Assert.Equal(1014, TagPageLayout.ResolvePageMeasureWidth(1014, 0), 0.01);

    [Fact]
    public void Classify_PageWidth_WideNarrowStacked()
    {
        Assert.Equal(TagPageLayoutMode.Wide, TagPageLayout.ClassifyPageWidth(800));
        Assert.Equal(TagPageLayoutMode.Narrow, TagPageLayout.ClassifyPageWidth(759));
        Assert.Equal(TagPageLayoutMode.Narrow, TagPageLayout.ClassifyPageWidth(640));
        Assert.Equal(TagPageLayoutMode.Stacked, TagPageLayout.ClassifyPageWidth(639));
    }

    /// <summary>
    /// Real Desktop wrap-1024: window 1024 − nav 248 → host 776 (≥760 → Wide).
    /// Columns + gaps still equal content; details ≥ 240 stays in frame.
    /// </summary>
    [Fact]
    public void AllocateForPage_Window1024ExpandedNav_FitsWide()
    {
        var host = TagPageLayout.HostWidthAt1024ExpandedNavDip;
        Assert.Equal(776, host, 0.01);
        Assert.Equal(TagPageLayoutMode.Wide, TagPageLayout.ClassifyPageWidth(host));

        var (sidebar, board, details, mode) = TagPageLayout.AllocateForPage(host);
        Assert.Equal(TagPageLayoutMode.Wide, mode);
        var content = TagPageLayout.ContentWidthFromHostDip(host);
        Assert.Equal(content, sidebar + board + details + TagPageLayout.GapsWidth, 0.01);
        Assert.True(TagPageLayout.DetailsFitsActions(details));
        Assert.True(board > 0);
    }

    [Fact]
    public void AllocateForPage_Below760_NarrowSidebar180Details240()
    {
        var (sidebar, board, details, mode) = TagPageLayout.AllocateForPage(759);
        Assert.Equal(TagPageLayoutMode.Narrow, mode);
        Assert.Equal(TagPageLayout.NarrowSidebarWidthDip, sidebar, 0.01);
        Assert.Equal(TagPageLayout.DetailsMinWidthDip, details, 0.01);
        var content = TagPageLayout.ContentWidthFromHostDip(759);
        Assert.Equal(content, sidebar + board + details + TagPageLayout.GapsWidth, 0.01);
    }

    [Fact]
    public void AllocateForPage_Stacked_DetailsMatchesBoardColumn()
    {
        var (sidebar, board, details, mode) = TagPageLayout.AllocateForPage(600);
        Assert.Equal(TagPageLayoutMode.Stacked, mode);
        Assert.Equal(board, details, 0.01);
        Assert.True(sidebar <= TagPageLayout.NarrowSidebarWidthDip);
        var content = TagPageLayout.ContentWidthFromHostDip(600);
        Assert.Equal(content, sidebar + board + TagPageLayout.ColumnSpacingDip, 0.01);
    }

    /// <summary>
    /// Wimbly: fixed columns + pad + spacing ≤ 776 − 200 (host at 1024−nav, reserve board).
    /// </summary>
    [Fact]
    public void NarrowFixedChrome_FitsHost776MinusBoardReserve200()
    {
        var budget = TagPageLayout.HostWidthAt1024ExpandedNavDip
            - TagPageLayout.BoardReserveForChromeBudgetDip;
        Assert.Equal(576, budget, 0.01);
        Assert.True(
            TagPageLayout.NarrowFixedChromeDip <= budget + 0.01,
            $"fixed chrome {TagPageLayout.NarrowFixedChromeDip} > budget {budget}");
    }

    [Fact]
    public void Allocate_HideDetails_GivesBoardTheRest()
    {
        var (sidebar, board, details) = TagPageLayout.Allocate(800, showDetails: false);
        Assert.Equal(0, details);
        Assert.Equal(800, sidebar + board + TagPageLayout.GapsWidth, 0.01);
    }
}
