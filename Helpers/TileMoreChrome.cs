namespace Palace.Helpers;

/// <summary>
/// Library mosaic per-tile ⋮ (<c>BtnAssetMore</c>) reveal policy. Idle tiles hide
/// the button (opacity 0, no hit-test, out of tab order). Shown on pointer-over,
/// keyboard focus, selection, or while that tile's context flyout is open.
/// Off WinUI — page code drives VisualStates from these names.
/// </summary>
public static class TileMoreChrome
{
    public const string HiddenState = "MoreHidden";
    public const string ShownState = "MoreShown";

    /// <summary>
    /// True when the overflow control should be opaque and interactive.
    /// </summary>
    public static bool ShouldShow(
        bool pointerOver,
        bool focused,
        bool selected,
        bool flyoutOpen) =>
        pointerOver || focused || selected || flyoutOpen;

    /// <summary>VisualState name for <see cref="ShouldShow"/>.</summary>
    public static string StateName(bool show) =>
        show ? ShownState : HiddenState;

    /// <summary>Opacity for the overflow control (no layout jump).</summary>
    public static double Opacity(bool show) => show ? 1 : 0;

    /// <summary>Tab stop only while the control is shown.</summary>
    public static bool IsTabStop(bool show) => show;
}
