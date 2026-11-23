namespace Palace.Helpers;

/// <summary>
/// Library mosaic per-tile ⋮ (<c>BtnAssetMore</c>) reveal policy. Idle tiles hide
/// the button (opacity 0, no hit-test, out of tab order). Shown on pointer-over,
/// keyboard focus, single-tile selection outside select mode, or while that tile's
/// context flyout is open. Select mode and multi-select keep ⋮ on hover/focus/flyout
/// only so leaving select mode does not leave a sticky ⋮ on every selected tile.
/// Off WinUI — page code applies <see cref="Opacity"/> / hit-test / tab stop.
/// </summary>
public static class TileMoreChrome
{
    /// <summary>
    /// True when the overflow control should be opaque and interactive.
    /// <paramref name="selected"/> counts only outside select mode with exactly one
    /// selected asset (browse single-select).
    /// </summary>
    public static bool ShouldShow(
        bool pointerOver,
        bool focused,
        bool selected,
        bool flyoutOpen,
        bool selectMode = false,
        int selectionCount = 0) =>
        pointerOver
        || focused
        || flyoutOpen
        || CountsSelected(selected, selectMode, selectionCount);

    /// <summary>
    /// Selection alone reveals ⋮ only in browse (not select mode) with one tile selected.
    /// </summary>
    public static bool CountsSelected(bool selected, bool selectMode, int selectionCount) =>
        selected && !selectMode && selectionCount == 1;

    /// <summary>Opacity for the overflow control (no layout jump).</summary>
    public static double Opacity(bool show) => show ? 1 : 0;

    /// <summary>Tab stop only while the control is shown.</summary>
    public static bool IsTabStop(bool show) => show;
}
