using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Laptop retest: window client DIP (script sizes at 150%) → nav pane → page pad →
/// columns → mosaic pack. Right edge must stay inside viewport − padding; toolbar
/// tight/compact use window client DIP (not Library grid width).
/// </summary>
public sealed class LibraryLayoutViewportTests
{
    [Fact]
    public void MinWindow770Dip_At150Percent_Is1155Physical()
    {
        const double scale = 1.5;
        Assert.Equal(
            1155,
            LibraryColumnLayout.MinWindowWidthDip * scale,
            0.01);
    }

    [Theory]
    [InlineData(770)]
    [InlineData(833)] // 1250 px @ 150%
    [InlineData(867)] // 1300 px @ 150%
    [InlineData(933)] // 1400 px @ 150%
    public void ClientDip_PackRightEdge_AtOrInsideViewportMinusPadding(double clientDip)
    {
        var right = LibraryColumnLayout.MosaicPackRightEdgeClientDip(clientDip);
        var limit = clientDip - LibraryColumnLayout.PagePaddingEdgeDip;
        Assert.True(
            right <= limit + 0.01,
            $"client={clientDip}: pack right {right} > viewport−pad {limit}");

        // Packed row must also fit the guttered mosaic column itself.
        var content = LibraryColumnLayout.ContentWidthFromClientDip(clientDip);
        var w = LibraryColumnLayout.Allocate(content);
        var pack = MosaicLayoutWidth.ApplyViewportGutter(w.Mosaic);
        Assert.True(pack <= w.Mosaic + 0.01);
        Assert.Equal(
            content,
            w.Tree + w.Mosaic + w.Preview + LibraryColumnLayout.GapsWidth,
            0.01);

        var items = new List<MosaicSpec>
        {
            new(MosaicSlotKind.FullHeader, 0, MiniWidth: 80),
            new(MosaicSlotKind.Asset, 1.2),
            new(MosaicSlotKind.Asset, 0.8),
            new(MosaicSlotKind.Asset, 1.0),
            new(MosaicSlotKind.Asset, 1.5)
        };
        var slots = MosaicRows.Layout(items, pack, rowHeight: 140, spacing: 8, headerHeight: 34);
        var maxRight = slots.Max(s => s.X + s.Width);
        Assert.True(maxRight <= pack + 0.01);
    }

    [Fact]
    public void CapToAllocatedMosaic_PreventsStaleWideActualWidth()
    {
        // After shrink, ColMosaic.ActualWidth can still report the prior wide extent.
        Assert.Equal(
            338,
            MosaicLayoutWidth.CapToAllocatedMosaic(measured: 744, allocatedMosaic: 338),
            0.01);
        Assert.Equal(
            500,
            MosaicLayoutWidth.CapToAllocatedMosaic(measured: 500, allocatedMosaic: 744),
            0.01);
        Assert.Equal(
            400,
            MosaicLayoutWidth.CapToAllocatedMosaic(measured: 0, allocatedMosaic: 400),
            0.01);
    }

    [Theory]
    [InlineData(933, false)]
    [InlineData(867, false)]
    [InlineData(833, false)]
    [InlineData(800, false)]
    [InlineData(799, true)]
    [InlineData(770, true)]
    public void ToolbarTight_UsesWindowClientDip(double clientDip, bool expectTight)
    {
        Assert.Equal(expectTight, LibraryColumnLayout.IsLibraryToolbarTight(clientDip));

        // Library grid width alone stays under 800 at these laptop sizes — that
        // was why Organize/Review stayed in More. Window client is the contract.
        var content = LibraryColumnLayout.ContentWidthFromClientDip(clientDip);
        Assert.True(content < LibraryColumnLayout.LibraryToolbarTightBelowDip);
    }

    [Theory]
    [InlineData(1000, false)]
    [InlineData(920, false)]
    [InlineData(919, true)]
    [InlineData(770, true)]
    public void ToolbarCompact_UsesWindowClientDip(double clientDip, bool expectCompact) =>
        Assert.Equal(expectCompact, LibraryColumnLayout.IsLibraryToolbarCompact(clientDip));

    [Fact]
    public void ShellNavPane_MatchesMainPageOpenPaneLength()
    {
        Assert.Equal(248, LibraryColumnLayout.ShellNavPaneWidthDip);
    }

    [Fact]
    public void Allocate_At770DipClient_FitsPadGapsAndGutter()
    {
        var content = LibraryColumnLayout.ContentWidthFromClientDip(
            LibraryColumnLayout.MinWindowWidthDip);
        Assert.Equal(
            LibraryColumnLayout.MinWindowWidthDip
                - LibraryColumnLayout.ShellNavPaneWidthDip
                - LibraryColumnLayout.PagePaddingHorizontalDip,
            content,
            0.01);

        var w = LibraryColumnLayout.Allocate(content);
        Assert.True(w.PreviewHidden);
        Assert.Equal(
            content,
            w.Tree + w.Mosaic + w.Preview + LibraryColumnLayout.GapsWidth,
            0.01);

        var pack = MosaicLayoutWidth.ApplyViewportGutter(w.Mosaic);
        Assert.True(pack < w.Mosaic);
        Assert.Equal(w.Mosaic - MosaicLayoutWidth.ViewportGutterDip, pack, 0.01);
    }

