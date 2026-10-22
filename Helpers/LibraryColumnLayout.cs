namespace Palace.Helpers;

/// <summary>
/// Library page three-column widths. The mosaic/grid column keeps a hard
/// minimum (<see cref="MosaicMinWidth"/>); the folder tree shrinks next, then
/// the Preview pane (down to <see cref="PreviewMinWidth"/>, then auto-hidden).
/// Matches the queued Preview panel toggle rule: auto-hide when the grid would
/// drop below 560 DIP. Off WinUI for Palace.Tests.
/// </summary>
public static class LibraryColumnLayout
{
    /// <summary>Hard floor for the mosaic / grid column (Preview toggle spec).</summary>
    public const double MosaicMinWidth = 560;

    public const double TreeDefaultWidth = 260;
    public const double TreeMinWidth = 160;

    public const double PreviewDefaultWidth = 340;
    /// <summary>Preview edge-drag minimum from the queued toggle spec (280–560).</summary>
    public const double PreviewMinWidth = 280;

    public const double ColumnSpacing = 12;
    public const int ColumnGapCount = 2;

    /// <summary>
    /// Main window preferred minimum width (DIP). Covers tree-min + mosaic-min
    /// + gaps + Library page pad (~160+560+24+24 ≈ 768) so the grid is not clipped.
    /// </summary>
    public const int MinWindowWidthDip = 770;

    /// <summary>Main window preferred minimum height (DIP).</summary>
    public const int MinWindowHeightDip = 480;

    /// <summary>
    /// <c>MainPage</c> <c>NavMain.OpenPaneLength</c> — Library content width is
    /// window client minus this when the pane is open (Left display mode).
    /// </summary>
    public const double ShellNavPaneWidthDip = 248;

    /// <summary>
    /// Below this <b>window client</b> width (DIP), New folder / Undo / Add to room
    /// move into the top toolbar overflow menu. Laptop script sizes are client DIP
    /// (not Library grid width — that already excludes the nav pane).
    /// </summary>
    public const double LibraryToolbarOverflowBelowDip = 920;

    /// <summary>
    /// Below this <b>window client</b> width (DIP), also park Organize / Review in
    /// More. Face buttons show at ≥800 client DIP; comparing Library grid width
    /// instead kept them collapsed (grid ≈ client − 248).
    /// </summary>
    public const double LibraryToolbarTightBelowDip = 800;

    /// <summary>
    /// Horizontal page padding on <c>GrdLibraryColumns</c>.
    /// Keep in sync with LibraryPage.xaml <c>Padding="12,8,12,8"</c> (Left+Right).
    /// </summary>
    public const double PagePaddingHorizontalDip = 24;

    /// <summary>Left (or right) pad alone — half of <see cref="PagePaddingHorizontalDip"/>.</summary>
    public const double PagePaddingEdgeDip = 12;

    /// <summary>True when Organize / Review should leave the face toolbar.</summary>
    public static bool IsLibraryToolbarTight(double windowClientDip) =>
        windowClientDip < LibraryToolbarTightBelowDip;

    /// <summary>True when New folder / Undo / Add to room move into More.</summary>
    public static bool IsLibraryToolbarCompact(double windowClientDip) =>
        windowClientDip < LibraryToolbarOverflowBelowDip;

    /// <summary>
    /// Library columns content width from a window client DIP size (nav pane open).
    /// Matches page/nav content slot ≈ client − <see cref="ShellNavPaneWidthDip"/>,
    /// then minus horizontal page padding.
    /// </summary>
    public static double ContentWidthFromClientDip(double windowClientDip)
    {
        if (!double.IsFinite(windowClientDip) || windowClientDip <= 0)
        {
            return 0;
        }

        var host = Math.Max(0, windowClientDip - ShellNavPaneWidthDip);
        return ContentWidthFromHostDip(host);
    }

    /// <summary>
    /// Library columns content width from the page / nav content host ActualWidth
    /// (already excludes the shell pane). Prefer this over the columns grid's own
    /// ActualWidth — once Preview is shown, mosaic MinWidth + Preview can floor the
    /// grid DesiredSize so its ActualWidth stops tracking the window.
    /// </summary>
    public static double ContentWidthFromHostDip(double hostDip)
    {
        if (!double.IsFinite(hostDip) || hostDip <= 0)
        {
            return 0;
        }

        return Math.Max(0, hostDip - PagePaddingHorizontalDip);
    }

