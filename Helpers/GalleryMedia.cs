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

        if (PrefersCachedThumbStill(originalPath) && !string.IsNullOrEmpty(thumbPath))
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

    /// <summary>
    /// Folder headers take a full mosaic line (LinedFlowLayout has no break-before),
    /// so a group never starts mid-row after the previous group's leftover tiles.
    /// The caption itself stays compact in the tile template.
    /// </summary>
    public const double FolderHeaderAspect = 32.0;

    public static double MosaicAspect(bool isFolderHeader, double assetAspect) =>
        isFolderHeader ? FolderHeaderAspect : assetAspect;

    /// <summary>
    /// Asset commands stay on tiles. Headers cancel <c>ContextRequested</c>
    /// so the ItemContainer flyout never opens there.
    /// </summary>
    public static bool ShouldShowAssetContextFlyout(bool isFolderHeader) => !isFolderHeader;

    /// <summary>
    /// Windows default double-click window. A header tap already navigates;
    /// a second click inside this window can land on a new tile after the
    /// async mosaic reload and must not open overlay.
    /// </summary>
    public const int MosaicDoubleClickMs = 500;

    public static bool ShouldOpenOverlayFromDoubleTap(
        bool isFolderHeader,
        long millisecondsSinceHeaderGesture)
    {
        if (isFolderHeader)
        {
            return false;
        }

        return millisecondsSinceHeaderGesture < 0
            || millisecondsSinceHeaderGesture >= MosaicDoubleClickMs;
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
    /// Mosaic may generate a JPEG only for a local original. Online-only
    /// placeholders stay on <see cref="ShowPlaceholderTile"/> — AVIF
    /// <c>GetThumbnailAsync</c> / WIC still opens the Dropbox file.
    /// </summary>
    public static bool ShouldRequestMosaicThumb(
        bool isOrphan,
        string? path,
        string? contentHash,
        string? thumbPath,
        bool isOnlineOnly) =>
        !isOrphan
        && !isOnlineOnly
        && !string.IsNullOrEmpty(path)
        && !string.IsNullOrEmpty(contentHash)
        && string.IsNullOrEmpty(thumbPath);

    /// <summary>
    /// PSD has no WIC overlay decode; show the shell mosaic JPEG.
    /// </summary>
    public static bool PrefersCachedThumbStill(string? path) =>
        PathSafe.IsPsd(path);

    /// <summary>
    /// Video / AVIF / HEIC / PSD must not <c>Open</c> + <c>BitmapDecoder</c>
    /// the original. Shell / provider <c>GetThumbnailAsync</c> only; if that
    /// fails, no JPEG. Never call this for online-only placeholders.
    /// </summary>
    public static bool UsesShellThumbnail(AssetKind kind, string? path) =>
        kind == AssetKind.Video || PathSafe.UsesShellStillThumb(path);

    /// <summary>WIC decode of the original — never online-only, never AVIF.</summary>
    public static bool MayOpenOriginalForThumb(bool isOnlineOnly, AssetKind kind, string? path) =>
        !isOnlineOnly && !UsesShellThumbnail(kind, path) && kind != AssetKind.Other;

    /// <summary>
    /// Never <c>ImageDimensions.TryRead</c> / <c>AvifFile</c> the original to
    /// decide regenerate. A readable cache JPEG is kept; missing / unreadable
    /// cache may rebuild without comparing source pixels.
    /// </summary>
    public static bool ShouldRegenerateCachedThumb((int Width, int Height)? cachedJpeg, int maxSide)
    {
        _ = maxSide;
        return cachedJpeg is null;
    }

    /// <summary>
    /// Cloud / On-Demand tile with no decoded preview — show a kind icon, not a blank.
    /// </summary>
    public static bool ShowPlaceholderTile(bool isOnlineOnly, bool isOrphan, bool hasDecodedImage) =>
        isOnlineOnly && !isOrphan && !hasDecodedImage;

    /// <summary>
    /// Corner cloud glyph on Library / Tags tiles. Online-only placeholders,
    /// API sources, and hydrated On-Demand copies all show it — including
    /// when a local-looking JPEG is already on the tile.
    /// </summary>
    public static bool ShowCloudBadge(
        bool isFolderHeader,
        bool isOrphan,
        bool isOnlineOnly,
        bool hasCloudItemId,
        bool isCloudBacked) =>
        !isFolderHeader
        && !isOrphan
        && (isOnlineOnly || hasCloudItemId || isCloudBacked);

    /// <summary>
    /// API sources (<see cref="SourceKind.OneDrive"/> / Dropbox) and local
    /// Files On-Demand roots. Never opens a stream.
    /// </summary>
    public static bool SourceFolderIsCloud(SourceKind kind, string? sourcePath) =>
        kind is SourceKind.OneDrive or SourceKind.Dropbox
        || CloudFile.IsCloudBackedFolder(sourcePath);

    /// <summary>
    /// Stamp <c>AssetItem.IsCloudBacked</c> from the source folder plus
    /// <see cref="CloudFile.IsCloudBacked"/> — not only <c>IsOnlineOnly</c>,
    /// so hydrated JPEGs still get the corner glyph.
    /// </summary>
    public static bool StampIsCloudBacked(
        bool isOnlineOnly,
        bool hasCloudItemId,
        bool sourceFolderIsCloud,
        bool pathIsCloudBacked) =>
        isOnlineOnly || hasCloudItemId || sourceFolderIsCloud || pathIsCloudBacked;

    public static string CloudBadgeAutomationId(string? assetId) =>
        MosaicBadgeAutomationId("IcnCloudBadge_", assetId);

    public static bool ShowVideoBadge(bool isFolderHeader, bool isOrphan, AssetKind kind) =>
        ShowKindBadge(isFolderHeader, isOrphan, kind == AssetKind.Video);

    public static bool ShowGifBadge(bool isFolderHeader, bool isOrphan, AssetKind kind) =>
        ShowKindBadge(isFolderHeader, isOrphan, kind == AssetKind.Gif);

    /// <summary>
    /// HDR glyph from catalog / header already on the row. Never Open or
    /// recall the original — <paramref name="catalogHdr"/> is scan-time
    /// header probe; Radiance <c>.hdr</c> is the path on the catalog row.
    /// </summary>
    public static bool ShowHdrBadge(
        bool isFolderHeader,
        bool isOrphan,
        string? path,
        bool catalogHdr) =>
        ShowKindBadge(isFolderHeader, isOrphan, catalogHdr || PathSafe.IsRadiance(path));

    /// <summary>
    /// Scan / hydrate only. Mosaic mapping must pass the catalog flag —
    /// never call this while building tiles. When the original cannot be
    /// read, keep <paramref name="existingHdr"/> so a later online-only /
    /// API rescan does not wipe a probe from Open.
    /// </summary>
    public static bool CatalogHdrFromHeader(
        string? path,
        bool mayReadOriginalHeader,
        bool existingHdr = false)
    {
        if (PathSafe.IsRadiance(path))
        {
            return true;
        }

        if (mayReadOriginalHeader)
        {
            return HdrFile.ProbePath(path).IsHdr;
        }

        return existingHdr;
    }

    public static bool ShowKindBadge(bool isFolderHeader, bool isOrphan, bool matches) =>
        !isFolderHeader && !isOrphan && matches;

    public static string VideoBadgeAutomationId(string? assetId) =>
        MosaicBadgeAutomationId("IcnVideoBadge_", assetId);

    public static string GifBadgeAutomationId(string? assetId) =>
        MosaicBadgeAutomationId("IcnGifBadge_", assetId);

    public static string HdrBadgeAutomationId(string? assetId) =>
        MosaicBadgeAutomationId("IcnHdrBadge_", assetId);

    private static string MosaicBadgeAutomationId(string prefix, string? assetId) =>
        prefix + string.Concat((assetId ?? "").Where(char.IsLetterOrDigit));

    /// <summary>
    /// True when <paramref name="path"/> can be shown without opening an
    /// On-Demand original (remote URL or a local, already-hydrated file).
    /// A catalog / API display path is not enough.
    /// </summary>
    public static bool CanShowPreview(string? path) =>
        IsRemoteUri(path)
        || (!string.IsNullOrEmpty(path) && CloudFile.Exists(path) && !CloudFile.IsOnlineOnly(path));

    /// <summary>
    /// Catalog <c>IsOnlineOnly</c> can stay true after the file is already local.
    /// A hydrated original that <see cref="CanShowPreview"/> accepts wins.
    /// </summary>
    public static bool IsLiveOnlineOnly(
        bool catalogIsOnlineOnly,
        bool hydrateOnOpen,
        bool apiOnly,
        bool canShowPreview) =>
        !canShowPreview && (catalogIsOnlineOnly || hydrateOnOpen || apiOnly);

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
            || string.IsNullOrEmpty(contentHash)
            || PathSafe.UsesShellStillThumb(path))
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

            var mosaic = ReadString(ctx, "MosaicTag");
            if (!string.IsNullOrEmpty(mosaic))
            {
                return mosaic;
            }
        }

        if (tag is string tagId && tagId.Length > 0)
        {
            return tagId;
        }

        return ReadId(tag) ?? ReadString(tag, "MosaicTag");
    }

    public static bool MatchesMosaicKey(string? id, string? mosaicTag, string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        return string.Equals(id, key, StringComparison.Ordinal)
            || (!string.IsNullOrEmpty(mosaicTag)
                && string.Equals(mosaicTag, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadId(object? value) => ReadString(value, "Id");

    private static string? ReadString(object? value, string property)
    {
        if (value is null)
        {
            return null;
        }

        var prop = value.GetType().GetProperty(property);
        return prop?.GetValue(value) as string;
    }
}
