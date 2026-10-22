using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class BatchRejectTests
{
    [Theory]
    [InlineData(1, 0, 0, 1, "Rejected 1 file.")]
    [InlineData(12, 0, 0, 1, "Rejected 12 files.")]
    [InlineData(12, 0, 0, 3, "Rejected 12 files across 3 folders.")]
    [InlineData(0, 3, 0, 1, "All 3 already in _rejected.")]
    [InlineData(0, 1, 0, 1, "Already in _rejected.")]
    [InlineData(12, 0, 3, 1, "Rejected 12 of 15; 3 failed.")]
    [InlineData(12, 3, 0, 1, "Rejected 12 of 15; 3 already in _rejected.")]
    [InlineData(10, 2, 3, 2, "Rejected 10 of 15; 2 already in _rejected; 3 failed.")]
    [InlineData(0, 0, 0, 1, "Nothing rejected.")]
    [InlineData(0, 0, 2, 1, "Rejected 0 of 2; 2 failed.")]
    public void ToastMessage_AggregatesPartialFailures(
        int moved,
        int already,
        int failed,
        int folders,
        string expected) =>
        Assert.Equal(expected, BatchReject.ToastMessage(moved, already, failed, folders));

    [Theory]
    [InlineData(1, 1, "Reject 1 file")]
    [InlineData(12, 1, "Reject 12 files")]
    [InlineData(12, 3, "Reject 12 files across 3 folders")]
    public void JournalSummary_AcrossFolders(int files, int folders, string expected) =>
        Assert.Equal(expected, BatchReject.JournalSummary(files, folders));

    [Fact]
    public void UndoToastSeconds_LongerForMultiFolder()
    {
        Assert.Equal(BatchReject.SingleFolderUndoSeconds, BatchReject.UndoToastSeconds(1));
        Assert.Equal(BatchReject.MultiFolderUndoSeconds, BatchReject.UndoToastSeconds(2));
        Assert.Equal(BatchReject.MultiFolderUndoSeconds, BatchReject.UndoToastSeconds(5));
    }

    [Fact]
    public void ConfirmBody_IncludesCollapsedSummary()
    {
        Assert.Contains("42 selected · 30 in collapsed sections", BatchReject.ConfirmBody(42, 30));
        Assert.DoesNotContain("collapsed", BatchReject.ConfirmBody(5, 0));
    }

    [Fact]
    public void NewerBatchesAllowAbsorbPrior_OnlyDestCreateFolder()
    {
        var dest = Path.Combine("Library", "shots", "_rejected");
        var ok = new FileOpBatch
        {
            Id = "c1",
            Kind = FileOpKind.CreateFolder,
            Items =
            [
                new FileOpItem { Step = FileOpStep.CreateDir, NewPath = dest }
            ]
        };
        Assert.True(BatchReject.NewerBatchesAllowAbsorbPrior([ok], dest));

        var wrongKind = new FileOpBatch
        {
            Id = "m1",
            Kind = FileOpKind.Move,
            Items =
            [
                new FileOpItem { Step = FileOpStep.MoveFile, NewPath = Path.Combine(dest, "a.png") }
            ]
        };
        Assert.False(BatchReject.NewerBatchesAllowAbsorbPrior([wrongKind], dest));

        var otherDest = new FileOpBatch
        {
            Id = "c2",
            Kind = FileOpKind.CreateFolder,
            Items =
            [
                new FileOpItem
                {
                    Step = FileOpStep.CreateDir,
                    NewPath = Path.Combine("Library", "refs", "_rejected")
                }
            ]
        };
        Assert.False(BatchReject.NewerBatchesAllowAbsorbPrior([otherDest], dest));
    }

    [Fact]
    public void NewerThanPrior_TakesUntilPriorId()
    {
        var a = new FileOpBatch { Id = "a" };
        var b = new FileOpBatch { Id = "b" };
        var c = new FileOpBatch { Id = "c" };
        // newest-first: a, b, c(prior)
        var newer = BatchReject.NewerThanPrior([a, b, c], "c");
        Assert.Equal(["a", "b"], newer.Select(x => x.Id));
        Assert.Empty(BatchReject.NewerThanPrior([a, b], "missing"));
    }

    [Fact]
    public void InterruptedMessage_ReportsMovedSoFar()
    {
        Assert.Equal("Interrupted.", BatchReject.InterruptedMessage(0));
        Assert.Equal("Interrupted, 1 rejected so far.", BatchReject.InterruptedMessage(1));
        Assert.Equal("Interrupted, 3 rejected so far.", BatchReject.InterruptedMessage(3));
    }

    [Fact]
    public void CanAbsorbPrior_FalseWhenInterveningMoveOnTip()
    {
        var dest = Path.Combine("Library", "refs", "_rejected");
        var prior = new FileOpBatch
        {
            Id = "A",
            Kind = FileOpKind.Move,
            Items =
            [
                new FileOpItem
                {
                    Step = FileOpStep.MoveFile,
                    OldPath = Path.Combine("Library", "shots", "a.png"),
                    NewPath = Path.Combine("Library", "shots", "_rejected", "a.png")
                }
            ]
        };
        var intervening = new FileOpBatch
        {
            Id = "X",
            Kind = FileOpKind.Move,
            Items =
            [
                new FileOpItem
                {
                    Step = FileOpStep.MoveFile,
                    OldPath = Path.Combine("Library", "extra.png"),
                    NewPath = Path.Combine("Library", "Other", "extra.png")
                }
            ]
        };
        // newest-first: X, A
        Assert.False(BatchReject.CanAbsorbPrior([intervening, prior], "A", dest));
        Assert.True(BatchReject.CanAbsorbPrior([prior], "A", dest));
    }

    [Fact]
    public void TrackUndoBatchId_RemovesOnlyAbsorbedPrior()
    {
        var ids = new List<string> { "older", "chain" };
        BatchReject.TrackUndoBatchId(ids, chainBatchId: "chain", newBatchId: "tip", absorbedPriorBatch: true);
        Assert.Equal(["older", "tip"], ids);

        var single = new List<string> { "chain" };
        BatchReject.TrackUndoBatchId(single, "chain", "tip", absorbedPriorBatch: true);
        Assert.Equal(["tip"], single);

        var noAbsorb = new List<string> { "a" };
        BatchReject.TrackUndoBatchId(noAbsorb, "a", "b", absorbedPriorBatch: false);
        Assert.Equal(["a", "b"], noAbsorb);
    }

    [Fact]
    public void PendingMultiUndoStillTip_RequiresTipMatch()
    {
        Assert.False(BatchReject.PendingMultiUndoStillTip(null, "tip"));
        Assert.False(BatchReject.PendingMultiUndoStillTip(["only"], "only"));
        Assert.True(BatchReject.PendingMultiUndoStillTip(["a", "b"], "b"));
        Assert.False(BatchReject.PendingMultiUndoStillTip(["a", "b"], "x"));
        Assert.False(BatchReject.PendingMultiUndoStillTip(["a", "b"], null));
    }

    [Fact]
    public void CountDestinationFolders_UsesReviewRejectDestAndSamePath()
    {
        var shotsReject = Path.Combine("Library", "shots", "_rejected");
        var refsReject = Path.Combine("Library", "refs", "_rejected");
        var items = new[]
        {
            new FileOpItem
            {
                Step = FileOpStep.MoveFile,
                OldPath = Path.Combine("Library", "shots", "a.png"),
                NewPath = Path.Combine(shotsReject, "a.png")
            },
            new FileOpItem
            {
                Step = FileOpStep.MoveFile,
                OldPath = Path.Combine("Library", "shots", "b.png"),
                // Trailing separator on parent must not double-count.
                NewPath = Path.Combine(shotsReject + Path.DirectorySeparatorChar, "b.png")
            },
            new FileOpItem
            {
                Step = FileOpStep.CreateDir,
                NewPath = shotsReject
            },
            new FileOpItem
            {
                Step = FileOpStep.MoveFile,
                OldPath = Path.Combine("Library", "refs", "c.png"),
                NewPath = Path.Combine(refsReject, "c.png")
            }
        };
        Assert.Equal(2, BatchReject.CountDestinationFolders(items));
    }
}
