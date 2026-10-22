using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class BatchRejectTests
{
    [Theory]
    [InlineData(1, 0, 0, "Rejected 1 file.")]
    [InlineData(12, 0, 0, "Rejected 12 files.")]
    [InlineData(0, 3, 0, "All 3 already in _rejected.")]
    [InlineData(0, 1, 0, "Already in _rejected.")]
    [InlineData(12, 0, 3, "Rejected 12 of 15; 3 failed.")]
    [InlineData(12, 3, 0, "Rejected 12 of 15; 3 already in _rejected.")]
    [InlineData(10, 2, 3, "Rejected 10 of 15; 2 already in _rejected; 3 failed.")]
    [InlineData(0, 0, 0, "Nothing rejected.")]
    [InlineData(0, 0, 2, "Rejected 0 of 2; 2 failed.")]
    public void ToastMessage_AggregatesPartialFailures(
        int moved,
        int already,
        int failed,
        string expected) =>
        Assert.Equal(expected, BatchReject.ToastMessage(moved, already, failed));

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
}
