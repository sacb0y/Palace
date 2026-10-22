namespace Palace.Helpers;

/// <summary>
/// One Review-mode pass over a queue of asset ids. Keep / Reject / Skip are
/// decisions; Skip leaves the file on disk and is undoable. Reject stores the
/// move <c>BatchId</c> so Undo can reverse that batch (only when it is still
/// newest). End of queue offers "Review n skipped". WinUI-free for Palace.Tests.
/// </summary>
public enum ReviewDecision
{
    Keep = 1,
    Reject = 2,
    Skip = 3
}

public readonly record struct ReviewHistoryEntry(
    ReviewDecision Decision,
    string AssetId,
    string? MoveBatchId);

public sealed class ReviewSession
{
    private readonly List<string> _queue;
    private readonly List<ReviewHistoryEntry> _history = [];
    private readonly List<string> _skipped = [];
    private int _index;

    public ReviewSession(IEnumerable<string> assetIds)
    {
        _queue = assetIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _index = 0;
    }

    public IReadOnlyList<string> Queue => _queue;

    public IReadOnlyList<string> Skipped => _skipped;

    public int Count => _queue.Count;

    public int Index => _index;

    public int DecidedCount => _history.Count;

    public int RejectedCount => _history.Count(e => e.Decision == ReviewDecision.Reject);

    public int SkipCount => _skipped.Count;

    public bool IsActive => Count > 0 && !IsComplete;

    public bool IsComplete => Count == 0 || _index >= Count;

    public string? CurrentId =>
        IsComplete || Count == 0 ? null : _queue[_index];

    /// <summary>
    /// Progress line. Counts skips and, when done, reviewed total:
    /// <c>3 of 20 · 2 skipped</c>, or <c>Done · 5 reviewed · 2 skipped</c>.
    /// </summary>
    public string ProgressText
    {
        get
        {
            if (Count == 0)
            {
                return "No items to review";
            }

            if (IsComplete)
            {
                return SkipCount == 0
                    ? $"Done · {DecidedCount} reviewed"
                    : $"Done · {DecidedCount} reviewed · {SkipCount} skipped";
            }

            var position = _index + 1;
            return SkipCount == 0
                ? $"{position} of {Count}"
                : $"{position} of {Count} · {SkipCount} skipped";
        }
    }

    /// <summary>End-of-queue CTA label. Empty when there are no skips to re-review.</summary>
    public string ReviewSkippedLabel =>
        IsComplete && SkipCount > 0 ? $"Review {SkipCount} skipped" : "";

    public bool CanReviewSkipped => IsComplete && SkipCount > 0;

    public bool CanUndo => _history.Count > 0;

    public bool CanDecide => IsActive && CurrentId is not null;

    /// <summary>
    /// Record a decision and advance. For Reject, pass the move journal
    /// <paramref name="moveBatchId"/> so Undo can reverse that batch by id.
    /// </summary>
    public bool TryDecide(ReviewDecision decision, string? moveBatchId = null)
    {
        if (!CanDecide || CurrentId is null)
        {
            return false;
        }

        var id = CurrentId;
        _history.Add(new ReviewHistoryEntry(
            decision,
            id,
            decision == ReviewDecision.Reject ? moveBatchId : null));
        if (decision == ReviewDecision.Skip)
        {
            _skipped.Add(id);
        }

        _index++;
        return true;
    }

    /// <summary>
    /// Peek the last decision without rolling the session back. Host undoes
    /// Reject on disk first, then <see cref="TryCommitUndo"/>.
    /// </summary>
    public bool TryPeekUndo(out ReviewHistoryEntry entry)
    {
        entry = default;
        if (!CanUndo)
        {
            return false;
        }

        entry = _history[^1];
        return true;
    }

    /// <summary>
    /// Roll back the last decision after a successful disk undo (or Keep/Skip).
    /// Call only after Reject's batch undo succeeded — never on disk failure.
    /// </summary>
    public bool TryCommitUndo()
    {
        if (!CanUndo)
        {
            return false;
        }

        var entry = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        _index = Math.Max(0, _index - 1);
        if (entry.Decision == ReviewDecision.Skip)
        {
            var skipAt = _skipped.LastIndexOf(entry.AssetId);
            if (skipAt >= 0)
            {
                _skipped.RemoveAt(skipAt);
            }
        }

        return true;
    }

    /// <summary>
    /// Start a new session over the skipped ids only. Empty when nothing was skipped.
    /// </summary>
    public ReviewSession? StartSkippedPass()
    {
        if (_skipped.Count == 0)
        {
            return null;
        }

        return new ReviewSession(_skipped.ToList());
    }

    public static bool TryToDecision(TouchGestures.SwipeAction action, out ReviewDecision decision)
    {
        switch (action)
        {
            case TouchGestures.SwipeAction.Keep:
                decision = ReviewDecision.Keep;
                return true;
            case TouchGestures.SwipeAction.Reject:
                decision = ReviewDecision.Reject;
                return true;
            case TouchGestures.SwipeAction.Skip:
                decision = ReviewDecision.Skip;
                return true;
            default:
                decision = default;
                return false;
        }
    }
}
