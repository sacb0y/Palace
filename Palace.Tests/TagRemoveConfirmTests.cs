using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagRemoveConfirmTests
{
    [Fact]
    public void NeedsConfirm_AboveTen_True()
    {
        Assert.False(TagRemoveConfirm.NeedsConfirm(10));
        Assert.False(TagRemoveConfirm.NeedsConfirm(1));
        Assert.True(TagRemoveConfirm.NeedsConfirm(11));
        Assert.True(TagRemoveConfirm.NeedsConfirm(18));
    }

    [Fact]
    public void ChipFlyoutTitle_UsesFileCount() =>
        Assert.Equal("Remove wip from 18 files?", TagRemoveConfirm.ChipFlyoutTitle("wip", 18));

    [Fact]
    public void AffectedFileCount_PartialUsesK()
    {
        Assert.Equal(18, TagRemoveConfirm.AffectedFileCount(isPartial: true, partialCount: 18, selectionTotal: 24));
        Assert.Equal(24, TagRemoveConfirm.AffectedFileCount(isPartial: false, partialCount: 0, selectionTotal: 24));
    }

    [Fact]
    public void RemoveButtonLabel_Pluralizes()
    {
        Assert.Equal("Remove 1 tag", TagRemoveConfirm.RemoveButtonLabel(1));
        Assert.Equal("Remove 3 tags", TagRemoveConfirm.RemoveButtonLabel(3));
    }

    [Fact]
    public void AssignmentSummary_OneUndo()
    {
        Assert.Equal("28 assignments · one Undo", TagRemoveConfirm.AssignmentSummary(28));
        Assert.Equal("1 assignment · one Undo", TagRemoveConfirm.AssignmentSummary(1));
    }
}
