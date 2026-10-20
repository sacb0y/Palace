namespace Palace.Helpers;

/// <summary>
/// Viewer media chrome rules that stay off WinUI for tests. Overlay /
/// GalleryWindow must never change the host window's maximized / full-window
/// state when opening video — MediaPlayerElement full-window and AppWindow
/// maximize are both out of bounds.
/// </summary>
public static class ViewerMediaChrome
{
    /// <summary>
    /// True when a media full-window request should be rejected (always).
    /// </summary>
    public static bool AllowsMediaFullWindow => false;

    /// <summary>
    /// After video open, restore the prior presenter state when media chrome
    /// flipped the window to maximized.
    /// </summary>
    public static bool ShouldRestorePresenter(
        bool wasMaximizedBeforeVideo,
        bool isMaximizedAfterVideo) =>
        isMaximizedAfterVideo && !wasMaximizedBeforeVideo;

    /// <summary>
    /// Clamp a desired gallery window size so a high-DPI Resize never fills
    /// the work area (looks like maximize).
    /// </summary>
    public static (int Width, int Height) ClampGalleryWindowSize(
        int desiredWidth,
        int desiredHeight,
        int workWidth,
        int workHeight,
        double fillFraction = 0.90)
    {
        var frac = fillFraction is > 0 and <= 1 ? fillFraction : 0.90;
        var maxW = workWidth > 0 ? Math.Max(320, (int)(workWidth * frac)) : desiredWidth;
        var maxH = workHeight > 0 ? Math.Max(240, (int)(workHeight * frac)) : desiredHeight;
        return (
            Math.Clamp(desiredWidth, 320, maxW),
            Math.Clamp(desiredHeight, 240, maxH));
    }
}
