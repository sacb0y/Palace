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
    /// XAML must not put a board MinWidth floor on top of sidebar + details —
    /// at ~1024 DIP with expanded nav the Tags strip is ~670 and a 320 board
    /// floor pushed ScrTagDetails past the HWND (Desktop #197).
    /// </summary>
    [Fact]
    public void XamlColumnFloors_Fit1024WithExpandedNav()
    {
        const double windowDip = 1024;
        const double expandedNavDip = 320;
        const double pagePadDip = 32; // Tags grid Padding 16+16
        var tagsStrip = windowDip - expandedNavDip - pagePadDip;
        // Board is Width=* with no MinWidth; only sidebar + details + gaps are fixed.
        var fixedColumns = TagPageLayout.SidebarWidthDip
            + 260 // details Width in TagsPage.xaml
            + TagPageLayout.GapsWidth;
        Assert.True(fixedColumns <= tagsStrip);
        Assert.True(tagsStrip - fixedColumns > 0);
        Assert.True(TagPageLayout.DetailsFitsActions(TagPageLayout.DetailsMinWidthDip));
    }
}
