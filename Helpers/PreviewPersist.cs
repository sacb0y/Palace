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
}
