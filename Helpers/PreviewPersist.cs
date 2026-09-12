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
}