    [Theory]
    [InlineData(1400, 1.0)]
    [InlineData(1250, 1.0)]
    [InlineData(1155, 1.5)] // min outer @ 150% → 770 DIP client if script uses physical px
    [InlineData(900, 1.0)]
    public void Allocate_LaptopScriptWidths_PackGutteredInsideMosaic(
        double reportedPx,
        double scale)
    {
        var clientDip = reportedPx / scale;
        var content = LibraryColumnLayout.ContentWidthFromClientDip(clientDip);
        var w = LibraryColumnLayout.Allocate(content);
        var pack = MosaicLayoutWidth.ApplyViewportGutter(w.Mosaic);
        Assert.True(pack <= w.Mosaic);
        Assert.True(
            w.Tree + w.Mosaic + w.Preview + LibraryColumnLayout.GapsWidth
            <= content + 0.01);
        Assert.True(
            LibraryColumnLayout.MosaicPackRightEdgeClientDip(clientDip)
            <= clientDip - LibraryColumnLayout.PagePaddingEdgeDip + 0.01);
    }

    [Fact]
    public void ToolbarTightThreshold_BelowMinWindowClient()
    {
        Assert.True(
            LibraryColumnLayout.IsLibraryToolbarTight(LibraryColumnLayout.MinWindowWidthDip));
        Assert.True(
            LibraryColumnLayout.LibraryToolbarTightBelowDip
            < LibraryColumnLayout.LibraryToolbarOverflowBelowDip);
    }

    /// <summary>
    /// Isiac hand repro via PreviewPane reducer: start narrow → widen past Preview →
    /// shrink below (Preview hides) → widen again. Pack + both overflow thresholds
    /// track every step (available width, not grid ActualWidth).
    /// </summary>
    [Fact]
    public void PreviewPane_NarrowWidenPreviewNarrowWiden_TracksEachStep()
    {
        const double narrowClient = 770;
        const double wideClient = 1400;

        var narrowContent = LibraryColumnLayout.ContentWidthFromClientDip(narrowClient);
        var wideContent = LibraryColumnLayout.ContentWidthFromClientDip(wideClient);
        Assert.True(narrowContent < PreviewPane.AutoHideBelowContentDip);
        Assert.True(wideContent >= PreviewPane.AutoShowAboveContentDip);

        var s = PreviewPane.Initial;
        s = PreviewPane.OnWidth(s, narrowContent);
        var n1 = PreviewPane.Effective(s, narrowContent);
        Assert.Equal(PaneMode.Hidden, n1.Mode);
        Assert.True(n1.Widths.PreviewHidden);
        var packN1 = MosaicLayoutWidth.ApplyViewportGutter(n1.Widths.Mosaic);
        Assert.True(
            LibraryColumnLayout.MosaicPackRightEdgeClientDip(narrowClient)
            <= narrowClient - LibraryColumnLayout.PagePaddingEdgeDip + 0.01);
        Assert.True(LibraryColumnLayout.IsLibraryToolbarTight(narrowClient));

        s = PreviewPane.OnWidth(s, wideContent);
        var w1 = PreviewPane.Effective(s, wideContent);
        Assert.Equal(PaneMode.Docked, w1.Mode);
        Assert.False(w1.Widths.PreviewHidden);
        Assert.True(w1.Widths.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01);
        Assert.True(
            LibraryColumnLayout.MosaicPackRightEdgeClientDip(wideClient)
            <= wideClient - LibraryColumnLayout.PagePaddingEdgeDip + 0.01);
        Assert.False(LibraryColumnLayout.IsLibraryToolbarTight(wideClient));
        Assert.False(LibraryColumnLayout.IsLibraryToolbarCompact(wideClient));

        // Shrink below hide threshold — Preview hides; toolbar tight again.
        s = PreviewPane.OnWidth(s, narrowContent);
        var n2 = PreviewPane.Effective(s, narrowContent);
        Assert.Equal(PaneMode.Hidden, n2.Mode);
        Assert.Equal(0, n2.Widths.Preview);
        Assert.Equal(n1.Widths.Mosaic, n2.Widths.Mosaic, 0.01);
        Assert.Equal(packN1, MosaicLayoutWidth.ApplyViewportGutter(n2.Widths.Mosaic), 0.01);
        Assert.True(LibraryColumnLayout.IsLibraryToolbarTight(narrowClient));

        // Widen again — tracking continues.
        s = PreviewPane.OnWidth(s, wideContent);
        var w2 = PreviewPane.Effective(s, wideContent);
        Assert.Equal(PaneMode.Docked, w2.Mode);
        Assert.Equal(w1.Widths.Mosaic, w2.Widths.Mosaic, 0.01);
        Assert.Equal(w1.Widths.Preview, w2.Widths.Preview, 0.01);
        Assert.False(LibraryColumnLayout.IsLibraryToolbarTight(867));
        Assert.False(LibraryColumnLayout.IsLibraryToolbarTight(933));
    }

    [Fact]
    public void ContentWidthFromHostDip_ExcludesOnlyPagePadding()
    {
        // Page/nav slot already excludes the shell pane.
        Assert.Equal(700, LibraryColumnLayout.ContentWidthFromHostDip(724), 0.01);
        Assert.Equal(0, LibraryColumnLayout.ContentWidthFromHostDip(10));
    }
}
