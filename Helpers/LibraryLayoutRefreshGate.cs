namespace Palace.Helpers;

/// <summary>
/// Re-entrancy + no-op gate for Library column refresh. Same content/window/
/// auto-hide/pane/layout inputs → skip Apply (breaks SizeChanged loops when the
/// page ActualWidth fed OnWidth). Off WinUI for Palace.Tests.
/// </summary>
public sealed class LibraryLayoutRefreshGate
{
    public const double WidthEpsilonDip = 0.5;

    private bool _busy;
    private bool _hasSnapshot;
    private double _contentWidth = double.NaN;
    private double _windowDip = double.NaN;
    private bool _autoHideEnabled;
    private PreviewPaneState _pane;
    private PreviewPaneLayoutState _layout;

    /// <summary>True when a refresh is already on the stack (re-entrant call).</summary>
    public bool IsBusy => _busy;

    /// <summary>How many Apply commits this gate has accepted (tests / probe).</summary>
    public int ApplyCount { get; private set; }

    /// <summary>
    /// Enter the refresh critical section. False if already busy — caller must
    /// return without applying (re-entrancy).
    /// </summary>
    public bool TryEnter()
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        return true;
    }

    public void Exit() => _busy = false;

    /// <summary>
    /// True when columns/chrome must be applied. False when inputs match the last
    /// committed snapshot (idle no-op).
    /// </summary>
    public bool ShouldApply(
        double contentWidth,
        double windowDip,
        bool autoHideEnabled,
        PreviewPaneState pane,
        PreviewPaneLayoutState layout)
    {
        if (!_hasSnapshot)
        {
            return true;
        }

        return !Near(contentWidth, _contentWidth)
            || !Near(windowDip, _windowDip)
            || autoHideEnabled != _autoHideEnabled
            || !pane.Equals(_pane)
            || !LayoutNear(layout, _layout);
    }

    public void Commit(
        double contentWidth,
        double windowDip,
        bool autoHideEnabled,
        PreviewPaneState pane,
        PreviewPaneLayoutState layout)
    {
        _contentWidth = contentWidth;
        _windowDip = windowDip;
        _autoHideEnabled = autoHideEnabled;
        _pane = pane;
        _layout = layout;
        _hasSnapshot = true;
        ApplyCount++;
    }

    /// <summary>
    /// Drive apply/no-op decisions for a stable input stream. Returns how many
    /// times <see cref="ShouldApply"/> was true (must be ≤2 for a settled width).
    /// </summary>
    public static int CountAppliesUntilStable(
        double contentWidth,
        double windowDip,
        bool autoHideEnabled,
        PreviewPaneState seed,
        int maxPasses = 8)
    {
        var gate = new LibraryLayoutRefreshGate();
        var pane = seed;
        var applies = 0;
        for (var i = 0; i < maxPasses; i++)
        {
            if (!gate.TryEnter())
            {
                break;
            }

            try
            {
                pane = PreviewPane.OnWidth(pane, contentWidth, autoHideEnabled);
                var layout = PreviewPane.Layout(PreviewPane.Effective(pane, contentWidth));
                if (!gate.ShouldApply(contentWidth, windowDip, autoHideEnabled, pane, layout))
                {
                    continue;
                }

                gate.Commit(contentWidth, windowDip, autoHideEnabled, pane, layout);
                applies++;
            }
            finally
            {
                gate.Exit();
            }
        }

        return applies;
    }

    private static bool LayoutNear(PreviewPaneLayoutState a, PreviewPaneLayoutState b) =>
        a.Mode == b.Mode
        && Near(a.Tree, b.Tree)
        && Near(a.Mosaic, b.Mosaic)
        && Near(a.Preview, b.Preview)
        && a.DockHostVisible == b.DockHostVisible
        && a.OverlayVisible == b.OverlayVisible;

    private static bool Near(double a, double b) =>
        double.IsFinite(a)
        && double.IsFinite(b)
        && Math.Abs(a - b) <= WidthEpsilonDip;
}
