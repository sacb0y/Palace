namespace Palace.Helpers;

/// <summary>
/// Per-asset zoom/pan snapshot for swipe-nav. When KeepZoomBetweenImages is
/// off, commit clears local zoom/pan; going back restores a prior snapshot
/// (docs/specs/swipe-nav.md). LRU capped. Off WinUI.
/// </summary>
public sealed class SwipeZoomRestore
{
    public const int MaxEntries = 50;

    public readonly record struct Snapshot(
        ImageScaling Scaling,
        double PinchZoom,
        double OffsetX,
        double OffsetY);

    private readonly Dictionary<string, Snapshot> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _order = new();

    public void Save(string? path, Snapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_byPath.ContainsKey(path))
        {
            _order.Remove(path);
        }

        _byPath[path] = snapshot;
        _order.AddFirst(path);
        while (_order.Count > MaxEntries)
        {
            var last = _order.Last!.Value;
            _order.RemoveLast();
            _byPath.Remove(last);
        }
    }

    public bool TryRestore(string? path, out Snapshot snapshot)
    {
        snapshot = default;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!_byPath.TryGetValue(path, out snapshot))
        {
            return false;
        }

        // Touch as most-recent.
        _order.Remove(path);
        _order.AddFirst(path);
        return true;
    }

    public void Forget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_byPath.Remove(path))
        {
            _order.Remove(path);
        }
    }

    public void Clear()
    {
        _byPath.Clear();
        _order.Clear();
    }

    public int Count => _byPath.Count;

    /// <summary>
    /// Fit/1.0/0/0 is the post-commit default — only stash when the user had
    /// zoomed or panned away from that.
    /// </summary>
    public static bool IsWorthSaving(Snapshot snapshot) =>
        snapshot.Scaling != ImageScaling.Fit
        || snapshot.PinchZoom > 1.0001
        || Math.Abs(snapshot.OffsetX) > 0.5
        || Math.Abs(snapshot.OffsetY) > 0.5;
}
