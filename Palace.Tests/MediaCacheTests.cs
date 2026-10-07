using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class MediaCacheTests
{
    public MediaCacheTests() =>
        MediaCache.Apply(false, MediaCache.DefaultMaxMb, null, null);

    [Fact]
    public void ParseEnabled_ReadsBoolAndString()
    {
        Assert.False(MediaCache.ParseEnabled(null));
        Assert.True(MediaCache.ParseEnabled(true));
        Assert.False(MediaCache.ParseEnabled(false));
        Assert.True(MediaCache.ParseEnabled("true"));
        Assert.False(MediaCache.ParseEnabled("nope"));
    }

    [Fact]
    public void ParseMaxMb_ClampsAndFallsBack()
    {
        Assert.Equal(MediaCache.DefaultMaxMb, MediaCache.ParseMaxMb(null), 2);
        Assert.Equal(1024, MediaCache.ParseMaxMb(1024.0), 2);
        Assert.Equal(512, MediaCache.ParseMaxMb("512"), 2);
        Assert.Equal(MediaCache.MinMaxMb, MediaCache.ParseMaxMb(10), 2);
        Assert.Equal(MediaCache.MaxMaxMb, MediaCache.ParseMaxMb(999999), 2);
    }

    [Fact]
    public void ParsePath_TrimsAndDropsEmpty()
    {
        Assert.Null(MediaCache.ParsePath(null));
        Assert.Null(MediaCache.ParsePath(""));
        Assert.Null(MediaCache.ParsePath("  "));
        Assert.Null(MediaCache.ParsePath(12));
        Assert.Equal(@"D:\cache", MediaCache.ParsePath(@" D:\cache "));
    }

    [Fact]
    public void ResolveThumbsRoot_UsesCustomWhenEnabled()
    {
        var local = Path.Combine(Path.GetTempPath(), "palace-local");
        MediaCache.Apply(false, 2048, @"D:\big-disk\palace-cache", null);
        Assert.Equal(
            Path.Combine(local, MediaCache.ThumbsFolderName),
            MediaCache.ResolveThumbsRoot(local));

        MediaCache.Apply(true, 2048, @"D:\big-disk\palace-cache", "tok");
        Assert.Equal(
            Path.Combine(@"D:\big-disk\palace-cache", MediaCache.ThumbsFolderName),
            MediaCache.ResolveThumbsRoot(local));
        Assert.Equal(
            Path.Combine(@"D:\big-disk\palace-cache", MediaCache.PreviewFolderName),
            MediaCache.ResolvePreviewRoot(local));
    }

    [Fact]
    public void IsAllowedRoot_AllowsEmptyAndRefusesSourceOverlap()
    {
        Assert.True(MediaCache.IsAllowedRoot(null, [], out _));
        Assert.True(MediaCache.IsAllowedRoot("", ["C:\\Photos"], out _));

        var source = Path.Combine(Path.GetTempPath(), "palace-src-" + Guid.NewGuid().ToString("N"));
        var otherProjectSource = Path.Combine(Path.GetTempPath(), "palace-src2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(otherProjectSource);
        try
        {
            var nested = Path.Combine(source, "cache");
            Directory.CreateDirectory(nested);
            Assert.False(MediaCache.IsAllowedRoot(nested, [source], out var reason));
            Assert.Contains("source", reason!, StringComparison.OrdinalIgnoreCase);

            // All-project list: overlap with another project's source is refused.
            var underOther = Path.Combine(otherProjectSource, "cache");
            Directory.CreateDirectory(underOther);
            Assert.False(MediaCache.IsAllowedRoot(underOther, [source, otherProjectSource], out _));

            var outside = Path.Combine(Path.GetTempPath(), "palace-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
            try
            {
                Assert.True(MediaCache.IsAllowedRoot(outside, [source, otherProjectSource], out _));
            }
            finally
            {
                Directory.Delete(outside, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            Directory.Delete(otherProjectSource, recursive: true);
        }
    }

    [Fact]
    public void IsAllowedSource_RefusesOverlapWithCacheRoot()
    {
        var cache = Path.Combine(Path.GetTempPath(), "palace-cache-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        try
        {
            Assert.False(MediaCache.IsAllowedSource(cache, cache, out var same));
            Assert.Contains("cache", same!, StringComparison.OrdinalIgnoreCase);

            var nested = Path.Combine(cache, "photos");
            Directory.CreateDirectory(nested);
            Assert.False(MediaCache.IsAllowedSource(nested, cache, out _));

            var parent = Path.GetDirectoryName(cache)!;
            Assert.False(MediaCache.IsAllowedSource(parent, cache, out _));

            var outside = Path.Combine(Path.GetTempPath(), "palace-ok-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
            try
            {
                Assert.True(MediaCache.IsAllowedSource(outside, cache, out _));
            }
            finally
            {
                Directory.Delete(outside, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public void EnsureRoots_FallsBackWhenCustomRootUnwritable()
    {
        var local = Path.Combine(Path.GetTempPath(), "palace-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(local);
        // A file where the cache root should be — CreateDirectory fails.
        var blocked = Path.Combine(Path.GetTempPath(), "palace-blocked-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocked, "not-a-dir");
        try
        {
            MediaCache.Apply(true, MediaCache.DefaultMaxMb, blocked, null);
            var used = MediaCache.EnsureRoots(local, out var thumbs, out var fellBack);
            Assert.True(fellBack);
            Assert.True(MediaCache.UsingLocalFallback);
            Assert.Equal(Path.GetFullPath(local), Path.GetFullPath(used));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(local, MediaCache.ThumbsFolderName)),
                Path.GetFullPath(thumbs));
            Assert.True(Directory.Exists(thumbs));
            Assert.Contains("unavailable", MediaCache.RootLabel(local), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(blocked);
            if (Directory.Exists(local))
            {
                Directory.Delete(local, recursive: true);
            }

            MediaCache.Apply(false, MediaCache.DefaultMaxMb, null, null);
        }
    }

    [Fact]
    public void EnsureRoots_UsesCustomWhenWritable()
    {
        var local = Path.Combine(Path.GetTempPath(), "palace-local2-" + Guid.NewGuid().ToString("N"));
        var custom = Path.Combine(Path.GetTempPath(), "palace-custom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(local);
        try
        {
            MediaCache.Apply(true, MediaCache.DefaultMaxMb, custom, "tok");
            var used = MediaCache.EnsureRoots(local, out var thumbs, out var fellBack);
            Assert.False(fellBack);
            Assert.False(MediaCache.UsingLocalFallback);
            Assert.Equal(Path.GetFullPath(custom), Path.GetFullPath(used));
            Assert.True(Directory.Exists(thumbs));
            Assert.True(Directory.Exists(Path.Combine(custom, MediaCache.PreviewFolderName)));
        }
        finally
        {
            if (Directory.Exists(local))
            {
                Directory.Delete(local, recursive: true);
            }

            if (Directory.Exists(custom))
            {
                Directory.Delete(custom, recursive: true);
            }

            MediaCache.Apply(false, MediaCache.DefaultMaxMb, null, null);
        }
    }

    [Fact]
    public void PlanEviction_RemovesOldestUntilUnderHeadroom()
    {
        var t0 = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var files = new[]
        {
            new CacheFileInfo("a.jpg", 100, t0, t0),
            new CacheFileInfo("b.jpg", 100, t0.AddHours(1), t0.AddHours(1)),
            new CacheFileInfo("c.jpg", 100, t0.AddHours(2), t0.AddHours(2)),
        };
        // Budget 200; headroom 90% → target 180. Total 300 → drop a then b.
        var plan = MediaCache.PlanEviction(files, totalBytes: 300, maxBytes: 200);
        Assert.Equal(["a.jpg", "b.jpg"], plan);
        Assert.Empty(MediaCache.PlanEviction(files, totalBytes: 150, maxBytes: 200));
    }

    [Fact]
    public void EnforceMaxSize_NoOpWhenDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "palace-cache-off-" + Guid.NewGuid().ToString("N"));
        var thumbs = Path.Combine(root, MediaCache.ThumbsFolderName);
        Directory.CreateDirectory(thumbs);
        var path = Path.Combine(thumbs, "x.jpg");
        File.WriteAllBytes(path, new byte[1024]);
        try
        {
            MediaCache.Apply(false, MediaCache.MinMaxMb, root, null);
            Assert.Equal(0, MediaCache.EnforceMaxSize(root, maxBytes: 1));
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            MediaCache.Apply(false, MediaCache.DefaultMaxMb, null, null);
        }
    }

    [Fact]
    public void EnforceMaxSize_DeletesOldestWhenOverBudget()
    {
        var root = Path.Combine(Path.GetTempPath(), "palace-cache-on-" + Guid.NewGuid().ToString("N"));
        var thumbs = Path.Combine(root, MediaCache.ThumbsFolderName);
        Directory.CreateDirectory(thumbs);
        var oldPath = Path.Combine(thumbs, "old.jpg");
        var newPath = Path.Combine(thumbs, "new.jpg");
        File.WriteAllBytes(oldPath, new byte[800]);
        File.WriteAllBytes(newPath, new byte[800]);
        File.SetLastAccessTimeUtc(oldPath, DateTime.UtcNow.AddDays(-2));
        File.SetLastAccessTimeUtc(newPath, DateTime.UtcNow);
        try
        {
            MediaCache.Apply(true, MediaCache.MinMaxMb, root, null);
            var deleted = MediaCache.EnforceMaxSize(root, maxBytes: 1000);
            Assert.True(deleted >= 800);
            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(newPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            MediaCache.Apply(false, MediaCache.DefaultMaxMb, null, null);
        }
    }

    [Fact]
    public void Clear_RemovesThumbsAndPreviewOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "palace-cache-clear-" + Guid.NewGuid().ToString("N"));
        var thumbs = Path.Combine(root, MediaCache.ThumbsFolderName);
        var preview = Path.Combine(root, MediaCache.PreviewFolderName);
        Directory.CreateDirectory(thumbs);
        Directory.CreateDirectory(preview);
        File.WriteAllBytes(Path.Combine(thumbs, "a.jpg"), new byte[10]);
        File.WriteAllBytes(Path.Combine(preview, "b.bin"), new byte[20]);
        File.WriteAllText(Path.Combine(root, "keep.txt"), "stay");
        try
        {
            var deleted = MediaCache.Clear(root);
            Assert.Equal(30, deleted);
            Assert.False(File.Exists(Path.Combine(thumbs, "a.jpg")));
            Assert.False(File.Exists(Path.Combine(preview, "b.bin")));
            Assert.True(File.Exists(Path.Combine(root, "keep.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Apply_SetsStateAndFiresChanged()
    {
        var fired = 0;
        void OnChanged(object? _, EventArgs e) => fired++;
        MediaCache.Changed += OnChanged;
        try
        {
            MediaCache.Apply(true, 10, @"C:\cache", "tok");
            Assert.True(MediaCache.Enabled);
            Assert.Equal(MediaCache.MinMaxMb, MediaCache.MaxMb, 2);
            Assert.Equal(@"C:\cache", MediaCache.RootPath);
            Assert.Equal("tok", MediaCache.AccessToken);
            Assert.Equal(1, fired);

            MediaCache.Apply(true, MediaCache.MinMaxMb, @"C:\cache", "tok");
            Assert.Equal(1, fired);

            MediaCache.Apply(false, 4096, null, null);
            Assert.False(MediaCache.Enabled);
            Assert.Null(MediaCache.RootPath);
            Assert.Equal(2, fired);
        }
        finally
        {
            MediaCache.Changed -= OnChanged;
            MediaCache.Apply(false, MediaCache.DefaultMaxMb, null, null);
        }
    }

    [Fact]
    public void UsageAndMaxLabels_Format()
    {
        Assert.Equal("2048 MB", MediaCache.MaxMbLabel(2048));
        Assert.Contains("MB", MediaCache.UsageLabel(5 * 1024 * 1024, 2048L * 1024 * 1024));
    }
}
