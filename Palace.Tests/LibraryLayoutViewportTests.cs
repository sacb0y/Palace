using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Laptop retest: 770 DIP client @ 150% scale ≈ 1155 physical px — mosaic and
/// chrome must fit the content viewport (pad + gaps + scrollbar gutter).
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

    [Fact]
    public void Allocate_At770DipClient_FitsPadGapsAndGutter()
    {
        // GrdLibraryColumns ActualWidth ≈ client; columns lay out inside Padding.
        var content = LibraryColumnLayout.MinWindowWidthDip
            - LibraryColumnLayout.PagePaddingHorizontalDip;
        Assert.Equal(746, content, 0.01);

        var w = LibraryColumnLayout.Allocate(content);
        Assert.True(w.PreviewHidden);
        Assert.Equal(
            content,
            w.Tree + w.Mosaic + w.Preview + LibraryColumnLayout.GapsWidth,
            0.01);

        var pack = MosaicLayoutWidth.ApplyViewportGutter(w.Mosaic);
        Assert.True(pack < w.Mosaic);
        Assert.Equal(w.Mosaic - MosaicLayoutWidth.ViewportGutterDip, pack, 0.01);

        // Justified row must not extend past the guttered pack width.
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

    [Theory]
    [InlineData(1400, 1.0)]
    [InlineData(1250, 1.0)]
    [InlineData(1155, 1.5)] // min outer @ 150% → 770 DIP client if script uses physical px
    [InlineData(900, 1.0)]
    public void Allocate_LaptopScriptWidths_PackGutteredInsideMosaic(
        double reportedPx,
        double scale)
    {
        // Script resize reports physical px; layout is DIP.
        var clientDip = reportedPx / scale;
        var content = Math.Max(0, clientDip - LibraryColumnLayout.PagePaddingHorizontalDip);
        var w = LibraryColumnLayout.Allocate(content);
        var pack = MosaicLayoutWidth.ApplyViewportGutter(w.Mosaic);
        Assert.True(pack <= w.Mosaic);
        Assert.True(
            w.Tree + w.Mosaic + w.Preview + LibraryColumnLayout.GapsWidth
            <= content + 0.01);
    }

    [Fact]
    public void ToolbarTightThreshold_CoversMinWindowContentWidth()
    {
        var contentAtMin = LibraryColumnLayout.MinWindowWidthDip
            - LibraryColumnLayout.PagePaddingHorizontalDip;
        Assert.True(contentAtMin < LibraryColumnLayout.LibraryToolbarTightBelowDip);
        Assert.True(LibraryColumnLayout.LibraryToolbarTightBelowDip
            < LibraryColumnLayout.LibraryToolbarOverflowBelowDip);
    }
}
