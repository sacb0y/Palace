namespace Palace.Helpers;

/// <summary>
/// Reject destination for Review mode: sibling <c>_rejected</c> under the
/// asset's parent folder (not the watched source root). WinUI-free for Palace.Tests.
/// Folder name matches <see cref="TouchGestures.RejectedFolderName"/>.
/// </summary>
public static class ReviewReject
{
    public static string FolderName => TouchGestures.RejectedFolderName;

    /// <summary>
    /// Parent folder of <paramref name="assetPath"/> that should hold
    /// <see cref="FolderName"/>. Null when the path has no directory.
    /// </summary>
    public static string? ParentDir(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        var dir = Path.GetDirectoryName(assetPath);
        return string.IsNullOrWhiteSpace(dir) ? null : dir;
    }

    /// <summary>Full path of <c>_rejected</c> beside the asset.</summary>
    public static string? DestinationDir(string? assetPath)
    {
        var parent = ParentDir(assetPath);
        return parent is null ? null : Path.Combine(parent, FolderName);
    }

    /// <summary>
    /// True when the asset already lives inside a <c>_rejected</c> folder
    /// (do not nest another).
    /// </summary>
    public static bool IsAlreadyRejected(string? assetPath)
    {
        var parent = ParentDir(assetPath);
        if (parent is null)
        {
            return false;
        }

        var leaf = Path.GetFileName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(leaf, FolderName, StringComparison.OrdinalIgnoreCase);
    }
}
