namespace Palace.Helpers;

/// <summary>
/// Pure Preview pane state for Library columns. Auto-hide from available width
/// (40 DIP hysteresis); 0.3.12 toggle / pin / overlay. Off WinUI for Palace.Tests.
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
/// <param name="OverlayOpen">Floating overlay while auto-hidden; cleared when auto-hide crosses.</param>
/// <param name="Pinned">Pin docks while width would auto-hide; cleared when auto-hide crosses.</param>
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

    /// <summary>Fixed overlay panel width while auto-hidden (Isiac).</summary>
    public const double OverlayWidthDip = 360;

    /// <summary>Edge chevron tab size (Isiac).</summary>
    public const double EdgeTabWidthDip = 14;

    /// <summary>Edge chevron tab height (Isiac).</summary>
    public const double EdgeTabHeightDip = 56;

    /// <summary>Show-in motion duration (ms).</summary>
    public const int MotionInMs = 250;

    /// <summary>Hide-out motion duration (ms).</summary>
    public const int MotionOutMs = 167;

    /// <summary>Default: user wants Preview under Automatic; no overlay/pin.</summary>
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
    /// When <paramref name="autoHideEnabled"/> is false (Settings Show), AutoHidden stays false.
    /// Crossing the auto-hide band clears <see cref="PreviewPaneState.Pinned"/> and
    /// <see cref="PreviewPaneState.OverlayOpen"/>. Never changes UserShow.
    /// </summary>
    public static PreviewPaneState OnWidth(
        PreviewPaneState state,
        double contentWidth,
        bool autoHideEnabled = true)
    {
        if (!double.IsFinite(contentWidth) || contentWidth < 0)
        {
            contentWidth = 0;
        }

        if (!autoHideEnabled)
        {
            var wasHidden = state.AutoHidden;
            return state with
            {
                AutoHidden = false,
                Pinned = wasHidden ? false : state.Pinned,
                OverlayOpen = wasHidden ? false : state.OverlayOpen
            };
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

    /// <summary>
    /// Toolbar / Alt+P / edge-tab intent: open overlay while auto-hidden,
    /// dismiss overlay, or flip UserShow when docked / roomy.
    /// </summary>
    public static PreviewPaneState ApplyToggle(PreviewPaneState state)
    {
        if (state.OverlayOpen)
        {
            return state with { OverlayOpen = false };
        }

        if (state.AutoHidden)
        {
            return state with { UserShow = true, OverlayOpen = true, Pinned = false };
        }

        return state with { UserShow = !state.UserShow, OverlayOpen = false };
    }

    /// <summary>User pin — docks while auto-hidden until width band crosses or restart.</summary>
    public static PreviewPaneState Pin(PreviewPaneState state, bool pinned = true) =>
        state with
        {
            Pinned = pinned,
            OverlayOpen = pinned ? false : state.OverlayOpen,
            UserShow = pinned ? true : state.UserShow
        };

    /// <summary>User overlay open — does not touch AutoHidden / UserShow.</summary>
    public static PreviewPaneState SetOverlayOpen(PreviewPaneState state, bool open) =>
        state with { OverlayOpen = open };

    /// <summary>Persist dock width (clamped); does not touch auto-hide flags.</summary>
    public static PreviewPaneState SetUserWidth(PreviewPaneState state, double width) =>
        state with { UserWidth = ClampUserWidth(width) };

    /// <summary>Reset dock width to the product default.</summary>
    public static PreviewPaneState ResetUserWidth(PreviewPaneState state) =>
        state with { UserWidth = LibraryColumnLayout.PreviewDefaultWidth };

    /// <summary>
    /// Toggle checked when the pane is visible (docked or overlay), not merely UserShow.
    /// </summary>
    public static bool IsChromeChecked(PreviewPaneState state)
    {
        if (!state.UserShow)
        {
            return false;
        }

        if (state.OverlayOpen)
        {
            return true;
        }

        if (state.AutoHidden && !state.Pinned)
        {
            return false;
        }

        return true;
    }

    /// <summary>Tooltip while auto-hidden and overlay closed.</summary>
    public const string AutoHiddenTooltip =
        "Hidden to fit this window width. Opening it shows it over the grid.";

    /// <summary>Default tooltip when the pane can dock or is open.</summary>
    public const string DefaultTooltip = "Preview pane (Alt+P)";

    public static string ChromeTooltip(PreviewPaneState state) =>
        state.AutoHidden && state.UserShow && !state.OverlayOpen && !state.Pinned
            ? AutoHiddenTooltip
            : DefaultTooltip;

    /// <summary>
    /// Resolve pane mode + column widths for available content width.
    /// Overlay (including while auto-hidden) allocates with preview=0 so the mosaic
    /// does not reflow. Pin docks even when AutoHidden.
    /// </summary>
    public static PreviewPaneEffective Effective(PreviewPaneState state, double contentWidth)
    {
        var width = ClampUserWidth(state.UserWidth);
        if (!state.UserShow)
        {
            return new PreviewPaneEffective(
                PaneMode.Hidden,
                LibraryColumnLayout.AllocateWithoutPreview(contentWidth));
        }

        if (state.OverlayOpen)
        {
            return new PreviewPaneEffective(
                PaneMode.Overlay,
                LibraryColumnLayout.AllocateWithoutPreview(contentWidth));
        }

        if (state.AutoHidden && !state.Pinned)
        {
            return new PreviewPaneEffective(
                PaneMode.Hidden,
                LibraryColumnLayout.AllocateWithoutPreview(contentWidth));
        }

        return new PreviewPaneEffective(
            PaneMode.Docked,
            LibraryColumnLayout.AllocateWithPreview(contentWidth, width));
    }

    /// <summary>
    /// Column + chrome flags the Library page must apply for one Effective result.
    /// Hide→show docked must restore a positive Preview width (not leave the column at 0).
    /// </summary>
    public static PreviewPaneLayoutState Layout(PreviewPaneEffective effective) =>
        new(
            effective.Mode,
            effective.Widths.Tree,
            effective.Widths.Mosaic,
            effective.Widths.Preview,
            DockHostVisible: effective.Mode == PaneMode.Docked,
            OverlayVisible: effective.Mode == PaneMode.Overlay);

    /// <summary>
    /// True when docked column space appears or disappears — Grid must remeasure;
    /// equal total width (mosaic ± preview) alone does not always rearrange children.
    /// </summary>
    public static bool NeedsDockColumnRelayout(PaneMode from, PaneMode to) =>
        (from == PaneMode.Docked) != (to == PaneMode.Docked);
}

/// <summary>Applied Library Preview column + chrome visibility for one layout pass.</summary>
public readonly record struct PreviewPaneLayoutState(
    PaneMode Mode,
    double Tree,
    double Mosaic,
    double Preview,
    bool DockHostVisible,
    bool OverlayVisible);
