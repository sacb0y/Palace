using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class PreviewPersistTests
{
    [Fact]
    public void ShouldWriteNotes_FalseWhileApplyingPreview()
    {
        Assert.False(PreviewPersist.ShouldWriteNotes(true, true, "a", "a"));
    }

    [Fact]
    public void ShouldWriteNotes_FalseWhenClearPreviewHasNoPreviewId()
    {
        Assert.False(PreviewPersist.ShouldWriteNotes(false, true, "a", null));
        Assert.False(PreviewPersist.ShouldWriteNotes(false, true, "a", ""));
    }

    [Fact]
    public void ShouldWriteNotes_FalseWhenOverlayPreviewIsAnotherAsset()
    {
        Assert.False(PreviewPersist.ShouldWriteNotes(false, true, "selected", "overlay"));
    }

    [Fact]
    public void ShouldWriteNotes_TrueForUserEditOfSelectedAsset()
    {
        Assert.True(PreviewPersist.ShouldWriteNotes(false, true, "a", "a"));
    }

    [Fact]
    public void ShouldWriteNotes_FalseWhenCannotEditOrNoSelection()
    {
        Assert.False(PreviewPersist.ShouldWriteNotes(false, false, "a", "a"));
        Assert.False(PreviewPersist.ShouldWriteNotes(false, true, null, "a"));
    }

    [Fact]
    public void ShouldWriteRating_AllowsMultiSelectWhenNotApplying()
    {
        Assert.True(PreviewPersist.ShouldWriteRating(false, true, 4));
        Assert.False(PreviewPersist.ShouldWriteRating(true, true, 4));
        Assert.False(PreviewPersist.ShouldWriteRating(false, false, 4));
        Assert.False(PreviewPersist.ShouldWriteRating(false, true, 0));
    }

    [Fact]
    public void PreviewTargetsMatchSelection_OrderIndependent()
    {
        Assert.True(PreviewPersist.PreviewTargetsMatchSelection(["a", "b"], ["b", "a"]));
        Assert.False(PreviewPersist.PreviewTargetsMatchSelection(["a", "b"], ["a"]));
        Assert.False(PreviewPersist.PreviewTargetsMatchSelection(["a"], ["a", "b"]));
        Assert.False(PreviewPersist.PreviewTargetsMatchSelection(["a", "b"], ["a", "c"]));
        Assert.True(PreviewPersist.PreviewTargetsMatchSelection([], []));
    }

    [Fact]
    public void ShouldEnableRatingEdit_FalseUntilPreviewMatchesSelection()
    {
        Assert.False(PreviewPersist.ShouldEnableRatingEdit(2, previewTargetsMatchSelection: false));
        Assert.False(PreviewPersist.ShouldEnableRatingEdit(0, previewTargetsMatchSelection: true));
        Assert.True(PreviewPersist.ShouldEnableRatingEdit(2, previewTargetsMatchSelection: true));
    }

    [Fact]
    public void ShouldApplyRatingChange_UsesSelectionSnapshotNotStalePreview()
    {
        // Selection changed to b,c while preview still holds a,b — must not write.
        Assert.False(PreviewPersist.ShouldApplyRatingChange(
            applyingPreview: false,
            canEditRating: true,
            selectionIdsAtChange: ["b", "c"],
            previewTargetIds: ["a", "b"]));

        // Even if CanEditRating was wrongly left on after a change, mismatch blocks write.
        Assert.False(PreviewPersist.ShouldApplyRatingChange(
            applyingPreview: false,
            canEditRating: true,
            selectionIdsAtChange: ["c"],
            previewTargetIds: ["a", "b"]));

        // Matched snapshot — only current selection is eligible.
        Assert.True(PreviewPersist.ShouldApplyRatingChange(
            applyingPreview: false,
            canEditRating: true,
            selectionIdsAtChange: ["b", "c"],
            previewTargetIds: ["c", "b"]));

        // CanEditRating stays false until LoadPreviewAsync finishes matching.
        Assert.False(PreviewPersist.ShouldApplyRatingChange(
            applyingPreview: false,
            canEditRating: false,
            selectionIdsAtChange: ["b", "c"],
            previewTargetIds: ["b", "c"]));
    }
}
