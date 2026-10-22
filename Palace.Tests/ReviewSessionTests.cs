using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ReviewSessionTests
{
    [Fact]
    public void Progress_CountsSkipsAndReviewed()
    {
        var session = new ReviewSession(["a", "b", "c"]);
        Assert.Equal("1 of 3", session.ProgressText);
        Assert.True(session.TryDecide(ReviewDecision.Skip));
        Assert.Equal("2 of 3 · 1 skipped", session.ProgressText);
        Assert.True(session.TryDecide(ReviewDecision.Keep));
        Assert.Equal("3 of 3 · 1 skipped", session.ProgressText);
        Assert.True(session.TryDecide(ReviewDecision.Skip));
        Assert.True(session.IsComplete);
        Assert.Equal("Done · 3 reviewed · 2 skipped", session.ProgressText);
        Assert.Equal("Review 2 skipped", session.ReviewSkippedLabel);
        Assert.True(session.CanReviewSkipped);
    }

    [Fact]
    public void Undo_CoversSkips()
    {
        var session = new ReviewSession(["a", "b"]);
        Assert.True(session.TryDecide(ReviewDecision.Skip));
        Assert.Equal(1, session.SkipCount);
        Assert.Equal("b", session.CurrentId);
        Assert.True(session.TryPeekUndo(out var peek));
        Assert.Equal(ReviewDecision.Skip, peek.Decision);
        Assert.Equal("a", peek.AssetId);
        Assert.True(session.TryCommitUndo());
        Assert.Equal(0, session.SkipCount);
        Assert.Equal("a", session.CurrentId);
        Assert.Equal("1 of 2", session.ProgressText);
    }

    [Fact]
    public void Undo_KeepAndReject_StoresBatchId()
    {
        var session = new ReviewSession(["a", "b", "c"]);
        Assert.True(session.TryDecide(ReviewDecision.Keep));
        Assert.True(session.TryDecide(ReviewDecision.Reject, moveBatchId: "batch-b"));
        Assert.Equal("c", session.CurrentId);
        Assert.Equal(1, session.RejectedCount);
        Assert.True(session.TryPeekUndo(out var reject));
        Assert.Equal(ReviewDecision.Reject, reject.Decision);
        Assert.Equal("b", reject.AssetId);
        Assert.Equal("batch-b", reject.MoveBatchId);
        Assert.True(session.TryCommitUndo());
        Assert.True(session.TryPeekUndo(out var keep));
        Assert.Equal(ReviewDecision.Keep, keep.Decision);
        Assert.Equal("a", keep.AssetId);
        Assert.Null(keep.MoveBatchId);
        Assert.True(session.TryCommitUndo());
        Assert.False(session.TryPeekUndo(out _));
    }

    [Fact]
    public void StartSkippedPass_RequeuesSkipsOnly()
    {
        var session = new ReviewSession(["a", "b", "c"]);
        Assert.True(session.TryDecide(ReviewDecision.Skip));
        Assert.True(session.TryDecide(ReviewDecision.Keep));
        Assert.True(session.TryDecide(ReviewDecision.Skip));
        var again = session.StartSkippedPass();
        Assert.NotNull(again);
        Assert.Equal(2, again!.Count);
        Assert.Equal("a", again.CurrentId);
        Assert.True(again.TryDecide(ReviewDecision.Keep));
        Assert.Equal("c", again.CurrentId);
    }

    [Fact]
    public void EmptyQueue_ReportsNoItems()
    {
        var session = new ReviewSession([]);
        Assert.False(session.IsActive);
        Assert.True(session.IsComplete);
        Assert.Equal("No items to review", session.ProgressText);
        Assert.False(session.TryDecide(ReviewDecision.Keep));
        Assert.Null(session.StartSkippedPass());
    }

    [Fact]
    public void DedupesIds()
    {
        var session = new ReviewSession(["a", "a", "", "b"]);
        Assert.Equal(2, session.Count);
        Assert.Equal("a", session.CurrentId);
    }

    [Fact]
    public void TryToDecision_MapsSwipeActions()
    {
        Assert.True(ReviewSession.TryToDecision(TouchGestures.SwipeAction.Keep, out var keep));
        Assert.Equal(ReviewDecision.Keep, keep);
        Assert.True(ReviewSession.TryToDecision(TouchGestures.SwipeAction.Reject, out var reject));
        Assert.Equal(ReviewDecision.Reject, reject);
        Assert.True(ReviewSession.TryToDecision(TouchGestures.SwipeAction.Skip, out var skip));
        Assert.Equal(ReviewDecision.Skip, skip);
        Assert.False(ReviewSession.TryToDecision(TouchGestures.SwipeAction.SpringBack, out _));
        Assert.False(ReviewSession.TryToDecision(TouchGestures.SwipeAction.None, out _));
    }
}
