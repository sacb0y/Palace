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
    /// Below this Library content width (DIP), New folder / Undo / Add to room
    /// move into the top toolbar overflow menu.
    /// </summary>
    public const double LibraryToolbarOverflowBelowDip = 920;

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
