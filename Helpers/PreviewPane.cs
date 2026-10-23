namespace Palace.Helpers;

/// <summary>
/// Pure Preview pane state for Library columns. #134 drives auto-hide from available
/// width (hysteresis); 0.3.9 adds toggle / pin / overlay UI on the same reducer.
/// Off WinUI for Palace.Tests.
/// </summary>
public enum PaneMode
{
    Hidden,
    Docked,
    Overlay
}

/// <summary>User + auto-hide intent for the Library Preview pane.</summary>
/// <param name="UserShow">User wants the pane; auto-hide must never overwrite this.</param>
/// <param name="AutoHidden">True when available width cannot keep mosaic ≥560 with Preview.</param>
/// <param name="OverlayOpen">0.3.9 floating overlay; cleared when auto-hide crosses.</param>
/// <param name="Pinned">0.3.9 pin; cleared when auto-hide crosses.</param>
/// <param name="UserWidth">Persisted dock width (280–560); default 340.</param>
public readonly record struct PreviewPaneState(
    bool UserShow,
    bool AutoHidden,
    bool OverlayOpen,
    bool Pinned,
    double UserWidth);

/// <summary>Result of <see cref="PreviewPane.Effective"/>.</summary>
public readonly record struct PreviewPaneEffective(
    PaneMode Mode,
    LibraryColumnWidths Widths);

/// <summary>Reducer + effective column layout for the Library Preview pane.</summary>
public static class PreviewPane
{
    /// <summary>DIP hysteresis between auto-hide and auto-show thresholds.</summary>
    public const double AutoHideHysteresisDip = 40;

    /// <summary>#134 default: user wants Preview; no overlay/pin UI yet.</summary>
    public static PreviewPaneState Initial { get; } = new(
        UserShow: true,
        AutoHidden: false,
        OverlayOpen: false,
        Pinned: false,
        UserWidth: LibraryColumnLayout.PreviewDefaultWidth);

    /// <summary>
    /// Content width below which Preview cannot dock while keeping mosaic ≥560
    /// even with Preview at <see cref="LibraryColumnLayout.PreviewMinWidth"/>.
    /// </summary>
    public static double AutoHideBelowContentDip =>
        LibraryColumnLayout.TreeMinWidth
        + LibraryColumnLayout.MosaicMinWidth
        + LibraryColumnLayout.PreviewMinWidth
        + LibraryColumnLayout.GapsWidth;

    /// <summary>Content width at/above which auto-hidden Preview may show again.</summary>
    public static double AutoShowAboveContentDip =>
        AutoHideBelowContentDip + AutoHideHysteresisDip;

    public static double ClampUserWidth(double width)
    {
        if (!double.IsFinite(width))
        {
            return LibraryColumnLayout.PreviewDefaultWidth;
        }

        return Math.Clamp(
            width,
            LibraryColumnLayout.PreviewMinWidth,
            LibraryColumnLayout.MosaicMinWidth);
    }

    /// <summary>
    /// Recompute <see cref="PreviewPaneState.AutoHidden"/> from available content width.
    /// Crossing the auto-hide band clears <see cref="PreviewPaneState.Pinned"/> and
    /// <see cref="PreviewPaneState.OverlayOpen"/>. Never changes UserShow.
    /// </summary>
    public static PreviewPaneState OnWidth(PreviewPaneState state, double contentWidth)
    {
        if (!double.IsFinite(contentWidth) || contentWidth < 0)
        {
            contentWidth = 0;
        }

        var hideBelow = AutoHideBelowContentDip;
        var showAbove = AutoShowAboveContentDip;
        var autoHidden = state.AutoHidden
            ? contentWidth < showAbove
            : contentWidth < hideBelow;
        var crossed = autoHidden != state.AutoHidden;
        return state with
        {
            AutoHidden = autoHidden,
            Pinned = crossed ? false : state.Pinned,
            OverlayOpen = crossed ? false : state.OverlayOpen
        };
    }

    /// <summary>User toggle — flips UserShow only.</summary>
    public static PreviewPaneState Toggle(PreviewPaneState state) =>
        state with { UserShow = !state.UserShow };

    /// <summary>User pin — does not touch AutoHidden / UserShow.</summary>
    public static PreviewPaneState Pin(PreviewPaneState state, bool pinned = true) =>
        state with { Pinned = pinned };

    /// <summary>User overlay open — does not touch AutoHidden / UserShow.</summary>
    public static PreviewPaneState SetOverlayOpen(PreviewPaneState state, bool open) =>
        state with { OverlayOpen = open };

    /// <summary>Persist dock width (clamped); does not touch auto-hide flags.</summary>
    public static PreviewPaneState SetUserWidth(PreviewPaneState state, double width) =>
        state with { UserWidth = ClampUserWidth(width) };

    /// <summary>
    /// Resolve pane mode + column widths for available content width.
    /// Overlay allocates with preview=0 (mosaic uses the full span).
    /// </summary>
    public static PreviewPaneEffective Effective(PreviewPaneState state, double contentWidth)
    {
        var width = ClampUserWidth(state.UserWidth);
        if (state.OverlayOpen && state.UserShow && !state.AutoHidden)
        {
            return new PreviewPaneEffective(
                PaneMode.Overlay,
                LibraryColumnLayout.AllocateWithoutPreview(contentWidth));
        }

        if (!state.UserShow || state.AutoHidden)
        {
            return new PreviewPaneEffective(
                PaneMode.Hidden,
                LibraryColumnLayout.AllocateWithoutPreview(contentWidth));
        }

        return new PreviewPaneEffective(
            PaneMode.Docked,
            LibraryColumnLayout.AllocateWithPreview(contentWidth, width));
    }
}
