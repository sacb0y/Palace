namespace Palace.Helpers;

/// <summary>
/// Notes/rating TwoWay binds persist only for a user edit of the selected
/// asset. <c>ClearPreview</c> and overlay/catalog apply must not write.
/// </summary>
public static class PreviewPersist
{
    public static bool ShouldWriteNotes(
        bool applyingPreview,
        bool canEdit,
        string? selectedAssetId,
        string? previewAssetId)
    {
        if (applyingPreview || !canEdit)
        {
            return false;
        }

        if (string.IsNullOrEmpty(selectedAssetId) || string.IsNullOrEmpty(previewAssetId))
        {
            return false;
        }

        return string.Equals(selectedAssetId, previewAssetId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Multi-select rating stars apply to every selected asset. Single-select
    /// still goes through <see cref="ShouldWriteNotes"/> (same canEdit gate).
    /// </summary>
    public static bool ShouldWriteRating(
        bool applyingPreview,
        bool canEditRating,
        int targetCount) =>
        !applyingPreview && canEditRating && targetCount > 0;

    /// <summary>
    /// True when loaded preview targets are the same set as the live selection
    /// (order ignored). Rating UI must stay off until this is true so a star
    /// change cannot write the previous selection.
    /// </summary>
    public static bool PreviewTargetsMatchSelection(
        IReadOnlyList<string> previewTargetIds,
        IReadOnlyList<string> selectionIds)
    {
        if (previewTargetIds.Count != selectionIds.Count)
        {
            return false;
        }

        if (previewTargetIds.Count == 0)
        {
            return true;
        }

        var pending = new HashSet<string>(previewTargetIds, StringComparer.Ordinal);
        foreach (var id in selectionIds)
        {
            if (!pending.Remove(id))
            {
                return false;
            }
        }

        return pending.Count == 0;
    }

    /// <summary>
    /// Rating stars are editable only when there is a selection and the side
    /// preview has finished loading that same set into <c>_previewTargets</c>.
    /// </summary>
    public static bool ShouldEnableRatingEdit(
        int selectionCount,
        bool previewTargetsMatchSelection) =>
        selectionCount > 0 && previewTargetsMatchSelection;

    /// <summary>
    /// Belt-and-braces gate for a rating change: CanEditRating is on, preview
    /// targets match the live selection snapshot, and we are not applying a
    /// catalog load into the binds.
    /// </summary>
    public static bool ShouldApplyRatingChange(
        bool applyingPreview,
        bool canEditRating,
        IReadOnlyList<string> selectionIdsAtChange,
        IReadOnlyList<string> previewTargetIds) =>
        ShouldWriteRating(applyingPreview, canEditRating, selectionIdsAtChange.Count)
        && PreviewTargetsMatchSelection(previewTargetIds, selectionIdsAtChange);

    /// <summary>
    /// Ids that a rating change may write. Always the selection snapshot when
    /// the gate passes — never the stale preview-target list. Empty when a
    /// LoadPreviewAsync is still in flight after a selection change.
    /// </summary>
    public static IReadOnlyList<string> RatingWriteTargetIds(
        bool applyingPreview,
        bool canEditRating,
        IReadOnlyList<string> selectionIdsAtChange,
        IReadOnlyList<string> previewTargetIds) =>
        ShouldApplyRatingChange(
            applyingPreview, canEditRating, selectionIdsAtChange, previewTargetIds)
            ? selectionIdsAtChange
            : [];
}
