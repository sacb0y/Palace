using System.Reflection;

namespace Palace.Helpers;

/// <summary>
/// Shared error policy: cancellation is not an error; everything else is logged and surfaced
/// without crashing. No WinUI types — the app wires the log and a global presenter at startup.
/// </summary>
internal static class ErrorReporter
{
    private const int MaxMessageLength = 200;

    private static readonly object Gate = new();
    private static ErrorLog? _log;
    private static Action<string>? _presenter;
    private static ErrorThrottle _throttle = new(TimeSpan.FromSeconds(5));

    public static void Configure(ErrorLog? log, Action<string>? presenter = null, ErrorThrottle? throttle = null)
    {
        lock (Gate)
        {
            _log = log;
            _presenter = presenter;
            _throttle = throttle ?? new ErrorThrottle(TimeSpan.FromSeconds(5));
        }
    }

    public static bool IsCancellation(Exception exception) => exception switch
    {
        OperationCanceledException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Count > 0
            && aggregate.InnerExceptions.All(IsCancellation),
        _ => false
    };

    /// <summary>State may be corrupt; do not swallow these.</summary>
    public static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or AccessViolationException or AppDomainUnloadedException;

    public static string UserMessage(string context, Exception exception) =>
        $"{context} failed: {Detail(exception)}";

    public static string UnhandledMessage(Exception exception) =>
        $"Palace hit an unexpected error and kept running: {Detail(exception)}";

    private static string Detail(Exception exception)
    {
        var inner = Unwrap(exception);
        var text = inner.Message?.Trim() ?? "";
        var newline = text.IndexOfAny(['\r', '\n']);
        if (newline >= 0)
        {
            text = text[..newline].TrimEnd();
        }

        if (text.Length == 0)
        {
            return inner.GetType().Name;
        }

        return text.Length > MaxMessageLength ? text[..(MaxMessageLength - 1)] + "\u2026" : text;
    }

    /// <summary>
    /// Logs and notifies. Returns false (and does nothing) for cancellation. Never throws.
    /// When <paramref name="notify"/> is null the global presenter is used.
    /// </summary>
    public static bool Report(string context, Exception exception, Action<string>? notify = null)
    {
        if (IsCancellation(exception))
        {
            return false;
        }

        TryLog(context, exception);
        Present(UserMessage(context, exception), notify);
        return true;
    }

    /// <summary>
    /// For process-level handlers (<c>Application.UnhandledException</c> etc.). Repeats of the same
    /// failure inside the throttle window are dropped so a faulting layout pass cannot flood the log.
    /// </summary>
    public static void ReportUnhandled(string source, Exception exception, bool notifyUser = true)
    {
        if (IsCancellation(exception))
        {
            return;
        }

        ErrorThrottle throttle;
        lock (Gate)
        {
            throttle = _throttle;
        }

        if (!throttle.ShouldReport(ErrorThrottle.KeyFor(exception)))
        {
            return;
        }

        TryLog(source, exception);
        if (notifyUser)
        {
            Present(UnhandledMessage(exception), null);
        }
    }

    public static void LogOnly(string source, Exception exception) => TryLog(source, exception);

    public static async Task RunAsync(string context, Action<string>? notify, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            Report(context, ex, notify);
        }
    }

    public static void Run(string context, Action<string>? notify, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (!IsFatal(ex))
        {
            Report(context, ex, notify);
        }
    }

    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (true)
        {
            switch (current)
            {
                case AggregateException { InnerExceptions.Count: > 0 } aggregate:
                    current = aggregate.InnerExceptions[0];
                    break;
                case TargetInvocationException { InnerException: { } inner }:
                    current = inner;
                    break;
                default:
                    return current;
            }
        }
    }

    private static void TryLog(string source, Exception exception)
    {
        ErrorLog? log;
        lock (Gate)
        {
            log = _log;
        }

        log?.TryAppend(source, exception);
    }

    private static void Present(string message, Action<string>? notify)
    {
        Action<string>? target;
        lock (Gate)
        {
            target = notify ?? _presenter;
        }

        try
        {
            target?.Invoke(message);
        }
        catch
        {
            // The reporter must never be the thing that crashes.
        }
    }
}
