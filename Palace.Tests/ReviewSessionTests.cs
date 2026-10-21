using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ReviewSessionTests
{
    [Fact]
    public void Progress_CountsSkips()
    {
        var session = new ReviewSession(["a", "b", "c"]);
        Assert.Equal("1 of 3", session.ProgressText);
        Assert.True(session.TryDecide(ReviewDecision.Skip));
        Assert.Equal("2 of 3 · 1 skipped", session.ProgressText);
        Assert.True(session.TryDecide(ReviewDecision.Keep));
        Assert.Equal("3 of 3 · 1 skipped", session.ProgressText);
        Assert.True(session.TryDecide(ReviewDecision.Skip));
        Assert.True(session.IsComplete);
        Assert.Equal("Done · 2 skipped", session.ProgressText);
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
        Assert.True(session.TryUndo(out var decision, out var id));
        Assert.Equal(ReviewDecision.Skip, decision);
        Assert.Equal("a", id);
        Assert.Equal(0, session.SkipCount);
        Assert.Equal("a", session.CurrentId);
        Assert.Equal("1 of 2", session.ProgressText);
    }

    [Fact]
    public void Undo_KeepAndReject()
    {
        var session = new ReviewSession(["a", "b", "c"]);
        Assert.True(session.TryDecide(ReviewDecision.Keep));
        Assert.True(session.TryDecide(ReviewDecision.Reject));
        Assert.Equal("c", session.CurrentId);
        Assert.True(session.TryUndo(out var reject, out var rejectId));
        Assert.Equal(ReviewDecision.Reject, reject);
        Assert.Equal("b", rejectId);
        Assert.True(session.TryUndo(out var keep, out var keepId));
        Assert.Equal(ReviewDecision.Keep, keep);
        Assert.Equal("a", keepId);
        Assert.False(session.TryUndo(out _, out _));
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
    public void ToDecision_MapsSwipeActions()
    {
        Assert.Equal(ReviewDecision.Keep, ReviewSession.ToDecision(TouchGestures.SwipeAction.Keep));
        Assert.Equal(ReviewDecision.Reject, ReviewSession.ToDecision(TouchGestures.SwipeAction.Reject));
        Assert.Equal(ReviewDecision.Skip, ReviewSession.ToDecision(TouchGestures.SwipeAction.Skip));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReviewSession.ToDecision(TouchGestures.SwipeAction.None));
        Assert.True(ReviewSession.TryToDecision(TouchGestures.SwipeAction.Keep, out var keep));
        Assert.Equal(ReviewDecision.Keep, keep);
        Assert.False(ReviewSession.TryToDecision(TouchGestures.SwipeAction.SpringBack, out _));
    }
}
