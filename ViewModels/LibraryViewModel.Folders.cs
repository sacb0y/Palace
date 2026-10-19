using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Palace.ViewModels;

/// <summary>Library: folder tree and breadcrumbs.</summary>
public partial class LibraryViewModel
{
    public void NavigateBreadcrumb(PathCrumb crumb)
    {
        if (IsTagBrowse)
        {
            SelectedFilterTags.Clear();
            if (crumb.Path.Length > 0)
            {
                var tagNode = TagTreeBuilder.Find(TagTree, crumb.Path);
                if (tagNode?.TagId is not null)
                {
                    SelectedFilterTags.Add(ToFilterChip(tagNode));
                    SelectedTag = tagNode;
                }
            }
            else
            {
                SelectedTag = null;
            }

            RestoreFilterSelection(SelectedFilterTags.Select(t => t.TagId).ToList());
            SyncFilterFlags();
            RebuildBreadcrumbs();
            _ = ApplyFilterAsync();
            return;
        }

        if (string.IsNullOrEmpty(crumb.Path))
        {
            SelectedFolder = null;
            return;
        }

        var node = FindNode(FolderTree, crumb.Path);
        if (node is not null)
        {
            SelectedFolder = node;
        }
    }

    /// <summary>Folders the user collapsed; kept across tree rebuilds (new folders, moves, sort).</summary>
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bumped by every tree rebuild / refresh; stale results are not applied.</summary>
    private int _folderTreeGeneration;

    /// <summary>Bumped by every <see cref="RefreshQuietAsync"/>; only the newest one is applied.</summary>
    private int _folderRefreshGeneration;

    /// <summary>
    /// Tree Expanding / Collapsed. Ignores nodes that are no longer in the tree: tearing down the old
    /// containers during a rebuild raises Collapsed for them, which must not stick.
    /// </summary>
    public void NoteFolderExpanded(FolderNode node, bool expanded)
    {
        if (!ContainsNode(FolderTree, node))
        {
            return;
        }

        node.IsExpanded = expanded;
        if (expanded)
        {
            _collapsedFolders.Remove(node.Path);
        }
        else
        {
            _collapsedFolders.Add(node.Path);
        }
    }

    private static bool ContainsNode(IEnumerable<FolderNode> nodes, FolderNode target)
    {
        foreach (var node in nodes)
        {
            if (ReferenceEquals(node, target) || ContainsNode(node.Children, target))
            {
                return true;
            }
        }

        return false;
    }

    private void ReplaceFolderTree(IReadOnlyList<FolderNode> roots)
    {
        // Keep an in-progress New folder / rename editor; a concurrent refresh must not wipe it.
        if (HasFolderEditInProgress(FolderTree))
        {
            return;
        }

        if (_collapsedFolders.Count > 0)
        {
            ApplyCollapsed(roots, _collapsedFolders);
        }

        FolderTree.Clear();
        foreach (var root in roots)
        {
            FolderTree.Add(root);
        }

        StampFolderSelection(SelectedFolder);
        NotifyAccentScan();
    }

    private static bool HasFolderEditInProgress(IEnumerable<FolderNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsEditing || node.IsPending)
            {
                return true;
            }

            if (HasFolderEditInProgress(node.Children))
            {
                return true;
            }
        }

        return false;
    }

    private static void ApplyCollapsed(IEnumerable<FolderNode> nodes, HashSet<string> collapsed)
    {
        foreach (var node in nodes)
        {
            if (collapsed.Contains(node.Path))
            {
                node.IsExpanded = false;
            }

            ApplyCollapsed(node.Children, collapsed);
        }
    }

    /// <summary>
    /// Folder tree from catalog asset folders plus real (possibly empty) directories under local
    /// sources. Lists disk, so call off the UI thread.
    /// </summary>
    private static List<FolderNode> BuildFolderNodes(IReadOnlyList<SourceFolder> sources, IReadOnlyList<Asset> assets, FolderTreeSort sort)
    {
        var entries = FolderTreeBuilder.Build(
            sources.Select(s => new FolderTreeSource(s.Id, s.Path, s.Kind == SourceKind.Local)).ToList(),
            assets.Select(a => (a.SourceFolderId, a.Path)),
            FolderTreeBuilder.ListDiskChildren,
            sort,
            FolderTreeBuilder.IsHiddenOnDisk);
        return entries.Select(ToNode).ToList();
    }

    private static FolderNode ToNode(FolderTreeEntry entry)
    {
        var node = new FolderNode
        {
            Name = entry.Name,
            Path = entry.Path,
            SourceFolderId = entry.SourceFolderId,
            IsRoot = entry.IsRoot,
            AssetCount = entry.AssetCount,
            Modified = entry.Modified
        };
        foreach (var child in entry.Children)
        {
            node.Children.Add(ToNode(child));
        }

        return node;
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new PathCrumb { Name = "Library", Path = "" });
        if (IsTagBrowse)
        {
            foreach (var chip in SelectedFilterTags)
            {
                Breadcrumbs.Add(new PathCrumb { Name = chip.Name, Path = chip.TagId });
            }

            return;
        }

        var node = SelectedFolder;
        if (node is null)
        {
            return;
        }

        var chain = new List<FolderNode>();
        CollectChain(FolderTree, node.Path, chain);
        if (chain.Count == 0)
        {
            Breadcrumbs.Add(new PathCrumb { Name = node.Name, Path = node.Path });
            return;
        }

        foreach (var item in chain)
        {
            Breadcrumbs.Add(new PathCrumb { Name = item.Name, Path = item.Path });
        }
    }

    private static bool CollectChain(IEnumerable<FolderNode> nodes, string path, List<FolderNode> chain)
    {
        foreach (var node in nodes)
        {
            chain.Add(node);
            if (string.Equals(node.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (CollectChain(node.Children, path, chain))
            {
                return true;
            }

            chain.RemoveAt(chain.Count - 1);
        }

        return false;
    }

    private static FolderNode? FindNode(IEnumerable<FolderNode> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var child = FindNode(node.Children, path);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }
}
