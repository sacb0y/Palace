using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class LibraryColumnLayoutTests
{
    [Fact]
    public void Allocate_WideWindow_KeepsDefaults()
    {
        // Comfortable: tree 260 + mosaic ≥560 + preview 340 + gaps.
        var w = LibraryColumnLayout.Allocate(1400);
        Assert.Equal(260, w.Tree);
        Assert.Equal(340, w.Preview);
        Assert.False(w.PreviewHidden);
        Assert.True(w.Mosaic >= LibraryColumnLayout.MosaicMinWidth);
        Assert.Equal(
            1400 - LibraryColumnLayout.GapsWidth - 260 - 340,
            w.Mosaic,
            0.01);
    }

    [Fact]
    public void Allocate_ShrinksTreeBeforePreview()
    {
        // Just under defaults: need a little from the tree, preview stays 340.
        var defaults = LibraryColumnLayout.TreeDefaultWidth
            + LibraryColumnLayout.MosaicMinWidth
            + LibraryColumnLayout.PreviewDefaultWidth
            + LibraryColumnLayout.GapsWidth;
        var w = LibraryColumnLayout.Allocate(defaults - 40);
        Assert.True(w.Tree < LibraryColumnLayout.TreeDefaultWidth);
        Assert.True(w.Tree >= LibraryColumnLayout.TreeMinWidth);
        Assert.Equal(LibraryColumnLayout.PreviewDefaultWidth, w.Preview);
        Assert.False(w.PreviewHidden);
        Assert.Equal(LibraryColumnLayout.MosaicMinWidth, w.Mosaic, 0.01);
    }

    [Fact]
    public void Allocate_ShrinksPreviewAfterTreeMin()
    {
        var atTreeMin = LibraryColumnLayout.TreeMinWidth
            + LibraryColumnLayout.MosaicMinWidth
            + LibraryColumnLayout.PreviewDefaultWidth
            + LibraryColumnLayout.GapsWidth;
        var w = LibraryColumnLayout.Allocate(atTreeMin - 30);
        Assert.Equal(LibraryColumnLayout.TreeMinWidth, w.Tree);
        Assert.True(w.Preview < LibraryColumnLayout.PreviewDefaultWidth);
        Assert.True(w.Preview >= LibraryColumnLayout.PreviewMinWidth);
        Assert.False(w.PreviewHidden);
        Assert.Equal(LibraryColumnLayout.MosaicMinWidth, w.Mosaic, 0.01);
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(1250)]
    [InlineData(1400)]
    public void Allocate_LaptopNarrowWidths_NeverStealBelowMosaicMinWhilePreviewCanHide(double width)
    {
        // Content widths in the range that previously crushed the grid to a sliver.
        var w = LibraryColumnLayout.Allocate(width);
        if (!w.PreviewHidden)
        {
            Assert.True(w.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01);
        }
        else
        {
            // Preview gone: mosaic gets the rest after tree (+ one gap).
            Assert.Equal(0, w.Preview);
            Assert.True(w.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01
                || w.Tree <= LibraryColumnLayout.TreeMinWidth + 0.01);
            Assert.True(w.Mosaic > 100); // not a 31px sliver
        }
    }

    [Fact]
    public void Allocate_HidesPreviewWhenGridWouldDropBelow560()
    {
        // Tree at min + mosaic 560 + preview at min + 2 gaps — one DIP less → hide.
        var tight = LibraryColumnLayout.TreeMinWidth
            + LibraryColumnLayout.MosaicMinWidth
            + LibraryColumnLayout.PreviewMinWidth
            + LibraryColumnLayout.GapsWidth;
        var w = LibraryColumnLayout.Allocate(tight - 1);
        Assert.True(w.PreviewHidden);
        Assert.Equal(0, w.Preview);
        Assert.True(w.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01);
    }

    [Fact]
    public void Allocate_VeryNarrow_KeepsTreeMinAndGivesRestToMosaic()
    {
        var w = LibraryColumnLayout.Allocate(500);
        Assert.Equal(LibraryColumnLayout.TreeMinWidth, w.Tree);
        Assert.True(w.PreviewHidden);
        Assert.True(w.Mosaic > 0);
        // Window smaller than tree-min + 560: mosaic may be under floor, but not a sliver from sides.
        Assert.Equal(
            500 - LibraryColumnLayout.GapsWidth - LibraryColumnLayout.TreeMinWidth,
            w.Mosaic,
            0.01);
        // Column MinWidth must not demand more than allocated (would clip).
        Assert.Equal(w.Mosaic, LibraryColumnLayout.MosaicColumnMinWidth(w.Mosaic), 0.01);
        Assert.True(LibraryColumnLayout.MosaicColumnMinWidth(w.Mosaic) < LibraryColumnLayout.MosaicMinWidth);
    }

    [Fact]
    public void MosaicColumnMinWidth_CapsAt560AndNeverExceedsAllocated()
    {
        Assert.Equal(560, LibraryColumnLayout.MosaicColumnMinWidth(800));
        Assert.Equal(316, LibraryColumnLayout.MosaicColumnMinWidth(316));
        Assert.Equal(0, LibraryColumnLayout.MosaicColumnMinWidth(0));
        Assert.Equal(0, LibraryColumnLayout.MosaicColumnMinWidth(-10));
    }

    [Fact]
    public void MinWindowWidthDip_CoversTreeMinPlusMosaicMinPlusGapsAndPad()
    {
        // ~160+560+24 gaps + ~24 page pad ≈ 768; floor is 770.
        var contentFloor = LibraryColumnLayout.TreeMinWidth
            + LibraryColumnLayout.MosaicMinWidth
            + LibraryColumnLayout.GapsWidth;
        Assert.True(LibraryColumnLayout.MinWindowWidthDip >= contentFloor);
        Assert.Equal(770, LibraryColumnLayout.MinWindowWidthDip);
        Assert.True(LibraryColumnLayout.LibraryToolbarOverflowBelowDip > LibraryColumnLayout.MinWindowWidthDip);
        Assert.Equal(920, LibraryColumnLayout.LibraryToolbarOverflowBelowDip);
        Assert.Equal(800, LibraryColumnLayout.LibraryToolbarTightBelowDip);
        Assert.Equal(24, LibraryColumnLayout.PagePaddingHorizontalDip);
        Assert.Equal(12, LibraryColumnLayout.PagePaddingEdgeDip);
        Assert.Equal(248, LibraryColumnLayout.ShellNavPaneWidthDip);
        Assert.Equal(48, LibraryColumnLayout.ShellNavCompactPaneWidthDip);
        Assert.True(LibraryColumnLayout.MosaicToolbarFlattenBelowDip > LibraryColumnLayout.MosaicToolbarSortCompactBelowDip);
        Assert.True(LibraryColumnLayout.MosaicToolbarSortCompactBelowDip > LibraryColumnLayout.MosaicToolbarSortOverflowBelowDip);
    }

    [Theory]
    [InlineData(ShellNavDisplayMode.Expanded, true, 248)]
    [InlineData(ShellNavDisplayMode.Expanded, false, 48)]
    [InlineData(ShellNavDisplayMode.Compact, true, 48)]
    [InlineData(ShellNavDisplayMode.Compact, false, 48)]
    [InlineData(ShellNavDisplayMode.Minimal, true, 0)]
    [InlineData(ShellNavDisplayMode.Minimal, false, 0)]
    public void ShellNavOccupiedWidthDip_TracksDisplayModeAndOpen(
        ShellNavDisplayMode mode,
        bool isOpen,
        double expected)
    {
        Assert.Equal(
            expected,
            LibraryColumnLayout.ShellNavOccupiedWidthDip(mode, isOpen),
            0.01);
    }

    [Fact]
    public void ContentWidthFromClientDip_UsesPassedPaneWidth()
    {
        const double client = 1200;
        var open = LibraryColumnLayout.ContentWidthFromClientDip(
            client,
            LibraryColumnLayout.ShellNavPaneWidthDip);
        var compact = LibraryColumnLayout.ContentWidthFromClientDip(
            client,
            LibraryColumnLayout.ShellNavCompactPaneWidthDip);
        Assert.Equal(
            client - 248 - LibraryColumnLayout.PagePaddingHorizontalDip,
            open,
            0.01);
        Assert.Equal(
            client - 48 - LibraryColumnLayout.PagePaddingHorizontalDip,
            compact,
            0.01);
        // Collapsed hamburger frees ~200 DIP — Preview threshold must shift with it.
        Assert.Equal(200, compact - open, 0.01);
    }

    [Fact]
    public void PreferLiveClientDip_PrefersMin_DebugCrossCheckOnly()
    {
        // Debug helper only — layout uses page ActualWidth, not this.
        Assert.Equal(
            1003,
            LibraryColumnLayout.PreferLiveClientDip(1920, 1003),
            0.01);
        Assert.Equal(
            909,
            LibraryColumnLayout.PreferLiveClientDip(1400, 909),
            0.01);
        Assert.Equal(1003, LibraryColumnLayout.PreferLiveClientDip(0, 1003), 0.01);
        Assert.Equal(1003, LibraryColumnLayout.PreferLiveClientDip(1003, 0), 0.01);
        Assert.Equal(0, LibraryColumnLayout.PreferLiveClientDip(32, 16), 0.01);
    }

    /// <summary>
    /// Studio smoking gun @150%: HWND ~911 DIP vs XamlRoot.Size=1352 (≈911×1.5).
    /// Content DIP math must ignore display scale — inputs are already DIP.
    /// Treating (clientDip×scale) as DIP would inflate past AutoHideBelow.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void ContentWidthFromClientDip_IndependentOfDisplayScale(double scale)
    {
        const double hwndClientDip = 911;
        const double nav = LibraryColumnLayout.ShellNavPaneWidthDip;
        var content = LibraryColumnLayout.ContentWidthFromClientDip(hwndClientDip, nav);
        Assert.Equal(
            hwndClientDip - nav - LibraryColumnLayout.PagePaddingHorizontalDip,
            content,
            0.01);
        Assert.True(content < PreviewPane.AutoHideBelowContentDip);

        // Wrong: feed physical-looking width (DIP×scale) as if it were DIP.
        var inflatedClient = hwndClientDip * scale;
        var inflatedContent = LibraryColumnLayout.ContentWidthFromClientDip(inflatedClient, nav);
        if (scale >= 1.5)
        {
            Assert.True(
                inflatedContent >= PreviewPane.AutoHideBelowContentDip,
                $"scale {scale}: inflated content {inflatedContent} should clear AutoHideBelow (documents the Studio bug)");
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void ContentWidthFromHostDip_PageActualWidth_IndependentOfDisplayScale(double scale)
    {
        // Page ActualWidth excludes nav. Studio ~911 client − 248 nav ≈ 663 host DIP.
        const double pageActualDip = 911 - LibraryColumnLayout.ShellNavPaneWidthDip;
        var content = LibraryColumnLayout.ContentWidthFromHostDip(pageActualDip);
        Assert.Equal(
            pageActualDip - LibraryColumnLayout.PagePaddingHorizontalDip,
            content,
            0.01);
        Assert.True(content < PreviewPane.AutoHideBelowContentDip);

        // scale must not be multiplied into host DIP (Would be double-correct).
        _ = scale;
        var again = LibraryColumnLayout.ContentWidthFromHostDip(pageActualDip);
        Assert.Equal(content, again, 0.01);
    }

    [Theory]
    [InlineData(911, 1.5, 1352)]
    [InlineData(1005, 1.5, 1492)]
    [InlineData(782, 1.5, 1158)]
    public void ContentWidthFromHostDip_ScaledXamlRootSignal_MustNotDriveLayout(
        double hwndClientDip,
        double scale,
        double xamlRootReported)
    {
        // Probe: xamlRootReported ≈ hwndClientDip × scale (±harness chrome). Layout
        // must use page host DIP from HWND client, not the inflated root reading.
        Assert.InRange(xamlRootReported / hwndClientDip, scale - 0.05, scale + 0.05);

        var pageHost = hwndClientDip - LibraryColumnLayout.ShellNavPaneWidthDip;
        var fromPage = LibraryColumnLayout.ContentWidthFromHostDip(pageHost);
        var fromInflatedRoot = LibraryColumnLayout.ContentWidthFromClientDip(xamlRootReported);

        Assert.True(fromPage < PreviewPane.AutoHideBelowContentDip);
        Assert.True(PreviewPane.OnWidth(PreviewPane.Initial, fromPage).AutoHidden);

        // Inflated root is the lying input that kept AutoHidden=false @900/@1000.
        if (hwndClientDip >= 900)
        {
            Assert.True(fromInflatedRoot >= PreviewPane.AutoHideBelowContentDip);
            Assert.False(PreviewPane.OnWidth(PreviewPane.Initial, fromInflatedRoot).AutoHidden);
        }
        else
        {
            // ~782×1.5 still yields content under AutoHideBelow → Overlay PASS in probe.
            Assert.True(fromInflatedRoot < PreviewPane.AutoHideBelowContentDip);
        }
    }
}
