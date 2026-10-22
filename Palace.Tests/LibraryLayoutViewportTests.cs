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
    /// Isiac hand repro: start narrow → widen until Preview shows → shrink below
    /// Preview threshold → widen again. Tiles / pack / Preview visibility must
    /// track every step (layout must not freeze after Preview first appears).
    /// </summary>
    [Fact]
    public void Allocate_NarrowWidenPreviewNarrowWiden_TracksEachStep()
    {
        // Client DIPs: min window; wide enough for Preview (content ≥ tree-min+560+preview-min+gaps).
        const double narrowClient = 770;
        const double wideClient = 1400; // 1400 DIP client → content well above Preview show

        var narrowContent = LibraryColumnLayout.ContentWidthFromClientDip(narrowClient);
        var wideContent = LibraryColumnLayout.ContentWidthFromClientDip(wideClient);
        Assert.True(wideContent > narrowContent);
        Assert.False(LibraryColumnLayout.ShowsPreview(narrowContent));
        Assert.True(LibraryColumnLayout.ShowsPreview(wideContent));

        var n1 = LibraryColumnLayout.Allocate(narrowContent);
        Assert.True(n1.PreviewHidden);
        var packN1 = MosaicLayoutWidth.ApplyViewportGutter(n1.Mosaic);
        Assert.True(
            LibraryColumnLayout.MosaicPackRightEdgeClientDip(narrowClient)
            <= narrowClient - LibraryColumnLayout.PagePaddingEdgeDip + 0.01);

        var w1 = LibraryColumnLayout.Allocate(wideContent);
        Assert.False(w1.PreviewHidden);
        Assert.True(w1.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01);
        Assert.True(w1.Preview >= LibraryColumnLayout.PreviewMinWidth - 0.01);
        var packW1 = MosaicLayoutWidth.ApplyViewportGutter(w1.Mosaic);
        Assert.True(packW1 < packN1 || w1.Mosaic != n1.Mosaic); // layout changed when Preview appeared
        Assert.True(
            LibraryColumnLayout.MosaicPackRightEdgeClientDip(wideClient)
            <= wideClient - LibraryColumnLayout.PagePaddingEdgeDip + 0.01);

        // Shrink below Preview threshold — Preview must hide again and pack retarget.
        var n2 = LibraryColumnLayout.Allocate(narrowContent);
        Assert.True(n2.PreviewHidden);
        Assert.Equal(0, n2.Preview);
        Assert.Equal(n1.Mosaic, n2.Mosaic, 0.01);
        Assert.Equal(
            MosaicLayoutWidth.ApplyViewportGutter(n2.Mosaic),
            packN1,
            0.01);

        // Widen again — tracking continues (Preview returns, mosaic ≥560).
        var w2 = LibraryColumnLayout.Allocate(wideContent);
        Assert.False(w2.PreviewHidden);
        Assert.Equal(w1.Mosaic, w2.Mosaic, 0.01);
        Assert.Equal(w1.Preview, w2.Preview, 0.01);

        // Toolbar face at wide client; tight at narrow min.
        Assert.False(LibraryColumnLayout.IsLibraryToolbarTight(wideClient));
        Assert.True(LibraryColumnLayout.IsLibraryToolbarTight(narrowClient));
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
