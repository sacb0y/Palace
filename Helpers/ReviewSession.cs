namespace Palace.Helpers;

/// <summary>
/// One Review-mode pass over a queue of asset ids. Keep / Reject / Skip are
/// decisions; Skip leaves the file on disk and is undoable. End of queue offers
/// "Review n skipped". WinUI-free for Palace.Tests.
/// </summary>
public enum ReviewDecision
{
    Keep = 1,
    Reject = 2,
    Skip = 3
}

public sealed class ReviewSession
{
    private readonly List<string> _queue;
    private readonly List<ReviewDecision> _history = [];
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

    public int SkipCount => _skipped.Count;

    public bool IsActive => Count > 0 && !IsComplete;

    public bool IsComplete => Count == 0 || _index >= Count;

    public string? CurrentId =>
        IsComplete || Count == 0 ? null : _queue[_index];

    /// <summary>
    /// Progress line. Counts skips: <c>3 of 20 · 2 skipped</c>, or
    /// <c>Done · 2 skipped</c> at end of queue.
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
                    ? $"Done · {Count} reviewed"
                    : $"Done · {SkipCount} skipped";
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

    /// <summary>Record a decision and advance. Returns false when the session cannot decide.</summary>
    public bool TryDecide(ReviewDecision decision)
    {
        if (!CanDecide || CurrentId is null)
        {
            return false;
        }

        var id = CurrentId;
        _history.Add(decision);
        if (decision == ReviewDecision.Skip)
        {
            _skipped.Add(id);
        }

        _index++;
        return true;
    }

    /// <summary>
    /// Undo the last decision. Returns the undone decision + asset id so the
    /// host can reverse a Reject move. Skip undoes without touching disk.
    /// </summary>
    public bool TryUndo(out ReviewDecision decision, out string assetId)
    {
        decision = default;
        assetId = "";
        if (!CanUndo)
        {
            return false;
        }

        decision = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        _index = Math.Max(0, _index - 1);
        assetId = _queue[_index];
        if (decision == ReviewDecision.Skip)
        {
            var skipAt = _skipped.LastIndexOf(assetId);
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

    public static ReviewDecision ToDecision(TouchGestures.SwipeAction action) => action switch
    {
        TouchGestures.SwipeAction.Keep => ReviewDecision.Keep,
        TouchGestures.SwipeAction.Reject => ReviewDecision.Reject,
        TouchGestures.SwipeAction.Skip => ReviewDecision.Skip,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Not a Review decision.")
    };

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
