using System.Globalization;

namespace Palace.Helpers;

/// <summary>
/// Call-rate probe for <c>RefreshLibraryLayout</c>. FENRIR harness heartbeats every
/// <see cref="HeartbeatMs"/> and fails on stall (&gt;<see cref="StallFailMs"/> with no
/// call while work expected) or hot idle (&gt;<see cref="MaxIdleCallsPerSecond"/>/s
/// while layout should be quiet). Off WinUI for Palace.Tests.
/// </summary>
public static class LibraryLayoutRefreshProbe
{
    public const int HeartbeatMs = 250;
    public const int StallFailMs = 1000;
    public const double MaxIdleCallsPerSecond = 10;
    public const int RateWindowMs = 1000;

    private static readonly object Gate = new();
    private static readonly Queue<long> CallTicksMs = new();
    private static long _lastCallMs = -1;
    private static long _lastLogMs = -1;
    private static int _totalCalls;

    /// <summary>When true, <see cref="RecordCall"/> writes a rate line at most once per heartbeat.</summary>
    public static bool LogEnabled { get; set; }

    /// <summary>Optional sink (tests / harness). Default: Debug.WriteLine when <see cref="LogEnabled"/>.</summary>
    public static Action<string>? LogSink { get; set; }

    public static int TotalCalls
    {
        get
        {
            lock (Gate)
            {
                return _totalCalls;
            }
        }
    }

    public static long LastCallMs
    {
        get
        {
            lock (Gate)
            {
                return _lastCallMs;
            }
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            CallTicksMs.Clear();
            _lastCallMs = -1;
            _lastLogMs = -1;
            _totalCalls = 0;
        }
    }

    /// <summary>Record one RefreshLibraryLayout entry (including re-entrant / no-op returns).</summary>
    public static void RecordCall(long tickMs = -1)
    {
        var now = tickMs >= 0 ? tickMs : Environment.TickCount64;
        double rate;
        lock (Gate)
        {
            _totalCalls++;
            _lastCallMs = now;
            CallTicksMs.Enqueue(now);
            TrimWindow_NoLock(now);
            rate = RatePerSecond_NoLock(now);
            if (LogEnabled && (now - _lastLogMs >= HeartbeatMs || _lastLogMs < 0))
            {
                _lastLogMs = now;
                var line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"LibraryLayoutRefresh rate={rate:0.##}/s total={_totalCalls} last={now}");
                if (LogSink is not null)
                {
                    LogSink(line);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(line);
                }
            }
        }
    }

    public static double CallsPerSecond(long nowMs = -1)
    {
        var now = nowMs >= 0 ? nowMs : Environment.TickCount64;
        lock (Gate)
        {
            TrimWindow_NoLock(now);
            return RatePerSecond_NoLock(now);
        }
    }

    public static int CallsInWindow(long nowMs = -1, int windowMs = RateWindowMs)
    {
        var now = nowMs >= 0 ? nowMs : Environment.TickCount64;
        lock (Gate)
        {
            TrimWindow_NoLock(now, windowMs);
            return CallTicksMs.Count;
        }
    }

    /// <summary>
    /// Harness heartbeat verdict. <paramref name="expectingProgress"/> true while a
    /// layout pass should still run; false when UI should be idle.
    /// </summary>
    public static LibraryLayoutHangVerdict Evaluate(
        long nowMs,
        bool expectingProgress)
    {
        long last;
        double rate;
        lock (Gate)
        {
            TrimWindow_NoLock(nowMs);
            last = _lastCallMs;
            rate = RatePerSecond_NoLock(nowMs);
        }

        if (expectingProgress)
        {
            if (last < 0 || nowMs - last > StallFailMs)
            {
                return LibraryLayoutHangVerdict.Stall;
            }
        }
        else if (rate > MaxIdleCallsPerSecond)
        {
            return LibraryLayoutHangVerdict.HotIdle;
        }

        return LibraryLayoutHangVerdict.Ok;
    }

    private static void TrimWindow_NoLock(long nowMs, int windowMs = RateWindowMs)
    {
        var floor = nowMs - windowMs;
        while (CallTicksMs.Count > 0 && CallTicksMs.Peek() < floor)
        {
            CallTicksMs.Dequeue();
        }
    }

    private static double RatePerSecond_NoLock(long nowMs)
    {
        if (CallTicksMs.Count == 0)
        {
            return 0;
        }

        return CallTicksMs.Count * (1000.0 / RateWindowMs);
    }
}

public enum LibraryLayoutHangVerdict
{
    Ok,
    Stall,
    HotIdle
}
