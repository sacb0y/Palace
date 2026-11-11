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
    public void UnionFileCount_DedupesOverlappingAssets()
    {
        // Two tags on 8 different files each → 16; overlapping 2 → 14.
        Assert.Equal(
            16,
            TagRemoveConfirm.UnionFileCount(
            [
                ["a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8"],
                ["b1", "b2", "b3", "b4", "b5", "b6", "b7", "b8"]
            ]));
        Assert.Equal(
            14,
            TagRemoveConfirm.UnionFileCount(
            [
                ["a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8"],
                ["a7", "a8", "b1", "b2", "b3", "b4", "b5", "b6"]
            ]));
        Assert.True(TagRemoveConfirm.NeedsConfirm(16));
        Assert.False(TagRemoveConfirm.NeedsConfirm(8)); // max-of-one would wrongly skip confirm
    }

    [Fact]
    public void SelectedBarLabel_AndDelBadge()
    {
        Assert.Equal("2 tags · 10 files", TagRemoveConfirm.SelectedBarLabel(2, 10));
        Assert.Equal("1 tag · 1 file", TagRemoveConfirm.SelectedBarLabel(1, 1));
        Assert.Equal("Remove from all", TagRemoveConfirm.RemoveFromAllLabel);
        Assert.Equal("Del", TagRemoveConfirm.DelBadge);
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
