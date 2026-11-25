namespace Palace.Helpers;

/// <summary>
/// Contract for heavy preview / composer pages (D10). Prefer a dedicated page with
/// <c>NavigationCacheMode=Disabled</c>, build in <c>OnNavigatedTo</c>, and
/// <c>await DisposeAsync</c> in <c>OnNavigatedFrom</c> — not Loaded/Unloaded alone.
/// Alternatively host in a separate <c>Window</c> like GalleryWindow.
/// </summary>
public static class NavigationTeardown
{
    public const string PreviewCacheMode = "Disabled";

    public static bool RequiresNavigatedFromDispose(bool holdsGpuDevice) => holdsGpuDevice;
}
