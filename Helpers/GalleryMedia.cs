using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Overlay / mosaic media decisions that must stay off WinUI so Palace.Tests can cover them.
/// </summary>
public static class GalleryMedia
{
    public static bool CanOpen(int mosaicCount, bool hasStartItem) =>
        hasStartItem && mosaicCount > 0;

    public static int StartIndex(IReadOnlyList<string> ids, string? startId)
    {
        if (ids.Count == 0)
        {
            return -1;
        }

        if (string.IsNullOrEmpty(startId))
        {
            return 0;
        }

        for (var i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], startId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    public static string? OverlayStillPath(
        bool isOrphan,
        bool isOnlineOnly,
        bool localExists,
        string? originalPath,
        string? thumbPath,
        string? cloudPreviewPath)
    {
        if (isOrphan)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(cloudPreviewPath))
        {
            return cloudPreviewPath;
        }

        if (isOnlineOnly || !localExists)
        {
            return thumbPath;
        }

        return originalPath;
    }

    public static bool IsPlayableLocalVideo(
        AssetKind kind,
        bool apiOnly,
        bool isOnlineOnly,
        bool localExists,
        string? path) =>
        kind == AssetKind.Video
        && !apiOnly
        && !isOnlineOnly
        && localExists
        && !string.IsNullOrEmpty(path);

    public static bool ShouldLoadTileThumb(
        bool isOrphan,
        bool alreadyStarted,
        bool hasImage,
        string? thumbPath,
        string? contentHash)
    {
        if (isOrphan || alreadyStarted || hasImage)
        {
            return false;
        }

        return !string.IsNullOrEmpty(thumbPath) || !string.IsNullOrEmpty(contentHash);
    }

    public static bool ShouldUpgradeThumb(
        bool mayUpgrade,
        bool isOnlineOnly,
        bool isOrphan,
        string? path,
        string? contentHash) =>
        mayUpgrade
        && !isOnlineOnly
        && !isOrphan
        && !string.IsNullOrEmpty(path)
        && !string.IsNullOrEmpty(contentHash);

    public static bool IsRemoteUri(string? path) =>
        !string.IsNullOrEmpty(path)
        && (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    public static string? FindAssetId(object? tag, object? dataContext)
    {
        if (dataContext is { } ctx)
        {
            var id = ReadId(ctx);
            if (!string.IsNullOrEmpty(id))
            {
                return id;
            }
        }

        if (tag is string tagId && tagId.Length > 0)
        {
            return tagId;
        }

        return ReadId(tag);
    }

    private static string? ReadId(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var prop = value.GetType().GetProperty("Id");
        return prop?.GetValue(value) as string;
    }
}
