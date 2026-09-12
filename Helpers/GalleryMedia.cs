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

    public static int MosaicAspectCount(int requestedLength, int available)
    {
        if (requestedLength <= 0 || available <= 0)
        {
            return 0;
        }

        return Math.Min(requestedLength, available);
    }

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

    /// <summary>
    /// Mosaic may ask for a provider / shell JPEG even when the original is
    /// online-only. <c>ThumbnailService</c> must use GetThumbnailAsync, not Open.
    /// </summary>
    public static bool ShouldRequestMosaicThumb(
        bool isOrphan,
        string? path,
        string? contentHash,
        string? thumbPath) =>
        !isOrphan
        && !string.IsNullOrEmpty(path)
        && !string.IsNullOrEmpty(contentHash)
        && string.IsNullOrEmpty(thumbPath);

    /// <summary>
    /// Cloud / On-Demand tile with no decoded preview — show a kind icon, not a blank.
    /// </summary>
    public static bool ShowPlaceholderTile(bool isOnlineOnly, bool isOrphan, bool hasDecodedImage) =>
        isOnlineOnly && !isOrphan && !hasDecodedImage;

    /// <summary>Provider stream is a real picture; a generic file icon is not.</summary>
    public static bool AcceptsProviderThumbnail(bool isImageType) => isImageType;

    public static string PlaceholderIcon(AssetKind kind) => kind switch
    {
        AssetKind.Video => "Video",
        AssetKind.Gif => "Gif",
        AssetKind.Image => "Image",
        _ => "Document"
    };

    public static bool ShouldUpgradeThumb(
        bool mayUpgrade,
        bool isOnlineOnly,
        bool isOrphan,
        string? path,
        string? contentHash,
        bool hasDecodedImage = false,
        string? thumbPath = null)
    {
        if (!mayUpgrade
            || isOnlineOnly
            || isOrphan
            || string.IsNullOrEmpty(path)
            || string.IsNullOrEmpty(contentHash))
        {
            return false;
        }

        // Already showing the cached JPEG — replacing BitmapImage flickers the tile.
        return !hasDecodedImage || string.IsNullOrEmpty(thumbPath);
    }

    public static bool ShouldReplaceTileBitmap(
        string? currentPath,
        string? newPath,
        bool hasImage) =>
        !hasImage
        || string.IsNullOrEmpty(currentPath)
        || !string.Equals(currentPath, newPath, StringComparison.OrdinalIgnoreCase);

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
