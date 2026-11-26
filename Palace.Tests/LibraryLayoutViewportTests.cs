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

    /// <summary>
    /// Pure park helper: boundaries from widths, not magic client-DIP constants.
    /// Order is Add to room → Undo → New folder → Organize → Review.
    /// </summary>
    [Fact]
    public void ResolveLibraryToolbarOverflow_ParksInPriorityUntilFits()
    {
        // always-on 200 + five 100-wide optionals = 700 when all on face.
        const double alwaysOn = 200;
        double[] widths = [100, 100, 100, 100, 100];

        var roomy = LibraryColumnLayout.ResolveLibraryToolbarOverflow(700, widths, alwaysOn);
        Assert.Equal(5, roomy.Length);
        Assert.All(roomy, p => Assert.False(p));

        // One DIP short of full face → park Add to room only.
        var oneShort = LibraryColumnLayout.ResolveLibraryToolbarOverflow(699, widths, alwaysOn);
        Assert.True(oneShort[0]);
        Assert.False(oneShort[1]);
        Assert.False(oneShort[2]);
        Assert.False(oneShort[3]);
        Assert.False(oneShort[4]);

        // Exactly always-on + last two (Organize+Review) = 400 → park first three.
        var mid = LibraryColumnLayout.ResolveLibraryToolbarOverflow(400, widths, alwaysOn);
        Assert.True(mid[0] && mid[1] && mid[2]);
        Assert.False(mid[3]);
        Assert.False(mid[4]);

        // Only always-on fits → park all five.
        var tight = LibraryColumnLayout.ResolveLibraryToolbarOverflow(alwaysOn, widths, alwaysOn);
        Assert.All(tight, p => Assert.True(p));

        // Under always-on still parks everything (caller keeps Delete rejected on face).
        var squeeze = LibraryColumnLayout.ResolveLibraryToolbarOverflow(alwaysOn - 1, widths, alwaysOn);
        Assert.All(squeeze, p => Assert.True(p));
    }

    [Fact]
    public void ResolveLibraryToolbarOverflow_DeleteRejectedWidensAlwaysOn_ParksEarlier()
    {
        // Same optional widths; Delete rejected on the face raises always-on so
        // Add to room parks at a width that previously fit.
        double[] widths = [120, 80, 100, 90, 90]; // sum 480
        const double alwaysWithout = 220;         // full face 700
        const double alwaysWithDelete = 220 + 140; // full face 840

        // 750 fits full face without Delete rejected (700), but with Delete
        // rejected (840) parks Add to room only (840 − 120 = 720 ≤ 750).
        const double available = 750;
        var without = LibraryColumnLayout.ResolveLibraryToolbarOverflow(
            available, widths, alwaysWithout);
        Assert.All(without, p => Assert.False(p));

        var withDelete = LibraryColumnLayout.ResolveLibraryToolbarOverflow(
            available, widths, alwaysWithDelete);
        Assert.True(withDelete[0]);
        Assert.False(withDelete[1]);
        Assert.False(withDelete[2]);
    }

    [Fact]
    public void ResolveLibraryToolbarOverflow_Hysteresis_BlocksFlickerOnExpand()
    {
        const double alwaysOn = 200;
        double[] widths = [100, 100, 100, 100, 100];
        var previous = LibraryColumnLayout.ResolveLibraryToolbarOverflow(500, widths, alwaysOn);
        // 500 → face needs park of first two (200+300=500 exact → 0 parked? 
        // FaceWidth(0)=700, park until fits: park0 → 600, park1 → 500. So 2 parked.
        Assert.True(previous[0] && previous[1]);
        Assert.False(previous[2]);

        // Grow by less than hysteresis — stay parked (no flicker).
        var still = LibraryColumnLayout.ResolveLibraryToolbarOverflow(
            500 + LibraryColumnLayout.LibraryToolbarOverflowHysteresisDip - 1,
            widths,
            alwaysOn,
            moreButtonWidth: 0,
            previous,
            LibraryColumnLayout.LibraryToolbarOverflowHysteresisDip);
        Assert.True(still[0] && still[1]);
        Assert.False(still[2]);

        // Grow by full face slack + hysteresis → unpark.
        var expanded = LibraryColumnLayout.ResolveLibraryToolbarOverflow(
            700 + LibraryColumnLayout.LibraryToolbarOverflowHysteresisDip,
            widths,
            alwaysOn,
            moreButtonWidth: 0,
            previous,
            LibraryColumnLayout.LibraryToolbarOverflowHysteresisDip);
        Assert.All(expanded, p => Assert.False(p));
    }

    [Fact]
    public void ShellNavPane_MatchesMainPageOpenAndCompactLengths()
    {
        Assert.Equal(248, LibraryColumnLayout.ShellNavPaneWidthDip);
        Assert.Equal(48, LibraryColumnLayout.ShellNavCompactPaneWidthDip);
        Assert.Equal(
            248,
            LibraryColumnLayout.ShellNavOccupiedWidthDip(ShellNavDisplayMode.Expanded, isPaneOpen: true),
            0.01);
        Assert.Equal(
            48,
            LibraryColumnLayout.ShellNavOccupiedWidthDip(ShellNavDisplayMode.Expanded, isPaneOpen: false),
            0.01);
    }

    [Fact]
    public void ContentWidthFromClientDip_CompactPane_WidensContentByOpenMinusCompact()
    {
        const double client = 1200;
        var openContent = LibraryColumnLayout.ContentWidthFromClientDip(
            client,
            LibraryColumnLayout.ShellNavPaneWidthDip);
        var compactContent = LibraryColumnLayout.ContentWidthFromClientDip(
            client,
            LibraryColumnLayout.ShellNavCompactPaneWidthDip);
        Assert.Equal(
            LibraryColumnLayout.ShellNavPaneWidthDip - LibraryColumnLayout.ShellNavCompactPaneWidthDip,
            compactContent - openContent,
            0.01);

        // At 1200 client with open pane Preview stays hidden; with compact rail
        // content gains 200 DIP and may dock — pack right edge still inside viewport.
        var rightCompact = LibraryColumnLayout.MosaicPackRightEdgeClientDip(
            client,
            LibraryColumnLayout.ShellNavCompactPaneWidthDip);
        Assert.True(
            rightCompact <= client - LibraryColumnLayout.PagePaddingEdgeDip + 0.01);
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

    /// <summary>
    /// Isiac hand repro via PreviewPane reducer: start narrow → widen past Preview →
    /// shrink below (Preview hides) → widen again. Pack tracks every step
    /// (available width, not grid ActualWidth). Toolbar overflow is measurement-
    /// driven separately (<see cref="ResolveLibraryToolbarOverflow_ParksInPriorityUntilFits"/>).
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

        s = PreviewPane.OnWidth(s, wideContent);
        var w1 = PreviewPane.Effective(s, wideContent);
        Assert.Equal(PaneMode.Docked, w1.Mode);
        Assert.False(w1.Widths.PreviewHidden);
        Assert.True(w1.Widths.Mosaic >= LibraryColumnLayout.MosaicMinWidth - 0.01);
        Assert.True(
            LibraryColumnLayout.MosaicPackRightEdgeClientDip(wideClient)
            <= wideClient - LibraryColumnLayout.PagePaddingEdgeDip + 0.01);

        // Shrink below hide threshold — Preview hides.
        s = PreviewPane.OnWidth(s, narrowContent);
        var n2 = PreviewPane.Effective(s, narrowContent);
        Assert.Equal(PaneMode.Hidden, n2.Mode);
        Assert.Equal(0, n2.Widths.Preview);
        Assert.Equal(n1.Widths.Mosaic, n2.Widths.Mosaic, 0.01);
        Assert.Equal(packN1, MosaicLayoutWidth.ApplyViewportGutter(n2.Widths.Mosaic), 0.01);

        // Widen again — tracking continues.
        s = PreviewPane.OnWidth(s, wideContent);
        var w2 = PreviewPane.Effective(s, wideContent);
        Assert.Equal(PaneMode.Docked, w2.Mode);
        Assert.Equal(w1.Widths.Mosaic, w2.Widths.Mosaic, 0.01);
        Assert.Equal(w1.Widths.Preview, w2.Widths.Preview, 0.01);
    }

    [Fact]
    public void ContentWidthFromHostDip_ExcludesOnlyPagePadding()
    {
        // Page/nav slot already excludes the shell pane.
        Assert.Equal(700, LibraryColumnLayout.ContentWidthFromHostDip(724), 0.01);
        Assert.Equal(0, LibraryColumnLayout.ContentWidthFromHostDip(10));
    }
}
