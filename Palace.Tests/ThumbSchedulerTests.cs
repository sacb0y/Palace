using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public class ThumbSchedulerTests
{
    private static ThumbRect View => new(0, 0, 800, 400);

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(4, 3)]
    [InlineData(8, 4)]
    [InlineData(16, 4)]
    public void ConcurrencyCap_ScalesBetweenTwoAndFour(int cores, int expected) =>
        Assert.Equal(expected, ThumbScheduler.ConcurrencyCap(cores));

    [Fact]
    public void DecodePixels_UsesDisplayScaleAndNeverFullResolution()
    {
        var tile = ThumbScheduler.DecodePixels(140, 140, 2);
        Assert.Equal((280, 280), tile);

        var preview = ThumbScheduler.DecodePixels(340, 220, 1.5);
        Assert.Equal((510, 330), preview);

        var huge = ThumbScheduler.DecodePixels(8000, 4000, 2);
        Assert.Equal(ThumbScheduler.MaxDecodePixels, huge.Width);
        Assert.Equal(ThumbScheduler.MaxDecodePixels, huge.Height);
        Assert.True(huge.Width < 3840 * 2);
    }

    [Fact]
    public void PreviewSource_UsesDiskThumbWhenStillIsLargerThanTheCap()
    {
        var shown = ThumbScheduler.PreviewSource(@"C:\pics\fourk.jpg", @"C:\cache\fourk.jpg", 3840, 680);
        Assert.Equal(@"C:\cache\fourk.jpg", shown);

        var small = ThumbScheduler.PreviewSource(@"C:\pics\small.jpg", @"C:\cache\small.jpg", 400, 680);
        Assert.Equal(@"C:\pics\small.jpg", small);

        var unknown = ThumbScheduler.PreviewSource(@"C:\pics\fourk.jpg", @"C:\cache\fourk.jpg", 0, 680);
        Assert.Equal(@"C:\pics\fourk.jpg", unknown);
    }

    [Fact]
    public void Order_VisibleFirst_TopToBottomThenLeftToRight_ThenPrefetch()
    {
        var tiles = new ThumbTile[]
        {
            new("prefetch-right", 400, 500, 100, 100),
            new("visible-lower-right", 300, 200, 100, 80),
            new("outside", 0, 2000, 100, 100),
            new("visible-top-right", 200, 10, 100, 80),
            new("prefetch-top", 0, 420, 100, 100),
            new("visible-top-left", 0, 10, 100, 80),
            new("visible-lower-left", 0, 200, 120, 80)
        };

        var order = ThumbScheduler.Order(tiles, View, prefetchDip: 280).Select(tile => tile.Key).ToList();

        Assert.Equal(
            [
                "visible-top-left",
                "visible-top-right",
                "visible-lower-left",
                "visible-lower-right",
                "prefetch-top",
                "prefetch-right"
            ],
            order);
        Assert.DoesNotContain("outside", order);
    }

    [Fact]
    public void Queue_CapsInFlight_ThenAdmitsNextVisibleBeforePrefetch()
    {
        var queue = new ThumbLoadQueue(2);
        var tiles = new ThumbTile[]
        {
            new("v-bottom", 0, 300, 100, 80),
            new("prefetch", 0, 500, 100, 80),
            new("v-top", 0, 10, 100, 80),
            new("v-mid", 0, 150, 100, 80)
        };

        var first = queue.Plan(tiles, View, 280);
        Assert.Equal(2, first.Cap);
        Assert.Equal(["v-top", "v-mid"], first.Start);
        Assert.Equal(2, first.InFlight);
        Assert.Empty(first.Cancel);

        queue.Complete("v-top");
        var second = queue.Plan(tiles, View, 280);
        Assert.Equal(["v-bottom"], second.Start);
        Assert.DoesNotContain("prefetch", second.Start);

        queue.Complete("v-mid");
        queue.Complete("v-bottom");
        var third = queue.Plan(tiles, View, 280);
        Assert.Equal(["prefetch"], third.Start);
    }

    [Fact]
    public void Queue_CancelsWorkThatLeavesTheViewportAndPrefetchBand()
    {
        var queue = new ThumbLoadQueue(2);
        var before = new ThumbTile[]
        {
            new("leaving", 0, 10, 100, 80),
            new("staying", 200, 10, 100, 80)
        };
        var started = queue.Plan(before, View, 280);
        Assert.Equal(["leaving", "staying"], started.Start);

        var afterScroll = new ThumbTile[]
        {
            new("leaving", 0, 10, 100, 80),
            new("staying", 200, 10, 100, 80),
            new("incoming", 0, 900, 100, 80)
        };
        var scrolled = queue.Plan(afterScroll, new ThumbRect(0, 800, 800, 400), 280);

        Assert.Contains("leaving", scrolled.Cancel);
        Assert.Contains("staying", scrolled.Cancel);
        Assert.Equal(["incoming"], scrolled.Start);
        Assert.Equal(1, queue.InFlight);
        Assert.DoesNotContain("leaving", scrolled.Start);

        queue.Reset();
        Assert.Equal(0, queue.InFlight);
        var again = queue.Plan(before, View, 280);
        Assert.Equal(["leaving", "staying"], again.Start);
    }

    [Fact]
    public void MemoryCache_EvictsLeastRecentlyUsed()
    {
        var cache = new ThumbMemoryCache<string>(2);
        cache.Set("a", "A");
        cache.Set("b", "B");
        Assert.True(cache.TryGet("a", out var hit));
        Assert.Equal("A", hit);
        cache.Set("c", "C");
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void TimingLines_NameQueueDepthCacheAndCappedPreview()
    {
        var show = ThumbTimingLog.LibraryShowLine(pageReused: true, previewKept: true, queue: 1, cap: 3, cacheCount: 12, threadId: 1);
        Assert.Contains("page=reused", show, StringComparison.Ordinal);
        Assert.Contains("preview=kept", show, StringComparison.Ordinal);
        Assert.Contains("queue=1", show, StringComparison.Ordinal);
        Assert.Contains("cap=3", show, StringComparison.Ordinal);

        var preview = ThumbTimingLog.PreviewLine(true, 510, 330, 1.5, "disk-thumb", 3840, 1);
        Assert.Contains("capped=true", preview, StringComparison.Ordinal);
        Assert.Contains("size=510x330", preview, StringComparison.Ordinal);
        Assert.Contains("source=disk-thumb", preview, StringComparison.Ordinal);

        var tile = ThumbTimingLog.TileLine("hit", "hit", 4, 280, 180, 2, 3, 7, 9, 1);
        Assert.Contains("cache=hit", tile, StringComparison.Ordinal);
        Assert.Contains("capped=true", tile, StringComparison.Ordinal);
        Assert.Contains("startThread=7", tile, StringComparison.Ordinal);
        Assert.Contains("ensureThread=9", tile, StringComparison.Ordinal);
        Assert.Contains("assignThread=1", tile, StringComparison.Ordinal);

        var dir = Path.Combine(Path.GetTempPath(), "palace-thumb-log-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, ThumbTimingLog.FileName);
        ThumbTimingLog.Configure(path);
        try
        {
            Assert.True(ThumbTimingLog.Write(show));
            var text = File.ReadAllText(path);
            Assert.Contains("library-show page=reused", text, StringComparison.Ordinal);
        }
        finally
        {
            ThumbTimingLog.Configure(null);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
