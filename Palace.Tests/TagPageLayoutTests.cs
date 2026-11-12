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
}