    /// <summary>
    /// Client-X of the packed mosaic's right edge (nav + pad + tree + gap + pack).
    /// Must stay ≤ <paramref name="windowClientDip"/> − right page pad.
    /// </summary>
    public static double MosaicPackRightEdgeClientDip(double windowClientDip)
    {
        var content = ContentWidthFromClientDip(windowClientDip);
        var widths = Allocate(content);
        var pack = MosaicLayoutWidth.ApplyViewportGutter(widths.Mosaic);
        return ShellNavPaneWidthDip
            + PagePaddingEdgeDip
            + widths.Tree
            + ColumnSpacing
            + pack;
    }

    /// <summary>
    /// True when <see cref="Allocate"/> keeps Preview visible for this content width.
    /// </summary>
    public static bool ShowsPreview(double contentWidth) => !Allocate(contentWidth).PreviewHidden;

    /// <summary>
    /// Below this mosaic column width (DIP), hide the Flatten face toggle
    /// (More menu keeps it). ~752 mosaic at 1400px still clipped Flatten.
    /// </summary>
    public const double MosaicToolbarFlattenBelowDip = 820;

    /// <summary>
    /// Below this mosaic column width (DIP), drop Sort MinWidth so the label
    /// can shrink (1250/1155 still clipped Sort at 200).
    /// </summary>
    public const double MosaicToolbarSortCompactBelowDip = 680;

    /// <summary>
    /// Below this mosaic column width (DIP), move Sort into the More menu.
    /// </summary>
    public const double MosaicToolbarSortOverflowBelowDip = 560;

    /// <summary>
    /// Two column gaps — Grid <c>ColumnSpacing</c> still applies when Preview
    /// width is 0 (column stays in the grid for the queued toggle).
    /// </summary>
    public static double GapsWidth => ColumnSpacing * ColumnGapCount;

    /// <summary>
    /// Column MinWidth for the mosaic: never demand more than the allocated
    /// width (avoids clipping when content is under 560 before the window min
    /// size has taken effect).
    /// </summary>
    public static double MosaicColumnMinWidth(double allocatedMosaic) =>
        Math.Min(MosaicMinWidth, Math.Max(0, allocatedMosaic));

    /// <summary>
    /// Allocate tree / mosaic / preview for the Library columns' content width
    /// (the padded grid's <c>ActualWidth</c>). Never steals below
    /// <see cref="MosaicMinWidth"/> while preview can still shrink or hide.
    /// </summary>
    /// <remarks>
    /// TODO(0.3.9 Preview panel toggle): honor a user-set Preview width
    /// (persisted 280–560) instead of <see cref="PreviewDefaultWidth"/>.
    /// </remarks>
    public static LibraryColumnWidths Allocate(double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            return new LibraryColumnWidths(
                TreeDefaultWidth,
                MosaicMinWidth,
                PreviewDefaultWidth,
                PreviewHidden: false);
        }

        var usable = Math.Max(0, availableWidth - GapsWidth);
        var tree = TreeDefaultWidth;
        // TODO(0.3.9): start from persisted Preview width when the toggle ships.
        var preview = PreviewDefaultWidth;
        var mosaic = usable - tree - preview;

        if (mosaic >= MosaicMinWidth)
        {
            return new LibraryColumnWidths(tree, mosaic, preview, PreviewHidden: false);
        }

        // 1) Shrink tree toward its minimum.
        var need = MosaicMinWidth - mosaic;
        var treeCut = Math.Min(need, Math.Max(0, tree - TreeMinWidth));
        tree -= treeCut;
        mosaic += treeCut;
        if (mosaic >= MosaicMinWidth)
        {
            return new LibraryColumnWidths(tree, mosaic, preview, PreviewHidden: false);
        }

        // 2) Shrink preview toward its drag minimum.
        need = MosaicMinWidth - mosaic;
        var previewCut = Math.Min(need, Math.Max(0, preview - PreviewMinWidth));
        preview -= previewCut;
        mosaic += previewCut;
        if (mosaic >= MosaicMinWidth)
        {
            return new LibraryColumnWidths(tree, mosaic, preview, PreviewHidden: false);
        }

        // 3) Auto-hide preview (same 560 rule as the queued Preview toggle).
        preview = 0;
        mosaic = usable - tree;
        if (mosaic < MosaicMinWidth && tree > TreeMinWidth)
        {
            var extra = Math.Min(tree - TreeMinWidth, MosaicMinWidth - mosaic);
            tree -= extra;
            mosaic += extra;
        }

        return new LibraryColumnWidths(
            tree,
            Math.Max(0, mosaic),
            preview,
            PreviewHidden: true);
    }
}

/// <summary>Resolved Library column widths for one arrange pass.</summary>
public readonly record struct LibraryColumnWidths(
    double Tree,
    double Mosaic,
    double Preview,
    bool PreviewHidden);
