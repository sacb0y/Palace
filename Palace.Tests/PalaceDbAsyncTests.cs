using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Palace.Data;
using Xunit;

namespace Palace.Tests;

public sealed class PalaceDbAsyncTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-db-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PalaceDb _db;

    public PalaceDbAsyncTests()
    {
        _db = new PalaceDb(Path.Combine(_dir, "palace.db"));
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task ReadAsync_returns_query_results()
    {
        var projects = await _db.ReadAsync(conn => Scalar(conn, "SELECT COUNT(*) FROM Project"));
        Assert.True(projects >= 1);
    }

    [Fact]
    public async Task WriteAsync_with_and_without_result_persist()
    {
        await _db.WriteAsync(conn => Exec(conn, "CREATE TABLE T (V INTEGER)"));
        var rows = await _db.WriteAsync(conn =>
        {
            Exec(conn, "INSERT INTO T (V) VALUES (1), (2), (3)");
            return Scalar(conn, "SELECT COUNT(*) FROM T");
        });
        Assert.Equal(3, rows);
        Assert.Equal(6, await _db.ReadAsync(conn => Scalar(conn, "SELECT SUM(V) FROM T")));
    }

    [Fact]
    public async Task Work_runs_off_the_callers_synchronization_context_and_resumes_on_it()
    {
        using var ui = new PumpContext();
        var result = await ui.RunAsync(async () =>
        {
            var uiThread = Environment.CurrentManagedThreadId;
            var workThread = await _db.ReadAsync(_ => Environment.CurrentManagedThreadId);
            var writeThread = await _db.WriteAsync(_ => Environment.CurrentManagedThreadId);
            return (uiThread, workThread, writeThread, resumed: Environment.CurrentManagedThreadId);
        });

        Assert.NotEqual(result.uiThread, result.workThread);
        Assert.NotEqual(result.uiThread, result.writeThread);
        Assert.Equal(result.uiThread, result.resumed);
    }

    [Fact]
    public async Task Work_does_not_block_the_calling_thread()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var pending = _db.ReadAsync(_ =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return 1;
        });

        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        Assert.False(pending.IsCompleted);
        release.Set();
        Assert.Equal(1, await pending);
    }

    [Fact]
    public async Task Concurrent_calls_are_serialized_on_the_single_connection()
    {
        var active = 0;
        var maxActive = 0;
        var total = 0;

        int Work(SqliteConnection _)
        {
            var now = Interlocked.Increment(ref active);
            InterlockedMax(ref maxActive, now);
            Thread.Sleep(2);
            Interlocked.Increment(ref total);
            Interlocked.Decrement(ref active);
            return now;
        }

        var tasks = Enumerable.Range(0, 40)
            .Select(i => i % 2 == 0 ? _db.ReadAsync(Work) : _db.WriteAsync(Work))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(40, total);
        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task Exceptions_propagate_and_release_the_gate()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _db.ReadAsync<int>(_ => throw new InvalidOperationException("boom")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _db.WriteAsync(_ => throw new InvalidOperationException("boom")));
        await Assert.ThrowsAsync<SqliteException>(() =>
            _db.WriteAsync(conn => Exec(conn, "INSERT INTO NoSuchTable VALUES (1)")));

        var ok = await _db.ReadAsync(conn => Scalar(conn, "SELECT 1")).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, ok);
    }

    [Fact]
    public async Task Cancelling_a_queued_call_skips_its_work_and_keeps_the_gate_usable()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource();
        var ran = 0;

        var blocker = _db.ReadAsync(_ =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return 0;
        });
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));

        var queued = _db.ReadAsync(
            _ =>
            {
                Interlocked.Increment(ref ran);
                return 1;
            },
            cts.Token);
        var queuedWrite = _db.WriteAsync(_ => Interlocked.Increment(ref ran), cts.Token);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedWrite);

        release.Set();
        await blocker;

        Assert.Equal(0, ran);
        Assert.Equal(1, await _db.ReadAsync(conn => Scalar(conn, "SELECT 1")).WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task A_pre_cancelled_token_never_runs_the_work()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ran = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _db.ReadAsync(_ =>
        {
            ran = true;
            return 0;
        }, cts.Token));

        Assert.False(ran);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref target);
            if (value <= seen)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, seen) != seen);
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static int Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Single dedicated thread with a <see cref="SynchronizationContext"/>, standing in for the WinUI dispatcher.
    /// </summary>
    private sealed class PumpContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public PumpContext()
        {
            _thread = new Thread(Pump) { IsBackground = true, Name = "fake-ui" };
            _thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public Task<T> RunAsync<T>(Func<Task<T>> body)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(
                async _ =>
                {
                    try
                    {
                        tcs.SetResult(await body());
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                },
                null);
            return tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }

        private void Pump()
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }
    }
}
