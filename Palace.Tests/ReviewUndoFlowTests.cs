using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Review Undo with a fake disk undo (stand-in for FileOpService.UndoBatchAsync).
/// </summary>
public sealed class ReviewUndoFlowTests
{
    [Fact]
    public async Task Undo_AfterUnrelatedMove_RefusesAndKeepsSession()
    {
        var session = new ReviewSession(["a", "b"]);
        Assert.True(session.TryDecide(ReviewDecision.Reject, moveBatchId: "reject-batch"));
        Assert.Equal("b", session.CurrentId);
        Assert.Equal(1, session.RejectedCount);

        var (rolledBack, _, message) = await ReviewUndoFlow.TryUndoAsync(
            session,
            batchId =>
            {
                Assert.Equal("reject-batch", batchId);
                // Newest journal entry is an unrelated move — UndoBatch refuses.
                return Task.FromResult(new ReviewUndoFlow.DiskUndoResult(
                    Ok: false,
                    Message: "Can't undo this reject — another file operation happened after it."));
            });

        Assert.False(rolledBack);
        Assert.Contains("another file operation", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("b", session.CurrentId);
        Assert.Equal(1, session.RejectedCount);
        Assert.True(session.TryPeekUndo(out var still));
        Assert.Equal("reject-batch", still.MoveBatchId);
    }

    [Fact]
    public async Task FailedDiskUndo_DoesNotRollBackSession()
    {
        var session = new ReviewSession(["a", "b", "c"]);
        Assert.True(session.TryDecide(ReviewDecision.Keep));
        Assert.True(session.TryDecide(ReviewDecision.Reject, moveBatchId: "batch-b"));
        Assert.Equal("c", session.CurrentId);

        var (rolledBack, _, message) = await ReviewUndoFlow.TryUndoAsync(
            session,
            _ => Task.FromResult(new ReviewUndoFlow.DiskUndoResult(
                Ok: false,
                Message: "Couldn't undo move. File is locked. Try again.")));

        Assert.False(rolledBack);
        Assert.Contains("locked", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("c", session.CurrentId);
        Assert.Equal(2, session.DecidedCount);
        Assert.Equal(1, session.RejectedCount);
    }

    [Fact]
    public async Task SuccessfulRejectUndo_RollsBackAfterDiskOk()
    {
        var session = new ReviewSession(["a", "b"]);
        Assert.True(session.TryDecide(ReviewDecision.Reject, moveBatchId: "batch-a"));
        Assert.Equal("b", session.CurrentId);

        string? undone = null;
        var (rolledBack, entry, message) = await ReviewUndoFlow.TryUndoAsync(
            session,
            batchId =>
            {
                undone = batchId;
                return Task.FromResult(new ReviewUndoFlow.DiskUndoResult(Ok: true, Message: "Undid move."));
            });

        Assert.True(rolledBack);
        Assert.Null(message);
        Assert.Equal("batch-a", undone);
        Assert.Equal(ReviewDecision.Reject, entry?.Decision);
        Assert.Equal("a", session.CurrentId);
        Assert.Equal(0, session.RejectedCount);
    }

    [Fact]
    public async Task SkipUndo_DoesNotCallDisk()
    {
        var session = new ReviewSession(["a", "b"]);
        Assert.True(session.TryDecide(ReviewDecision.Skip));
        var diskCalls = 0;
        var (rolledBack, entry, _) = await ReviewUndoFlow.TryUndoAsync(
            session,
            _ =>
            {
                diskCalls++;
                return Task.FromResult(new ReviewUndoFlow.DiskUndoResult(Ok: true, Message: ""));
            });

        Assert.True(rolledBack);
        Assert.Equal(0, diskCalls);
        Assert.Equal(ReviewDecision.Skip, entry?.Decision);
        Assert.Equal("a", session.CurrentId);
    }
}
