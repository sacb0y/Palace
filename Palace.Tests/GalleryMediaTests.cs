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
    public void ShouldLoadTileThumb_AllowsMissingJpegWhenHashExists()
    {
        Assert.True(GalleryMedia.ShouldLoadTileThumb(false, false, false, null, "abc"));
        Assert.False(GalleryMedia.ShouldLoadTileThumb(false, false, false, null, null));
        Assert.False(GalleryMedia.ShouldLoadTileThumb(true, false, false, @"C:\t.jpg", "abc"));
        Assert.False(GalleryMedia.ShouldLoadTileThumb(false, true, false, @"C:\t.jpg", "abc"));
        Assert.False(GalleryMedia.ShouldLoadTileThumb(false, false, true, @"C:\t.jpg", "abc"));
    }

    [Fact]
    public void ShouldRequestMosaicThumb_AllowsOnlineOnlyPlaceholders()
    {
        Assert.True(GalleryMedia.ShouldRequestMosaicThumb(false, @"D:\cloud\a.jpg", "abc", null));
        Assert.False(GalleryMedia.ShouldRequestMosaicThumb(true, @"D:\cloud\a.jpg", "abc", null));
        Assert.False(GalleryMedia.ShouldRequestMosaicThumb(false, @"D:\cloud\a.jpg", "abc", @"C:\t.jpg"));
        Assert.False(GalleryMedia.ShouldRequestMosaicThumb(false, null, "abc", null));
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
    public void IsRemoteUri_DetectsHttp()
    {
        Assert.True(GalleryMedia.IsRemoteUri("https://example.com/p.jpg"));
        Assert.False(GalleryMedia.IsRemoteUri(@"C:\thumbs\p.jpg"));
        Assert.False(GalleryMedia.IsRemoteUri(null));
    }
}
