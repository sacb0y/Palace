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
    public void ShouldLoadTileThumb_AllowsMissingJpegWhenHashExists()
    {
        Assert.True(GalleryMedia.ShouldLoadTileThumb(false, false, false, null, "abc"));
        Assert.False(GalleryMedia.ShouldLoadTileThumb(false, false, false, null, null));
        Assert.False(GalleryMedia.ShouldLoadTileThumb(true, false, false, @"C:\t.jpg", "abc"));
        Assert.False(GalleryMedia.ShouldLoadTileThumb(false, true, false, @"C:\t.jpg", "abc"));
        Assert.False(GalleryMedia.ShouldLoadTileThumb(false, false, true, @"C:\t.jpg", "abc"));
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
