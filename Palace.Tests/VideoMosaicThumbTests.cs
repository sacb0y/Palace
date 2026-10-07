using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class VideoMosaicThumbTests
{
    [Fact]
    public void AutomationIds_AreStable()
    {
        Assert.Equal("BtnGallerySetThumb", VideoMosaicThumb.OverlayAutomationId);
        Assert.Equal("BtnGalleryWindowSetThumb", VideoMosaicThumb.WindowAutomationId);
    }

    [Fact]
    public void IsActionVisible_OnlyPlayableLocalVideo()
    {
        Assert.True(VideoMosaicThumb.IsActionVisible(true));
        Assert.False(VideoMosaicThumb.IsActionVisible(false));
    }

    [Fact]
    public void CanSet_RequiresHashAndLocalPath()
    {
        Assert.True(VideoMosaicThumb.CanSet(true, "abc123", @"C:\vid.mp4"));
        Assert.False(VideoMosaicThumb.CanSet(false, "abc123", @"C:\vid.mp4"));
        Assert.False(VideoMosaicThumb.CanSet(true, null, @"C:\vid.mp4"));
        Assert.False(VideoMosaicThumb.CanSet(true, "", @"C:\vid.mp4"));
        Assert.False(VideoMosaicThumb.CanSet(true, "abc123", null));
        Assert.False(VideoMosaicThumb.CanSet(true, "abc123", ""));
    }

    [Fact]
    public void DisabledReason_CloudAndMissingHash()
    {
        Assert.Contains(
            "online-only",
            VideoMosaicThumb.DisabledReason(false, "h", @"C:\v.mp4", isOnlineOnly: true, apiOnly: false),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "cloud",
            VideoMosaicThumb.DisabledReason(false, "h", @"C:\v.mp4", isOnlineOnly: false, apiOnly: true),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "hash",
            VideoMosaicThumb.DisabledReason(true, null, @"C:\v.mp4", isOnlineOnly: false, apiOnly: false),
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(VideoMosaicThumb.DisabledReason(true, "h", @"C:\v.mp4", isOnlineOnly: false, apiOnly: false));
    }

    [Fact]
    public void ScaledSize_FitsMaxSideAndPreservesAspect()
    {
        var (w, h) = VideoMosaicThumb.ScaledSize(1920, 1080, 512);
        Assert.Equal(512, w);
        Assert.Equal(288, h);

        var square = VideoMosaicThumb.ScaledSize(0, 0, 512);
        Assert.Equal((512, 512), square);

        var alreadySmall = VideoMosaicThumb.ScaledSize(320, 240, 512);
        Assert.Equal((320, 240), alreadySmall);
    }

    [Fact]
    public void ClampPosition_StaysInsideDuration()
    {
        Assert.Equal(TimeSpan.Zero, VideoMosaicThumb.ClampPosition(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(3), VideoMosaicThumb.ClampPosition(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)));
        var nearEnd = VideoMosaicThumb.ClampPosition(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        Assert.True(nearEnd < TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(5), VideoMosaicThumb.ClampPosition(TimeSpan.FromSeconds(5), TimeSpan.Zero));
    }

    [Fact]
    public void MosaicCache_UsesBareHashSuffix()
    {
        Assert.Null(VideoMosaicThumb.MosaicSuffix);
        Assert.True(VideoMosaicThumb.UsesMosaicCacheName(null));
        Assert.True(VideoMosaicThumb.UsesMosaicCacheName(""));
        Assert.False(VideoMosaicThumb.UsesMosaicCacheName("_lg"));
        Assert.Equal("deadbeef.jpg", ThumbFileName.FileName("deadbeef", VideoMosaicThumb.MosaicSuffix));
    }
}
