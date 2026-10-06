using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class GalleryMediaTests
{
    [Fact]
    public void CanOpen_RequiresStartAndMosaic()
    {
        Assert.False(GalleryMedia.CanOpen(0, true));
        Assert.False(GalleryMedia.CanOpen(3, false));
        Assert.True(GalleryMedia.CanOpen(3, true));
    }

    [Fact]
    public void StartIndex_UsesMatchingIdOrZero()
    {
        var ids = new[] { "a", "b", "c" };
        Assert.Equal(1, GalleryMedia.StartIndex(ids, "b"));
        Assert.Equal(0, GalleryMedia.StartIndex(ids, "missing"));
        Assert.Equal(0, GalleryMedia.StartIndex(ids, null));
        Assert.Equal(-1, GalleryMedia.StartIndex([], "a"));
    }

    [Fact]
    public void OverlayStillPath_PrefersThumbWhenOnlineOnly()
    {
        Assert.Equal(
            @"C:\thumbs\x.jpg",
            GalleryMedia.OverlayStillPath(false, true, true, @"D:\photos\a.jpg", @"C:\thumbs\x.jpg", null));
    }

    [Fact]
    public void OverlayStillPath_UsesOriginalWhenLocal()
    {
        Assert.Equal(
            @"D:\photos\a.jpg",
            GalleryMedia.OverlayStillPath(false, false, true, @"D:\photos\a.jpg", @"C:\thumbs\x.jpg", null));
    }

    [Fact]
    public void OverlayStillPath_PrefersCloudPreview()
    {
        Assert.Equal(
            @"C:\thumbs\x_lg.jpg",
            GalleryMedia.OverlayStillPath(false, true, false, "cloud/a.jpg", @"C:\thumbs\x.jpg", @"C:\thumbs\x_lg.jpg"));
    }

    [Fact]
    public void OverlayStillPath_OrphanIsNull()
    {
        Assert.Null(GalleryMedia.OverlayStillPath(true, false, true, @"D:\a.jpg", @"C:\t.jpg", null));
    }

    [Fact]
    public void IsPlayableLocalVideo_RequiresHydratedLocalFile()
    {
        Assert.True(GalleryMedia.IsPlayableLocalVideo(AssetKind.Video, false, false, true, @"D:\clip.mp4"));
        Assert.False(GalleryMedia.IsPlayableLocalVideo(AssetKind.Video, false, true, true, @"D:\clip.mp4"));
        Assert.False(GalleryMedia.IsPlayableLocalVideo(AssetKind.Image, false, false, true, @"D:\a.jpg"));
        Assert.False(GalleryMedia.IsPlayableLocalVideo(AssetKind.Video, true, false, false, "cloud/clip.mp4"));
    }

    [Fact]
    public void MosaicAspectCount_CapsRequestedRangeToAvailable()
    {
        Assert.Equal(8, GalleryMedia.MosaicAspectCount(8, 2400));
        Assert.Equal(3, GalleryMedia.MosaicAspectCount(80, 3));
        Assert.Equal(0, GalleryMedia.MosaicAspectCount(8, 0));
        Assert.Equal(0, GalleryMedia.MosaicAspectCount(0, 12));
    }

    [Fact]
    public void MosaicAspect_HeadersAreNotAssetTiles()
    {
        Assert.Equal(28.0, GalleryMedia.FolderHeaderHeight);
        Assert.True(MosaicRows.IsCompactHeader(GalleryMedia.FolderHeaderHeight, 96));
        Assert.False(GalleryMedia.UsesAssetTileLayout(true));
        Assert.True(GalleryMedia.UsesAssetTileLayout(false));
        Assert.Equal(0, GalleryMedia.MosaicAspect(true, 1.5));
        Assert.Equal(1.5, GalleryMedia.MosaicAspect(false, 1.5));
    }

    [Fact]
    public void ShouldShowAssetContextFlyout_TilesOnly()
    {
        Assert.True(GalleryMedia.ShouldShowAssetContextFlyout(false));
        Assert.False(GalleryMedia.ShouldShowAssetContextFlyout(true));
    }

    [Fact]
    public void ShouldOpenOverlayFromDoubleTap_IgnoresHeadersAndHeaderGesture()
    {
        Assert.False(GalleryMedia.ShouldOpenOverlayFromDoubleTap(true, -1));
        Assert.False(GalleryMedia.ShouldOpenOverlayFromDoubleTap(true, 0));
        Assert.False(GalleryMedia.ShouldOpenOverlayFromDoubleTap(false, 0));
        Assert.False(GalleryMedia.ShouldOpenOverlayFromDoubleTap(false, GalleryMedia.MosaicDoubleClickMs - 1));
        Assert.True(GalleryMedia.ShouldOpenOverlayFromDoubleTap(false, -1));
        Assert.True(GalleryMedia.ShouldOpenOverlayFromDoubleTap(false, GalleryMedia.MosaicDoubleClickMs));
        Assert.True(GalleryMedia.ShouldOpenOverlayFromSpace(false, false, false, 32));
        Assert.False(GalleryMedia.ShouldOpenOverlayFromSpace(false, false, true, 32));
        Assert.False(GalleryMedia.ShouldOpenOverlayFromSpace(true, false, false, 32));
        Assert.False(GalleryMedia.ShouldOpenOverlayFromSpace(false, true, false, 32));
    }

    [Fact]
    public void ShouldLoadTileThumb_AllowsMissingJpegWhenHashExists()
    {
        Assert.True(GalleryMedia.ShouldLoadTileThumb(false, false, false, null, "abc"));
        Assert.False(GalleryMedia.ShouldLoadTileThumb(false, false, false, null, null));
        Assert.False(GalleryMedia.ShouldLoadTileThumb(true, false, false, @"C:\t.jpg", "abc"));
        Assert.False(GalleryMedia.ShouldLoadTileThumb(false, true, false, @"C:\t.jpg", "abc"));
        Assert.False(GalleryMedia.ShouldLoadTileThumb(false, false, true, @"C:\t.jpg", "abc"));
    }

    [Fact]
    public void ShouldRequestMosaicThumb_SkipsOnlineOnlyPlaceholders()
    {
        Assert.True(GalleryMedia.ShouldRequestMosaicThumb(false, @"D:\cloud\a.jpg", "abc", null, false));
        Assert.False(GalleryMedia.ShouldRequestMosaicThumb(false, @"D:\cloud\a.avif", "abc", null, true));
        Assert.False(GalleryMedia.ShouldRequestMosaicThumb(true, @"D:\cloud\a.jpg", "abc", null, false));
        Assert.False(GalleryMedia.ShouldRequestMosaicThumb(false, @"D:\cloud\a.jpg", "abc", @"C:\t.jpg", false));
        Assert.False(GalleryMedia.ShouldRequestMosaicThumb(false, null, "abc", null, false));
    }

    [Fact]
    public void AvifThumbs_UseShellAndNeverOpenOriginal()
    {
        Assert.True(PathSafe.IsAvif(@"D:\cloud\shot.avif"));
        Assert.True(PathSafe.IsAvif(".avif"));
        Assert.False(PathSafe.IsAvif(".jpg"));
        Assert.True(GalleryMedia.UsesShellThumbnail(AssetKind.Image, @"D:\a.avif"));
        Assert.True(GalleryMedia.UsesShellThumbnail(AssetKind.Video, @"D:\a.mp4"));
        Assert.False(GalleryMedia.UsesShellThumbnail(AssetKind.Image, @"D:\a.jpg"));
        Assert.False(GalleryMedia.MayOpenOriginalForThumb(true, AssetKind.Image, @"D:\a.jpg"));
        Assert.False(GalleryMedia.MayOpenOriginalForThumb(false, AssetKind.Image, @"D:\a.avif"));
        Assert.True(GalleryMedia.MayOpenOriginalForThumb(false, AssetKind.Image, @"D:\a.jpg"));
        Assert.True(GalleryMedia.ShouldRegenerateCachedThumb(null, 512));
        Assert.False(GalleryMedia.ShouldRegenerateCachedThumb((256, 256), 512));
        Assert.False(GalleryMedia.ShouldUpgradeThumb(true, false, false, @"D:\a.avif", "abc"));
    }

    [Fact]
    public void ShowPlaceholderTile_WhenOnlineOnlyAndNoDecodedPreview()
    {
        Assert.True(GalleryMedia.ShowPlaceholderTile(true, false, false));
        Assert.False(GalleryMedia.ShowPlaceholderTile(true, false, true));
        Assert.False(GalleryMedia.ShowPlaceholderTile(true, true, false));
        Assert.False(GalleryMedia.ShowPlaceholderTile(false, false, false));
    }

    [Fact]
    public void ShowCloudBadge_OnDemandApiAndHydratedThumbs()
    {
        Assert.True(GalleryMedia.ShowCloudBadge(false, false, true, false, false));
        Assert.True(GalleryMedia.ShowCloudBadge(false, false, false, true, false));
        Assert.True(GalleryMedia.ShowCloudBadge(false, false, false, false, true));
        Assert.False(GalleryMedia.ShowCloudBadge(true, false, true, true, true));
        Assert.False(GalleryMedia.ShowCloudBadge(false, true, true, true, true));
        Assert.False(GalleryMedia.ShowCloudBadge(false, false, false, false, false));
        Assert.Equal("IcnCloudBadge_abc123", GalleryMedia.CloudBadgeAutomationId("abc-123"));
        Assert.Equal("IcnCloudBadge_", GalleryMedia.CloudBadgeAutomationId(null));
    }

    [Fact]
    public void StampIsCloudBacked_UsesSourceFolderNotOnlyOnlineOnly()
    {
        Assert.True(GalleryMedia.StampIsCloudBacked(false, false, true, false));
        Assert.True(GalleryMedia.StampIsCloudBacked(false, false, false, true));
        Assert.True(GalleryMedia.StampIsCloudBacked(true, false, false, false));
        Assert.True(GalleryMedia.StampIsCloudBacked(false, true, false, false));
        Assert.False(GalleryMedia.StampIsCloudBacked(false, false, false, false));
        Assert.True(GalleryMedia.SourceFolderIsCloud(SourceKind.OneDrive, @"C:\Photos"));
        Assert.True(GalleryMedia.SourceFolderIsCloud(SourceKind.Dropbox, null));
        Assert.False(GalleryMedia.SourceFolderIsCloud(SourceKind.Local, null));
        Assert.True(GalleryMedia.ShowCloudBadge(
            false,
            false,
            false,
            false,
            GalleryMedia.StampIsCloudBacked(false, false, true, false)));
    }

    [Fact]
    public void KindBadges_SkipHeadersAndOrphans()
    {
        Assert.True(GalleryMedia.ShowVideoBadge(false, false, AssetKind.Video));
        Assert.False(GalleryMedia.ShowVideoBadge(false, false, AssetKind.Image));
        Assert.False(GalleryMedia.ShowVideoBadge(true, false, AssetKind.Video));
        Assert.False(GalleryMedia.ShowVideoBadge(false, true, AssetKind.Video));
        Assert.True(GalleryMedia.ShowGifBadge(false, false, AssetKind.Gif));
        Assert.False(GalleryMedia.ShowGifBadge(false, false, AssetKind.Video));
        Assert.False(GalleryMedia.ShowGifBadge(true, false, AssetKind.Gif));
        Assert.True(GalleryMedia.ShowHdrBadge(false, false, @"D:\shots\sky.hdr", false));
        Assert.True(GalleryMedia.ShowHdrBadge(false, false, @"D:\shots\a.jpg", true));
        Assert.True(GalleryMedia.ShowHdrBadge(false, false, null, true));
        Assert.False(GalleryMedia.ShowHdrBadge(false, false, @"D:\shots\a.jpg", false));
        Assert.False(GalleryMedia.ShowHdrBadge(false, false, null, false));
        Assert.False(GalleryMedia.ShowHdrBadge(true, false, @"D:\shots\sky.hdr", true));
        Assert.False(GalleryMedia.ShowHdrBadge(false, true, @"D:\shots\sky.hdr", true));
        Assert.Equal(24, GalleryMedia.HdrBadgeIconDip);
        Assert.Equal("HDR", GalleryMedia.HdrBadgeLabel);
        Assert.Equal("IcnGalleryOverlayHdr", GalleryMedia.OverlayHdrBadgeAutomationId);
        Assert.Equal("IcnGalleryWindowHdr", GalleryMedia.WindowHdrBadgeAutomationId);
        Assert.False(GalleryMedia.CatalogHdrFromHeader(@"D:\shots\a.jpg", mayReadOriginalHeader: false));
        Assert.True(GalleryMedia.CatalogHdrFromHeader(@"D:\shots\sky.hdr", mayReadOriginalHeader: false));
        Assert.True(GalleryMedia.CatalogHdrFromHeader(
            @"D:\cloud\hdr.avif",
            mayReadOriginalHeader: false,
            existingHdr: true));
        Assert.False(GalleryMedia.CatalogHdrFromHeader(
            @"D:\cloud\hdr.avif",
            mayReadOriginalHeader: false,
            existingHdr: false));
        Assert.True(GalleryMedia.CatalogHdrFromHeader(
            @"D:\shots\sky.hdr",
            mayReadOriginalHeader: false,
            existingHdr: false));
        Assert.Equal("IcnVideoBadge_abc123", GalleryMedia.VideoBadgeAutomationId("abc-123"));
        Assert.Equal("IcnGifBadge_clip", GalleryMedia.GifBadgeAutomationId("clip"));
        Assert.Equal("IcnHdrBadge_", GalleryMedia.HdrBadgeAutomationId(null));
    }

    [Fact]
    public void IsLiveOnlineOnly_StaleCatalogFlagYieldsToLocalPreview()
    {
        Assert.False(GalleryMedia.IsLiveOnlineOnly(true, false, false, canShowPreview: true));
        Assert.False(GalleryMedia.IsLiveOnlineOnly(true, true, true, canShowPreview: true));
        Assert.True(GalleryMedia.IsLiveOnlineOnly(true, false, false, canShowPreview: false));
        Assert.True(GalleryMedia.IsLiveOnlineOnly(false, true, false, canShowPreview: false));
        Assert.True(GalleryMedia.IsLiveOnlineOnly(false, false, true, canShowPreview: false));
        Assert.False(GalleryMedia.IsLiveOnlineOnly(false, false, false, canShowPreview: false));
    }

    [Fact]
    public void OverlayStillPath_UsesOriginalWhenStaleOnlineOnlyButLocalExists()
    {
        var staleOnline = GalleryMedia.IsLiveOnlineOnly(true, false, false, canShowPreview: true);
        Assert.Equal(
            @"D:\photos\a.jpg",
            GalleryMedia.OverlayStillPath(false, staleOnline, true, @"D:\photos\a.jpg", @"C:\thumbs\x.jpg", null));
        Assert.False(GalleryMedia.ShowPlaceholderTile(staleOnline, false, false));
        Assert.True(GalleryMedia.IsPlayableLocalVideo(AssetKind.Video, false, staleOnline, true, @"D:\clip.mp4"));
    }

    [Fact]
    public void CanShowPreview_RequiresHydratedLocalOrRemote()
    {
        Assert.True(GalleryMedia.CanShowPreview("https://example.com/p.jpg"));
        Assert.False(GalleryMedia.CanShowPreview(null));
        Assert.False(GalleryMedia.CanShowPreview("OneDrive / me / Photos/a.jpg"));
        Assert.False(GalleryMedia.CanShowPreview(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jpg")));

        var path = Path.Combine(Path.GetTempPath(), "palace-preview-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            Assert.True(GalleryMedia.CanShowPreview(path));
            if (CloudFileTests.TryStampOnlineOnly(path))
            {
                Assert.False(GalleryMedia.CanShowPreview(path));
            }
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    [Fact]
    public void AcceptsProviderThumbnail_OnlyImageType()
    {
        Assert.True(GalleryMedia.AcceptsProviderThumbnail(true));
        Assert.False(GalleryMedia.AcceptsProviderThumbnail(false));
    }

    [Fact]
    public void PlaceholderIcon_UsesKindName()
    {
        Assert.Equal("Image", GalleryMedia.PlaceholderIcon(AssetKind.Image));
        Assert.Equal("Video", GalleryMedia.PlaceholderIcon(AssetKind.Video));
        Assert.Equal("Gif", GalleryMedia.PlaceholderIcon(AssetKind.Gif));
        Assert.Equal("Document", GalleryMedia.PlaceholderIcon(AssetKind.Other));
    }

    [Fact]
    public void ShouldUpgradeThumb_SkipsOnlineOnlyAndMissingHash()
    {
        Assert.True(GalleryMedia.ShouldUpgradeThumb(true, false, false, @"D:\a.jpg", "abc"));
        Assert.False(GalleryMedia.ShouldUpgradeThumb(true, true, false, @"D:\a.jpg", "abc"));
        Assert.False(GalleryMedia.ShouldUpgradeThumb(true, false, false, @"D:\a.jpg", null));
        Assert.False(GalleryMedia.ShouldUpgradeThumb(false, false, false, @"D:\a.jpg", "abc"));
    }

    [Fact]
    public void ShouldUpgradeThumb_SkipsWhenDecodedCacheExists()
    {
        Assert.False(GalleryMedia.ShouldUpgradeThumb(true, false, false, @"D:\a.jpg", "abc", true, @"C:\t.jpg"));
        Assert.True(GalleryMedia.ShouldUpgradeThumb(true, false, false, @"D:\a.jpg", "abc", false, null));
        Assert.True(GalleryMedia.ShouldUpgradeThumb(true, false, false, @"D:\a.jpg", "abc", false, @"C:\t.jpg"));
    }

    [Fact]
    public void ShouldReplaceTileBitmap_OnlyWhenPathChangesOrMissing()
    {
        Assert.False(GalleryMedia.ShouldReplaceTileBitmap(@"C:\t.jpg", @"C:\t.jpg", true));
        Assert.True(GalleryMedia.ShouldReplaceTileBitmap(@"C:\t.jpg", @"C:\t2.jpg", true));
        Assert.True(GalleryMedia.ShouldReplaceTileBitmap(null, @"C:\t.jpg", false));
        Assert.True(GalleryMedia.ShouldReplaceTileBitmap(@"C:\t.jpg", @"C:\t.jpg", false));
    }

    [Fact]
    public void FindAssetId_ReadsTagStringOrIdProperty()
    {
        Assert.Equal("tile-1", GalleryMedia.FindAssetId("tile-1", null));
        Assert.Equal("from-ctx", GalleryMedia.FindAssetId("ignored", new { Id = "from-ctx" }));
        Assert.Null(GalleryMedia.FindAssetId(null, null));
    }

    [Fact]
    public void FindAssetId_ReadsMosaicTagWhenIdIsEmpty()
    {
        Assert.Equal(
            "folder-group:Photos/Vacation",
            GalleryMedia.FindAssetId(null, new { Id = "", MosaicTag = "folder-group:Photos/Vacation" }));
        Assert.True(GalleryMedia.MatchesMosaicKey("", "folder-group:Photos/Vacation", "folder-group:Photos/Vacation"));
        Assert.False(GalleryMedia.MatchesMosaicKey("asset-1", null, "folder-group:Photos/Vacation"));
    }

    [Fact]
    public void IsRemoteUri_DetectsHttp()
    {
        Assert.True(GalleryMedia.IsRemoteUri("https://example.com/p.jpg"));
        Assert.False(GalleryMedia.IsRemoteUri(@"C:\thumbs\p.jpg"));
        Assert.False(GalleryMedia.IsRemoteUri(null));
    }
}
