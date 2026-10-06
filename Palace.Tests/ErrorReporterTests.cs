using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

[Collection(ErrorReporterCollection.Name)]
public sealed class ErrorReporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-errors-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _presented = [];

    public ErrorReporterTests()
    {
        ErrorReporter.Configure(new ErrorLog(Path.Combine(_dir, ErrorLog.FileName)), _presented.Add);
    }

    public void Dispose()
    {
        ErrorReporter.Configure(null);
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    private string LogText() => File.ReadAllText(Path.Combine(_dir, ErrorLog.FileName));

    [Fact]
    public void IsCancellation_CoversCanceledAndAggregateOfCanceled()
    {
        Assert.True(ErrorReporter.IsCancellation(new OperationCanceledException()));
        Assert.True(ErrorReporter.IsCancellation(new TaskCanceledException()));
        Assert.True(ErrorReporter.IsCancellation(new AggregateException(new TaskCanceledException(), new OperationCanceledException())));
        Assert.False(ErrorReporter.IsCancellation(new AggregateException(new OperationCanceledException(), new IOException())));
        Assert.False(ErrorReporter.IsCancellation(new AggregateException()));
        Assert.False(ErrorReporter.IsCancellation(new IOException()));
    }

    [Fact]
    public void IsFatal_IsOnlyCorruptionClassFailures()
    {
        Assert.True(ErrorReporter.IsFatal(new OutOfMemoryException()));
        Assert.True(ErrorReporter.IsFatal(new AccessViolationException()));
        Assert.False(ErrorReporter.IsFatal(new InvalidOperationException()));
    }

    [Fact]
    public void UserMessage_UsesFirstLineOfInnermostMessage()
    {
        var ex = new AggregateException(new InvalidOperationException("disk gone\r\nsecond line"));
        Assert.Equal("Scan failed: disk gone", ErrorReporter.UserMessage("Scan", ex));
    }

    [Fact]
    public void UserMessage_FallsBackToTypeNameAndTruncates()
    {
        Assert.Equal("Scan failed: " + nameof(EmptyMessageException), ErrorReporter.UserMessage("Scan", new EmptyMessageException()));
        var text = ErrorReporter.UserMessage("Scan", new IOException(new string('x', 500)));
        Assert.EndsWith("\u2026", text);
        Assert.True(text.Length <= "Scan failed: ".Length + 200);
    }

    [Fact]
    public void UnhandledMessage_SaysTheAppKeptRunning()
    {
        Assert.Equal(
            "Palace hit an unexpected error and kept running: boom",
            ErrorReporter.UnhandledMessage(new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task RunAsync_Success_DoesNotLogOrNotify()
    {
        var notified = new List<string>();
        var ran = false;
        await ErrorReporter.RunAsync("Scan", notified.Add, () =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        Assert.True(ran);
        Assert.Empty(notified);
        Assert.Empty(_presented);
        Assert.False(File.Exists(Path.Combine(_dir, ErrorLog.FileName)));
    }

    [Fact]
    public async Task RunAsync_Failure_LogsAndNotifiesWithoutThrowing()
    {
        var notified = new List<string>();
        await ErrorReporter.RunAsync("Scan", notified.Add, async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        });

        Assert.Equal(["Scan failed: boom"], notified);
        Assert.Empty(_presented);
        var log = LogText();
        Assert.Contains("Scan", log);
        Assert.Contains("InvalidOperationException: boom", log);
    }

    [Fact]
    public async Task RunAsync_SynchronousThrowInsideDelegate_IsCaught()
    {
        var notified = new List<string>();
        await ErrorReporter.RunAsync("Scan", notified.Add, () => throw new IOException("sync"));
        Assert.Equal(["Scan failed: sync"], notified);
    }

    [Fact]
    public async Task RunAsync_NullNotify_UsesGlobalPresenter()
    {
        await ErrorReporter.RunAsync("Open", null, () => throw new IOException("nope"));
        Assert.Equal(["Open failed: nope"], _presented);
    }

    [Fact]
    public async Task RunAsync_Cancellation_IsNotAnError()
    {
        var notified = new List<string>();
        await ErrorReporter.RunAsync("Scan", notified.Add, () => throw new OperationCanceledException());
        await ErrorReporter.RunAsync("Scan", notified.Add, async () =>
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            await Task.Delay(10, cts.Token);
        });

        Assert.Empty(notified);
        Assert.Empty(_presented);
        Assert.False(File.Exists(Path.Combine(_dir, ErrorLog.FileName)));
    }

    [Fact]
    public async Task RunAsync_Fatal_IsNotSwallowed()
    {
        await Assert.ThrowsAsync<OutOfMemoryException>(() =>
            ErrorReporter.RunAsync("Scan", null, () => throw new OutOfMemoryException()));
    }

    [Fact]
    public async Task RunAsync_NotifierThatThrows_DoesNotEscape()
    {
        await ErrorReporter.RunAsync("Scan", _ => throw new InvalidOperationException("ui gone"), () => throw new IOException("boom"));
        Assert.Contains("boom", LogText());
    }

    [Fact]
    public void Run_Failure_LogsAndNotifies()
    {
        var notified = new List<string>();
        ErrorReporter.Run("Toggle", notified.Add, () => throw new InvalidOperationException("bad"));
        Assert.Equal(["Toggle failed: bad"], notified);
    }

    [Fact]
    public void Report_UnwritableLog_StillNotifies()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "as-dir"));
        ErrorReporter.Configure(new ErrorLog(Path.Combine(_dir, "as-dir")), _presented.Add);
        Assert.True(ErrorReporter.Report("Scan", new IOException("boom")));
        Assert.Equal(["Scan failed: boom"], _presented);
    }

    [Fact]
    public void Report_Cancellation_ReturnsFalse()
    {
        Assert.False(ErrorReporter.Report("Scan", new TaskCanceledException()));
        Assert.Empty(_presented);
    }

    [Fact]
    public void ReportUnhandled_ThrottlesRepeatsOfTheSameFailure()
    {
        var now = DateTimeOffset.UtcNow;
        ErrorReporter.Configure(
            new ErrorLog(Path.Combine(_dir, ErrorLog.FileName)),
            _presented.Add,
            new ErrorThrottle(TimeSpan.FromSeconds(5), () => now));

        ErrorReporter.ReportUnhandled("UI", Throw("same"));
        ErrorReporter.ReportUnhandled("UI", Throw("same"));
        Assert.Single(_presented);

        ErrorReporter.ReportUnhandled("UI", Throw("different"));
        Assert.Equal(2, _presented.Count);

        now += TimeSpan.FromSeconds(6);
        ErrorReporter.ReportUnhandled("UI", Throw("same"));
        Assert.Equal(3, _presented.Count);
    }

    [Fact]
    public void ReportUnhandled_NotifyUserFalse_OnlyLogs()
    {
        ErrorReporter.ReportUnhandled("Task", new IOException("quiet"), notifyUser: false);
        Assert.Empty(_presented);
        Assert.Contains("quiet", LogText());
    }

    private static Exception Throw(string message)
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

    private sealed class EmptyMessageException : Exception
    {
        public EmptyMessageException()
            : base("")
        {
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ErrorReporterCollection
{
    public const string Name = "ErrorReporter static state";
}
