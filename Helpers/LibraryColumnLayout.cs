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
    /// Two column gaps — Grid <c>ColumnSpacing</c> still applies when Preview
    /// width is 0 (column stays in the grid for the queued toggle).
    /// </summary>
    public static double GapsWidth => ColumnSpacing * ColumnGapCount;

    /// <summary>
    /// Allocate tree / mosaic / preview for the Library columns' content width
    /// (the padded grid's <c>ActualWidth</c>). Never steals below
    /// <see cref="MosaicMinWidth"/> while preview can still shrink or hide.
    /// </summary>
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
