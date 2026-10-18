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

    private void ReplaceFolderTree(IReadOnlyList<FolderNode> roots)
    {
        FolderTree.Clear();
        foreach (var root in roots)
        {
            FolderTree.Add(root);
        }
    }

    private static List<FolderNode> BuildFolderNodes(IReadOnlyList<SourceFolder> sources, IReadOnlyList<Asset> assets)
    {
        var roots = new List<FolderNode>();
        foreach (var source in sources)
        {
            var root = new FolderNode
            {
                Name = Path.GetFileName(source.Path.TrimEnd('\\')) is { Length: > 0 } n ? n : source.Path,
                Path = source.Path,
                SourceFolderId = source.Id
            };
            var dirs = assets.Where(a => a.SourceFolderId == source.Id)
                .Select(a => Path.GetDirectoryName(a.Path) ?? source.Path)
                .Where(d => d.StartsWith(source.Path, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs)
            {
                EnsurePath(root, source.Path, dir);
            }

            roots.Add(root);
        }

        return roots;
    }

    private static void EnsurePath(FolderNode root, string sourcePath, string fullDir)
    {
        if (string.Equals(fullDir, sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relative = Path.GetRelativePath(sourcePath, fullDir);
        if (relative.StartsWith(".."))
        {
            return;
        }

        var current = root;
        var acc = sourcePath;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            acc = Path.Combine(acc, part);
            var next = current.Children.FirstOrDefault(c => c.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                next = new FolderNode { Name = part, Path = acc, SourceFolderId = root.SourceFolderId };
                current.Children.Add(next);
            }

            current = next;
        }
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
