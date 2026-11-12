namespace Palace.Helpers;

/// <summary>
/// WinUI-free rules for Tags board drag chrome (Gooey dg-01…03).
/// Ghost bitmaps must be applied in <c>DragStarting</c> with no await/deferral —
/// post-await <c>SetContentFromBitmapImage</c> is dropped (WinUI #10144 / #4528).
/// </summary>
public static class TagDragUi
{
    /// <summary>dg-01a/b: source chip opacity while the ghost follows the pointer.</summary>
    public const double DragSourceOpacity = 0.4;

    /// <summary>
    /// DragUI content must be set synchronously in DragStarting (no GetDeferral await
    /// before SetContentFromBitmapImage).
    /// </summary>
    public static bool RequiresSynchronousDragUiContent => true;

    public static string GhostCacheKey(string? tagId, int payloadCount) =>
        $"{tagId ?? ""}\n{Math.Max(0, payloadCount)}";

    public static bool TryGetCachedGhostKey(string? cacheKey, string? tagId, int payloadCount) =>
        !string.IsNullOrEmpty(cacheKey)
        && string.Equals(cacheKey, GhostCacheKey(tagId, payloadCount), StringComparison.Ordinal);

    /// <summary>
    /// Child hit-testing inside a group row fires DragLeave without leaving the row.
    /// Clear drop chrome only when the pointer is outside the row bounds.
    /// </summary>
    public static bool ShouldClearDropChromeOnLeave(
        double x,
        double y,
        double width,
        double height)
    {
        if (!(width > 0) || !(height > 0)
            || !double.IsFinite(x) || !double.IsFinite(y)
            || !double.IsFinite(width) || !double.IsFinite(height))
        {
            return true;
        }

        return x < 0 || y < 0 || x > width || y > height;
    }

    public static bool ShowsValidDropChrome(bool isUserGroupRow, bool allowDrop) =>
        isUserGroupRow && allowDrop;

    public static bool ShowsRefuseDropChrome(bool isUserGroupRow, bool allowDrop) =>
        isUserGroupRow && !allowDrop;
}
