namespace Palace.Helpers;

/// <summary>
/// Per-asset zoom/pan snapshot for swipe-nav: commit resets to Fit; going back
/// restores the prior zoom/pan (docs/specs/swipe-nav.md). Off WinUI.
/// </summary>
public sealed class SwipeZoomRestore
{
    public readonly record struct Snapshot(
        ImageScaling Scaling,
        double PinchZoom,
        double OffsetX,
        double OffsetY);

    private readonly Dictionary<string, Snapshot> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public void Save(string? path, Snapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _byPath[path] = snapshot;
    }

    public bool TryRestore(string? path, out Snapshot snapshot)
    {
        snapshot = default;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return _byPath.TryGetValue(path, out snapshot);
    }

    public void Forget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _byPath.Remove(path);
    }

    public void Clear() => _byPath.Clear();

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
