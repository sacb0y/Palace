namespace Palace.Helpers;

/// <summary>
/// Library page three-column widths. The mosaic/grid column keeps a hard
/// minimum (<see cref="MosaicMinWidth"/>); the folder tree shrinks next, then
/// the Preview pane (down to <see cref="PreviewMinWidth"/>, then auto-hidden).
/// Matches the Preview panel toggle rule (0.3.12): auto-hide when the grid would
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
    /// <c>MainPage</c> <c>NavMain.OpenPaneLength</c> — default when the Left pane
    /// is expanded. Compact hamburger leaves only <see cref="ShellNavCompactPaneWidthDip"/>.
    /// </summary>
    public const double ShellNavPaneWidthDip = 248;

    /// <summary>
    /// <c>NavigationView</c> default <c>CompactPaneLength</c> when the Left pane
    /// is collapsed (hamburger rail).
    /// </summary>
    public const double ShellNavCompactPaneWidthDip = 48;

    /// <summary>
    /// Extra DIP required before un-parking a face button (expand). Shrink parks
    /// as soon as desired &gt; available so Add to room is not clipped mid-label.
    /// </summary>
    public const double LibraryToolbarOverflowHysteresisDip = 24;

    /// <summary>
    /// Live face must leave this many DIP before the Preview toggle's left edge
    /// (matches Studio BoundingRectangle gate). Subtracted from available width.
    /// </summary>
    public const double LibraryToolbarPreviewGapDip = 4;

    /// <summary>
    /// Park priority length for <see cref="ResolveLibraryToolbarOverflow"/>:
    /// Add to room → Undo → New folder → Organize → Review.
    /// </summary>
    public const int LibraryToolbarOptionalCount = 5;

    /// <summary>
    /// Horizontal page padding on <c>GrdLibraryColumns</c>.
    /// Keep in sync with LibraryPage.xaml <c>Padding="12,8,12,8"</c> (Left+Right).
    /// </summary>
    public const double PagePaddingHorizontalDip = 24;

    /// <summary>Left (or right) pad alone — half of <see cref="PagePaddingHorizontalDip"/>.</summary>
    public const double PagePaddingEdgeDip = 12;

    /// <summary>
    /// Measurement-driven Library toolbar overflow. Parks optional face buttons
    /// into More in fixed priority until always-on + remaining ≤ available.
    /// <paramref name="orderedButtonWidths"/> is park-first order (index 0 parks
    /// first): Add to room → Undo → New folder → Organize → Review. Each entry is
    /// that button's contribution when visible (width + leading StackPanel spacing).
    /// <paramref name="alwaysOnWidth"/> is Add folder / Scan / Delete rejected (when
    /// on the face, full label) — never parked into More.
    /// </summary>
    public static LibraryToolbarOverflowPlan ResolveLibraryToolbarOverflow(
        double available,
        IReadOnlyList<double> orderedButtonWidths,
        double alwaysOnWidth) =>
        ResolveLibraryToolbarOverflow(
            available,
            orderedButtonWidths,
            alwaysOnWidth,
            moreButtonWidth: 0,
            previous: null,
            expandHysteresisDip: 0,
            alwaysOnIconOnlyWidth: alwaysOnWidth,
            previousDeleteRejectedIconOnly: false);

    /// <summary>
    /// Same as <see cref="ResolveLibraryToolbarOverflow(double,IReadOnlyList{double},double)"/>
    /// plus More-button contribution, expand hysteresis, and Delete rejected
    /// icon+count last resort when every optional is parked and the full face
    /// still overflows (count stays visible; never park Delete rejected into More).
    /// </summary>
    public static LibraryToolbarOverflowPlan ResolveLibraryToolbarOverflow(
        double available,
        IReadOnlyList<double> orderedButtonWidths,
        double alwaysOnWidth,
        double moreButtonWidth,
        bool[]? previous,
        double expandHysteresisDip,
        double alwaysOnIconOnlyWidth = -1,
        bool previousDeleteRejectedIconOnly = false)
    {
        var n = orderedButtonWidths?.Count ?? 0;
        if (n == 0)
        {
            return new LibraryToolbarOverflowPlan([], DeleteRejectedIconOnly: false);
        }

        var widths = orderedButtonWidths!;
        var always = FiniteNonNeg(alwaysOnWidth);
        var more = FiniteNonNeg(moreButtonWidth);
        var avail = FiniteNonNeg(available);
        var hyst = FiniteNonNeg(expandHysteresisDip);
        var alwaysIcon = alwaysOnIconOnlyWidth >= 0
            ? FiniteNonNeg(alwaysOnIconOnlyWidth)
            : always;
        if (alwaysIcon > always)
        {
            alwaysIcon = always;
        }

        // Shrink: park from the front until the face fits (More counted when any parked).
        var parked = ParkUntilFits(avail, widths, always, more);

        if (previous is not null && previous.Length == n && hyst > 0)
        {
            var prevCount = CountParked(previous);
            var nextCount = CountParked(parked);
            if (nextCount < prevCount)
            {
                // Expand only when the wider face still fits under available − hysteresis.
                var withHyst = ParkUntilFits(Math.Max(0, avail - hyst), widths, always, more);
                parked = CountParked(withHyst) < prevCount ? withHyst : previous;
            }
        }

        var iconOnly = ResolveDeleteRejectedIconOnly(
            avail,
            widths,
            always,
            alwaysIcon,
            more,
            parked,
            previousDeleteRejectedIconOnly,
            hyst);

        return new LibraryToolbarOverflowPlan(parked, iconOnly);
    }

    /// <summary>
    /// Last resort when every optional is in More and full always-on + More still
    /// exceeds available: collapse Delete rejected to icon+count on the face (never
    /// park it into More; never drop the count). Expand back only past hysteresis.
    /// </summary>
    private static bool ResolveDeleteRejectedIconOnly(
        double available,
        IReadOnlyList<double> widths,
        double alwaysOnFull,
        double alwaysOnIconOnly,
        double moreButtonWidth,
        bool[] parked,
        bool previousIconOnly,
        double expandHysteresisDip)
    {
        if (alwaysOnIconOnly >= alwaysOnFull - 0.01)
        {
            return false;
        }

        var allParked = CountParked(parked) == widths.Count;
        if (!allParked)
        {
            return false;
        }

        var fullFace = FaceWidth(widths, alwaysOnFull, moreButtonWidth, parkCount: widths.Count);

        if (fullFace <= available)
        {
            // Room for the full label — clear compact mode, with hysteresis when expanding.
            if (!previousIconOnly)
            {
                return false;
            }

            // Stay compact until full label fits under available − hysteresis.
            return fullFace > Math.Max(0, available - expandHysteresisDip);
        }

        // Full face overflows — keep/enter icon+count even when that also overflows
        // (never restore the wider full label; Doktor / 01-770).
        return true;
    }

    private static bool[] ParkUntilFits(
        double available,
        IReadOnlyList<double> widths,
        double alwaysOnWidth,
        double moreButtonWidth)
    {
        var n = widths.Count;
        var parked = new bool[n];
        var parkCount = 0;
        while (parkCount < n
               && FaceWidth(widths, alwaysOnWidth, moreButtonWidth, parkCount) > available)
        {
            parked[parkCount] = true;
            parkCount++;
        }

        return parked;
    }

    private static double FaceWidth(
        IReadOnlyList<double> widths,
        double alwaysOnWidth,
        double moreButtonWidth,
        int parkCount)
    {
        var sum = alwaysOnWidth;
        for (var i = parkCount; i < widths.Count; i++)
        {
            var w = widths[i];
            if (double.IsFinite(w) && w > 0)
            {
                sum += w;
            }
        }

        if (parkCount > 0 && moreButtonWidth > 0)
        {
            sum += moreButtonWidth;
        }

        return sum;
    }

    private static int CountParked(bool[] parked)
    {
        var n = 0;
        for (var i = 0; i < parked.Length; i++)
        {
            if (parked[i])
            {
                n++;
            }
        }

        return n;
    }

    private static double FiniteNonNeg(double value) =>
        double.IsFinite(value) && value > 0 ? value : 0;

    /// <summary>
    /// Width the shell <c>NavigationView</c> currently occupies from the window
    /// client (DIP). Minimal overlays content (0). Compact keeps the rail.
    /// Expanded uses open vs compact length from <paramref name="isPaneOpen"/>.
    /// </summary>
    public static double ShellNavOccupiedWidthDip(
        ShellNavDisplayMode displayMode,
        bool isPaneOpen,
        double openPaneLength = ShellNavPaneWidthDip,
        double compactPaneLength = ShellNavCompactPaneWidthDip)
    {
        var open = double.IsFinite(openPaneLength) && openPaneLength >= 0
            ? openPaneLength
            : ShellNavPaneWidthDip;
        var compact = double.IsFinite(compactPaneLength) && compactPaneLength >= 0
            ? compactPaneLength
            : ShellNavCompactPaneWidthDip;

        return displayMode switch
        {
            ShellNavDisplayMode.Minimal => 0,
            ShellNavDisplayMode.Compact => compact,
            _ => isPaneOpen ? open : compact,
        };
    }

    /// <summary>
    /// Library columns content width from a window client DIP size.
    /// Pass the live nav pane width (<see cref="ShellNavOccupiedWidthDip"/>) —
    /// hard-coding open length left ~200 DIP dead space when the hamburger collapsed.
    /// </summary>
    public static double ContentWidthFromClientDip(
        double windowClientDip,
        double navPaneWidthDip = ShellNavPaneWidthDip)
    {
        if (!double.IsFinite(windowClientDip) || windowClientDip <= 0)
        {
            return 0;
        }

        var pane = double.IsFinite(navPaneWidthDip) && navPaneWidthDip > 0
            ? navPaneWidthDip
            : 0;
        var host = Math.Max(0, windowClientDip - pane);
        return ContentWidthFromHostDip(host);
    }

    /// <summary>
    /// Library columns content width from the shell content slot ActualWidth
    /// (Frame / ContentPresenter — already excludes the shell pane). Prefer this
    /// over page ActualWidth (sibling overlay reflow) and over the columns grid's
    /// own ActualWidth (latches when Preview docks).
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
    /// Debug helper: when XamlRoot DIP and AppWindow/scale DIP disagree, the
    /// smaller usable value. <b>Not</b> the layout source — LibraryPage drives
    /// OnWidth from the shell content slot (Frame) ActualWidth. Never divide
    /// <c>XamlRoot.Size</c> by scale (Size is already DIP). Values below
    /// <paramref name="minUsable"/> ignored.
    /// </summary>
    public static double PreferLiveClientDip(
        double xamlRootDip,
        double appWindowClientDip,
        double minUsable = MosaicLayoutWidth.MinUsable)
    {
        var root = double.IsFinite(xamlRootDip) && xamlRootDip >= minUsable
            ? xamlRootDip
            : 0;
        var app = double.IsFinite(appWindowClientDip) && appWindowClientDip >= minUsable
            ? appWindowClientDip
            : 0;
        if (root > 0 && app > 0)
        {
            return Math.Min(root, app);
        }

        return root > 0 ? root : app;
    }

    /// <summary>
    /// Client-X of the packed mosaic's right edge (nav + pad + tree + gap + pack).
    /// Must stay ≤ <paramref name="windowClientDip"/> − right page pad.
    /// </summary>
    public static double MosaicPackRightEdgeClientDip(
        double windowClientDip,
        double navPaneWidthDip = ShellNavPaneWidthDip)
    {
        var content = ContentWidthFromClientDip(windowClientDip, navPaneWidthDip);
        var state = PreviewPane.OnWidth(PreviewPane.Initial, content);
        var widths = PreviewPane.Effective(state, content).Widths;
        var pack = MosaicLayoutWidth.ApplyViewportGutter(widths.Mosaic);
        var pane = double.IsFinite(navPaneWidthDip) && navPaneWidthDip > 0
            ? navPaneWidthDip
            : 0;
        return pane
            + PagePaddingEdgeDip
            + widths.Tree
            + ColumnSpacing
            + pack;
    }

    /// <summary>
    /// True when the #134 Preview reducer docks Preview at this content width
    /// (UserShow true, starting from <see cref="PreviewPane.Initial"/>).
    /// </summary>
    public static bool ShowsPreview(double contentWidth)
    {
        var state = PreviewPane.OnWidth(PreviewPane.Initial, contentWidth);
        return PreviewPane.Effective(state, contentWidth).Mode == PaneMode.Docked;
    }

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
    /// One-shot allocate (legacy tests): dock Preview when it fits, else hide.
    /// Prefer <see cref="PreviewPane.OnWidth"/> + <see cref="PreviewPane.Effective"/>
    /// for hysteresis and UserShow.
    /// </summary>
    public static LibraryColumnWidths Allocate(double availableWidth)
    {
        var state = PreviewPane.OnWidth(PreviewPane.Initial, availableWidth);
        return PreviewPane.Effective(state, availableWidth).Widths;
    }

    /// <summary>
    /// Dock Preview at <paramref name="previewWidth"/> (clamped). Shrinks tree then
    /// preview toward minima so mosaic stays ≥560; does not fully hide Preview
    /// (caller already decided Docked).
    /// </summary>
    public static LibraryColumnWidths AllocateWithPreview(double availableWidth, double previewWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            return new LibraryColumnWidths(
                TreeDefaultWidth,
                MosaicMinWidth,
                PreviewPane.ClampUserWidth(previewWidth),
                PreviewHidden: false);
        }

        var usable = Math.Max(0, availableWidth - GapsWidth);
        var tree = TreeDefaultWidth;
        var preview = PreviewPane.ClampUserWidth(previewWidth);
        var mosaic = usable - tree - preview;

        if (mosaic >= MosaicMinWidth)
        {
            return new LibraryColumnWidths(tree, mosaic, preview, PreviewHidden: false);
        }

        var need = MosaicMinWidth - mosaic;
        var treeCut = Math.Min(need, Math.Max(0, tree - TreeMinWidth));
        tree -= treeCut;
        mosaic += treeCut;
        if (mosaic >= MosaicMinWidth)
        {
            return new LibraryColumnWidths(tree, mosaic, preview, PreviewHidden: false);
        }

        need = MosaicMinWidth - mosaic;
        var previewCut = Math.Min(need, Math.Max(0, preview - PreviewMinWidth));
        preview -= previewCut;
        mosaic += previewCut;
        if (mosaic >= MosaicMinWidth)
        {
            return new LibraryColumnWidths(tree, mosaic, preview, PreviewHidden: false);
        }

        // Safety: still Docked but under floor — keep preview at min, mosaic gets rest.
        mosaic = Math.Max(0, usable - tree - preview);
        return new LibraryColumnWidths(tree, mosaic, preview, PreviewHidden: false);
    }

    /// <summary>Tree + mosaic only (Preview column width 0).</summary>
    public static LibraryColumnWidths AllocateWithoutPreview(double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            return new LibraryColumnWidths(
                TreeMinWidth,
                MosaicMinWidth,
                0,
                PreviewHidden: true);
        }

        var usable = Math.Max(0, availableWidth - GapsWidth);
        var tree = TreeDefaultWidth;
        var mosaic = usable - tree;
        if (mosaic < MosaicMinWidth && tree > TreeMinWidth)
        {
            var extra = Math.Min(tree - TreeMinWidth, MosaicMinWidth - mosaic);
            tree -= extra;
            mosaic += extra;
        }

        if (mosaic < 0 && tree > TreeMinWidth)
        {
            var cut = Math.Min(tree - TreeMinWidth, -mosaic);
            tree -= cut;
            mosaic += cut;
        }

        return new LibraryColumnWidths(
            tree,
            Math.Max(0, mosaic),
            0,
            PreviewHidden: true);
    }
}


/// <summary>
/// Optional buttons parked into More, plus Delete rejected icon-only last resort.
/// </summary>
public readonly record struct LibraryToolbarOverflowPlan(
    bool[] Parked,
    bool DeleteRejectedIconOnly)
{
    public bool AnyParked
    {
        get
        {
            if (Parked is null)
            {
                return false;
            }

            for (var i = 0; i < Parked.Length; i++)
            {
                if (Parked[i])
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>Resolved Library column widths for one arrange pass.</summary>
public readonly record struct LibraryColumnWidths(
    double Tree,
    double Mosaic,
    double Preview,
    bool PreviewHidden);

/// <summary>
/// WinUI-free mirror of <c>NavigationViewDisplayMode</c> for
/// <see cref="LibraryColumnLayout.ShellNavOccupiedWidthDip"/>.
/// </summary>
public enum ShellNavDisplayMode
{
    Minimal = 0,
    Compact = 1,
    Expanded = 2,
}
