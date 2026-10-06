namespace Palace.Helpers;

/// <summary>Drops repeats of the same failure inside a time window.</summary>
internal sealed class ErrorThrottle
{
    private const int MaxKeys = 64;

    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _last = new(StringComparer.Ordinal);
    private readonly TimeSpan _window;
    private readonly Func<DateTimeOffset> _clock;

    public ErrorThrottle(TimeSpan window, Func<DateTimeOffset>? clock = null)
    {
        _window = window;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public static string KeyFor(Exception exception)
    {
        var firstFrame = new System.Diagnostics.StackTrace(exception, false).GetFrame(0)?.GetMethod();
        return $"{exception.GetType().FullName}|{exception.Message}|{firstFrame?.DeclaringType?.FullName}.{firstFrame?.Name}";
    }

    public bool ShouldReport(string key)
    {
        var now = _clock();
        lock (_gate)
        {
            if (_last.TryGetValue(key, out var previous) && now - previous < _window)
            {
                return false;
            }

            if (_last.Count >= MaxKeys)
            {
                _last.Clear();
            }

            _last[key] = now;
            return true;
        }
    }
}
