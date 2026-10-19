namespace Palace.Helpers;

/// <summary>How the visible mosaic follows a move without a full reload.</summary>
public enum MosaicMoveUpdate
{
    /// <summary>Moved tiles left the folder being viewed: drop them in place (like delete).</summary>
    Prune,
    /// <summary>Tiles stay visible (all-library, tag browse, search): rewrite their path in place.</summary>
    UpdateInPlace,
    /// <summary>Moved into a subfolder of the folder being viewed: re-run the view query so folder headers regroup.</summary>
    Requery
}

public static class MosaicMove
{
    /// <param name="viewFolder">Folder prefix the mosaic shows, or null for all-library / tag browse.</param>
    /// <param name="flatView">Search or tag browse (no folder grouping).</param>
    /// <param name="destinationDir">Folder the files moved into.</param>
    public static MosaicMoveUpdate Plan(string? viewFolder, bool flatView, string destinationDir)
    {
        if (flatView || string.IsNullOrWhiteSpace(viewFolder))
        {
            return MosaicMoveUpdate.UpdateInPlace;
        }

        return FolderNames.IsSameOrUnder(destinationDir, viewFolder)
            ? MosaicMoveUpdate.Requery
            : MosaicMoveUpdate.Prune;
    }
}
