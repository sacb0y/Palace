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

    /// <summary>
    /// Real Desktop wrap-1024: window client 1024 DIP @ 150%, expanded nav 248,
    /// Tags pad 32 → content strip must host sidebar|board|details with details ≥ 240.
    /// </summary>
    [Fact]
    public void Allocate_Window1024ExpandedNav_DetailsInFrame()
    {
        var content = TagPageLayout.ContentWidthFromClientDip(
            windowClientDip: 1024,
            navPaneWidthDip: TagPageLayout.ShellNavPaneWidthDip);
        Assert.Equal(1024 - TagPageLayout.ShellNavPaneWidthDip - TagPageLayout.PagePaddingHorizontalDip, content, 0.01);

        var (sidebar, board, details) = TagPageLayout.Allocate(content);
        Assert.Equal(content, sidebar + board + details + TagPageLayout.GapsWidth, 0.01);
        Assert.InRange(details, TagPageLayout.DetailsMinWidthDip, TagPageLayout.DetailsMaxWidthDip);
        Assert.True(sidebar <= TagPageLayout.SidebarWidthDip);
        Assert.True(sidebar >= TagPageLayout.SidebarMinWidthDip - 0.01);
        Assert.True(board > 0);
        Assert.True(TagPageLayout.DetailsFitsActions(details));
    }

    [Fact]
    public void Allocate_TightStrip_ShrinksSidebarBeforeClippingDetails()
    {
        // Below preferred sidebar+details+soft board — sidebar shrinks toward 160.
        var content = TagPageLayout.SidebarMinWidthDip
            + TagPageLayout.DetailsMinWidthDip
            + TagPageLayout.BoardSoftMinWidthDip
            + TagPageLayout.GapsWidth;
        var (sidebar, board, details) = TagPageLayout.Allocate(content);
        Assert.Equal(content, sidebar + board + details + TagPageLayout.GapsWidth, 0.01);
        Assert.Equal(TagPageLayout.SidebarMinWidthDip, sidebar, 0.01);
        Assert.True(TagPageLayout.DetailsFitsActions(details));
        Assert.True(board + 0.01 >= TagPageLayout.BoardSoftMinWidthDip);
    }

    [Fact]
    public void Allocate_HideDetails_GivesBoardTheRest()
    {
        var (sidebar, board, details) = TagPageLayout.Allocate(800, showDetails: false);
        Assert.Equal(0, details);
        Assert.Equal(800, sidebar + board + TagPageLayout.GapsWidth, 0.01);
    }
}
