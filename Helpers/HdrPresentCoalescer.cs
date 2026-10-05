namespace Palace.Helpers;

/// <summary>
/// Collapses rapid re-present requests (resize / scale / peak) into one
/// background loop. UI-thread only — no locking. <see cref="Request"/> says
/// whether the caller must start the loop; the loop calls <see cref="TryTake"/>
/// until it returns false (which also stops the loop).
/// </summary>
internal sealed class HdrPresentCoalescer
{
    /// <summary>First paint and discrete user actions (scale mode, new still).</summary>
    public const int ImmediateMs = 0;

    /// <summary>Quiet time after the last resize / peak tick before rasterizing.</summary>
    public const int SettleMs = 75;

    /// <summary>A drag that never goes quiet still presents at least this often.</summary>
    public const int MaxDebounceMs = 250;

    private bool _running;
    private bool _dirty;
    private int _delayMs;

    public bool IsRunning => _running;

    /// <summary>A newer request arrived since the last <see cref="TryTake"/>.</summary>
    public bool Superseded => _dirty;

    /// <returns>True when the caller must start the loop.</returns>
    public bool Request(int delayMs)
    {
        delayMs = Math.Max(0, delayMs);
        _delayMs = _dirty ? Math.Min(_delayMs, delayMs) : delayMs;
        _dirty = true;
        if (_running)
        {
            return false;
        }

        _running = true;
        return true;
    }

    public bool TryTake(out int delayMs)
    {
        if (!_dirty)
        {
            _running = false;
            delayMs = 0;
            return false;
        }

        _dirty = false;
        delayMs = _delayMs;
        _delayMs = 0;
        return true;
    }

    /// <summary>
    /// After waiting <paramref name="waitedMs"/> of debounce, should the loop
    /// go back for the newer request instead of presenting now?
    /// </summary>
    public bool ShouldRestartDebounce(int waitedMs) =>
        _dirty && waitedMs < MaxDebounceMs;

    public void Abort()
    {
        _dirty = false;
        _delayMs = 0;
        _running = false;
    }
}
