using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// WinUI-free Library folder-tree path shared by <c>LibraryViewModel</c> and Palace.Tests.
/// Builds via <see cref="FolderTreeBuilder"/> the same way the VM rebuilds after create / move,
/// and owns the count / Narrator face strings on tree rows.
/// </summary>
public static class FolderTreeVm
{
    /// <summary>
    /// Catalog sources + asset paths → tree entries (disk listing + hidden check for local sources).
    /// Call off the UI thread — same contract as the VM's <c>BuildFolderNodes</c>.
    /// </summary>
    public static List<FolderTreeEntry> BuildFromCatalog(
        IReadOnlyList<SourceFolder> sources,
        IReadOnlyList<Asset> assets,
        FolderTreeSort sort) =>
        FolderTreeBuilder.Build(
            sources.Select(s => new FolderTreeSource(s.Id, s.Path, s.Kind == SourceKind.Local)).ToList(),
            assets.Select(a => (a.SourceFolderId, a.Path)),
            FolderTreeBuilder.ListDiskChildren,
            sort,
            FolderTreeBuilder.IsHiddenOnDisk);

    /// <summary>Face count for a tree row, including <c>0</c> for empty folders.</summary>
    public static string CountText(int assetCount) => assetCount.ToString("N0");

    /// <summary>
    /// Exact leaf name <c>_rejected</c> (case-insensitive) — red/pink folder glyph + count
    /// in the tree (Delete-rejected mockups). Not path-based: nested siblings share the name.
    /// </summary>
    public static bool IsRejectedFolderName(string? name) =>
        string.Equals(name, ReviewReject.FolderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>UIA name: includes count or "empty" so Narrator is not name-only.</summary>
    public static string AutomationName(string name, int assetCount) =>
        assetCount > 0 ? $"{name}, {assetCount} items" : $"{name}, empty";

    /// <summary>Depth-first path match (case-insensitive), same idea as the VM's FindNode.</summary>
    public static FolderTreeEntry? Find(IEnumerable<FolderTreeEntry> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var child = Find(node.Children, path);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }
}
