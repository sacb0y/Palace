using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// VM-level rating race (same gates as <c>LibraryViewModel.OnPreviewRatingChanged</c>
/// + <c>RefreshSelectionSummary</c>). WinUI-free: LoadPreview in flight → selection
/// change → rating must write only the current selection (or nothing).
/// </summary>
public sealed class PreviewRatingRaceTests
{
    [Fact]
    public void RatingDuringInFlightPreview_WritesOnlyCurrentSelection()
    {
        // Preview finished for a,b — stars enabled (RefreshSelectionSummary / LoadPreview).
        var previewTargets = new List<string> { "a", "b" };
        var selection = new List<string> { "a", "b" };
        var canEdit = EnableRating(selection, previewTargets);
        Assert.True(canEdit);
        Assert.Equal(
            ["a", "b"],
            PreviewPersist.RatingWriteTargetIds(false, canEdit, selection, previewTargets));

        // Selection changes to b,c while LoadPreviewAsync for the new set is still in flight
        // (previewTargets still a,b). RefreshSelectionSummary must disable stars.
        selection = ["b", "c"];
        canEdit = EnableRating(selection, previewTargets);
        Assert.False(canEdit);

        // Rating change while load is in flight — write nothing (not the stale a,b set).
        var written = PreviewPersist.RatingWriteTargetIds(
            applyingPreview: false,
            canEditRating: canEdit,
            selectionIdsAtChange: selection,
            previewTargetIds: previewTargets);
        Assert.Empty(written);

        // Belt: even if CanEditRating were stuck true (pre-B1 bug), mismatch still blocks.
        written = PreviewPersist.RatingWriteTargetIds(
            applyingPreview: false,
            canEditRating: true,
            selectionIdsAtChange: selection,
            previewTargetIds: previewTargets);
        Assert.Empty(written);
        Assert.DoesNotContain("a", written);

        // LoadPreviewAsync completes for b,c — stars on; write only current selection.
        previewTargets = ["b", "c"];
        canEdit = EnableRating(selection, previewTargets);
        Assert.True(canEdit);
        written = PreviewPersist.RatingWriteTargetIds(
            applyingPreview: false,
            canEditRating: canEdit,
            selectionIdsAtChange: selection,
            previewTargetIds: previewTargets);
        Assert.Equal(["b", "c"], written);
        Assert.DoesNotContain("a", written);
    }

    private static bool EnableRating(
        IReadOnlyList<string> selection,
        IReadOnlyList<string> previewTargets) =>
        PreviewPersist.ShouldEnableRatingEdit(
            selection.Count,
            PreviewPersist.PreviewTargetsMatchSelection(previewTargets, selection));
}
