using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ErrorLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-errlog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public void TryAppend_CreatesDirectoryAndWritesTimestampSourceAndStack()
    {
        var when = new DateTimeOffset(2026, 10, 5, 6, 7, 8, 9, TimeSpan.FromHours(-4));
        var log = new ErrorLog(Path.Combine(_dir, "nested", ErrorLog.FileName), clock: () => when);

        Assert.True(log.TryAppend("Scan library", new InvalidOperationException("boom")));

        var text = File.ReadAllText(log.Path);
        Assert.StartsWith("[2026-10-05T10:07:08.009Z] Scan library", text);
        Assert.Contains("InvalidOperationException: boom", text);
    }

    [Fact]
    public void TryAppend_AppendsEntries()
    {
        var log = new ErrorLog(Path.Combine(_dir, ErrorLog.FileName));
        log.TryAppend("one", new IOException("a"));
        log.TryAppend("two", new IOException("b"));
        var text = File.ReadAllText(log.Path);
        Assert.Contains("one", text);
        Assert.Contains("two", text);
    }

    [Fact]
    public void TryAppend_RotatesOnceWhenOverCap()
    {
        var log = new ErrorLog(Path.Combine(_dir, ErrorLog.FileName), maxBytes: 200);
        for (var i = 0; i < 6; i++)
        {
            Assert.True(log.TryAppend("entry-" + i, new IOException(new string('x', 80))));
        }

        Assert.True(File.Exists(log.PreviousPath));
        Assert.True(File.Exists(log.Path));
        Assert.Contains("entry-5", File.ReadAllText(log.Path));
        Assert.False(File.Exists(log.PreviousPath + ".1"));
    }

    [Fact]
    public void TryAppend_ReturnsFalseInsteadOfThrowing()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "blocked"));
        var log = new ErrorLog(Path.Combine(_dir, "blocked"));
        Assert.False(log.TryAppend("x", new IOException("a")));
    }
}

public sealed class ErrorThrottleTests
{
    [Fact]
    public void ShouldReport_AllowsFirstThenBlocksInsideWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var throttle = new ErrorThrottle(TimeSpan.FromSeconds(5), () => now);
        Assert.True(throttle.ShouldReport("a"));
        Assert.False(throttle.ShouldReport("a"));
        Assert.True(throttle.ShouldReport("b"));
        now += TimeSpan.FromSeconds(5);
        Assert.True(throttle.ShouldReport("a"));
    }

    [Fact]
    public void KeyFor_SameThrowSiteAndMessageMatches_DifferentMessageDiffers()
    {
        static Exception Make(string message)
        {
            try
            {
                throw new InvalidOperationException(message);
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        Assert.Equal(ErrorThrottle.KeyFor(Make("a")), ErrorThrottle.KeyFor(Make("a")));
        Assert.NotEqual(ErrorThrottle.KeyFor(Make("a")), ErrorThrottle.KeyFor(Make("b")));
    }
}
