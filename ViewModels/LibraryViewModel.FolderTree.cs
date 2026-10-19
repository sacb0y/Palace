using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Windows.Storage;

namespace Palace.ViewModels;

/// <summary>
/// Library folder tree: sort, inline New folder / Rename, Delete (empty only), Show in File Explorer.
/// Disk work goes through <see cref="FileOpService"/> (undo journal, watcher suppression, root checks).
/// </summary>
public partial class LibraryViewModel
{
    private const string FolderSortKeyPrefix = "FolderTreeSort_";
    private bool _folderSortLoaded;

    [ObservableProperty]
    public partial FolderTreeSort FolderSort { get; set; }

    public string FolderSortLabel => FolderTreeBuilder.Label(FolderSort);

    partial void OnFolderSortChanged(FolderTreeSort value)
    {
        OnPropertyChanged(nameof(FolderSortLabel));
        if (!_folderSortLoaded || AppServices.CurrentProject is not { } project)
        {
            return;
        }

        ApplicationData.Current.LocalSettings.Values[FolderSortKeyPrefix + project.Id] = value.ToString();
        _ = ErrorReporter.RunAsync("Sort folders", Notify, () => RebuildFolderTreeAsync());
    }

    private void LoadFolderSort()
    {
        _folderSortLoaded = false;
        FolderSort = AppServices.CurrentProject is { } project
            ? FolderTreeBuilder.Parse(ApplicationData.Current.LocalSettings.Values[FolderSortKeyPrefix + project.Id] as string)
            : FolderTreeSort.NameAsc;
        _folderSortLoaded = true;
    }

    [RelayCommand]
    private void SetFolderSort(string? value) => FolderSort = FolderTreeBuilder.Parse(value);

    /// <summary>Toolbar / Ctrl+Shift+N / tree menu: add an inline "New folder" row under <paramref name="parent"/>.</summary>
    [RelayCommand]
    private void NewFolder(FolderNode? parent)
    {
        if (IsTagBrowse)
        {
            IsTagBrowse = false;
        }

        parent ??= SelectedFolder ?? LocalFolderRoots.FirstOrDefault();
        if (parent is null || parent.IsPending || !IsLocalNode(parent))
        {
            Notify(parent is null ? "Add a local watched folder first." : "New folders can only go in local watched folders.");
            return;
        }

        CancelPendingEdits(FolderTree);
        var name = FolderNames.UniqueChild(parent.Path, "New folder", candidate => FileOpService.ChildExists(parent.Path, candidate));
        var pending = new FolderNode
        {
            Name = name,
            EditName = name,
            Path = Path.Combine(parent.Path, name),
            SourceFolderId = parent.SourceFolderId,
            IsPending = true
        };
        parent.IsExpanded = true;
        _collapsedFolders.Remove(parent.Path);
        parent.Children.Insert(0, pending);
        pending.IsEditing = true;
    }

    [RelayCommand]
    private void RenameFolder(FolderNode? node)
    {
        node ??= SelectedFolder;
        if (node is null || node.IsPending)
        {
            return;
        }

        if (node.IsRoot)
        {
            Notify("Watched folders can't be renamed here. Rename it in File Explorer, then re-add it.");
            return;
        }

        if (!IsLocalNode(node))
        {
            Notify("Only folders in local watched folders can be renamed.");
            return;
        }

        CancelPendingEdits(FolderTree);
        node.EditName = node.Name;
        node.IsEditing = true;
    }

    /// <summary>Inline editor Enter / focus loss. Returns false when the editor should stay open.</summary>
    public async Task<bool> CommitFolderEditAsync(FolderNode node, string text, bool keepOpenOnError)
    {
        if (!node.IsEditing)
        {
            return true;
        }

        node.IsEditing = false;
        var parentDir = Path.GetDirectoryName(node.Path.TrimEnd('\\', '/')) ?? "";
        if (node.IsPending)
        {
            var created = await CreateFolderAsync(parentDir, text);
            if (created.Ok)
            {
                ShowUndoToastFor(created.Message);
                return true;
            }

            return FailEdit(node, text, created.Message, keepOpenOnError);
        }

        if (string.Equals(text.Trim(), node.Name, StringComparison.Ordinal))
        {
            return true;
        }

        var oldPath = node.Path;
        var renamed = await _fileOps.RenameFolderAsync(oldPath, text, AppServices.CurrentProject.Id);
        if (!renamed.Ok || renamed.Path is null)
        {
            return FailEdit(node, text, renamed.Message, keepOpenOnError);
        }

        await ApplyFolderRenameAsync(oldPath, renamed.Path);
        await RefreshUndoStateAsync();
        ShowUndoToastFor(renamed.Message);
        return true;
    }

    public void CancelFolderEdit(FolderNode node)
    {
        node.IsEditing = false;
        if (node.IsPending)
        {
            RemoveNode(FolderTree, node);
        }
    }

    [RelayCommand]
    private Task DeleteFolderAsync(FolderNode? node) =>
        ErrorReporter.RunAsync("Delete folder", Notify, async () =>
        {
            node ??= SelectedFolder;
            if (node is null || node.IsPending)
            {
                return;
            }

            if (node.IsRoot)
            {
                Notify("Watched folders can't be deleted here. Remove the watched folder in Settings instead.");
                return;
            }

            if (node.AssetCount > 0 || !FileOpService.IsEmptyFolder(node.Path))
            {
                Notify($"{node.Name} isn't empty. Only empty folders can be deleted.");
                return;
            }

            if (RequestConfirm is not null
                && !await RequestConfirm("Delete folder", $"Delete the empty folder {node.Name}? You can undo this.", "Delete"))
            {
                return;
            }

            var parentPath = Path.GetDirectoryName(node.Path.TrimEnd('\\', '/'));
            var wasSelected = SelectedFolder is { } sel && FolderNames.IsSameOrUnder(sel.Path, node.Path);
            var result = await _fileOps.DeleteEmptyFolderAsync(node.Path, AppServices.CurrentProject.Id);
            if (!result.Ok)
            {
                Notify(result.Message);
                return;
            }

            _moveRecents = DestinationRecents.Rebase(_moveRecents, node.Path, null);
            SaveMoveRecents();
            await RebuildFolderTreeAsync(wasSelected ? parentPath : null);
            if (wasSelected)
            {
                await ApplyFilterAsync();
            }

            await RefreshUndoStateAsync();
            ShowUndoToastFor(result.Message);
        });

    [RelayCommand]
    private void ShowFolderInExplorer(FolderNode? node)
    {
        node ??= SelectedFolder;
        if (node is null || node.IsPending || !Directory.Exists(node.Path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{node.Path}\"",
            UseShellExecute = true
        });
    }

    /// <summary>Tree menu state for one folder.</summary>
    public bool CanEditFolder(FolderNode? node) => node is { IsPending: false, IsRoot: false } && IsLocalNode(node);

    public bool CanAddFolderIn(FolderNode? node) => node is { IsPending: false } && IsLocalNode(node);

    /// <summary>Drop target check: a real local folder that isn't a pending row.</summary>
    public bool CanDropOn(FolderNode? node) => CanAddFolderIn(node);

    private bool IsLocalNode(FolderNode node) => node.SourceFolderId is { } id && _localSourceIds.Contains(id);

    private bool FailEdit(FolderNode node, string text, string message, bool keepOpen)
    {
        Notify(message);
        if (keepOpen)
        {
            node.EditName = text;
            node.IsEditing = true;
            return false;
        }

        if (node.IsPending)
        {
            RemoveNode(FolderTree, node);
        }

        return true;
    }

    /// <summary>After a folder rename: fix in-memory paths, recents, the tree and the view.</summary>
    private async Task ApplyFolderRenameAsync(string oldDir, string newDir)
    {
        foreach (var asset in _allAssets)
        {
            if (FolderNames.IsSameOrUnder(asset.Path, oldDir))
            {
                asset.Path = RebasePath(asset.Path, oldDir, newDir);
            }
        }

        _moveRecents = DestinationRecents.Rebase(_moveRecents, oldDir, newDir);
        SaveMoveRecents();
        var selected = SelectedFolder?.Path;
        var reselect = selected is not null && FolderNames.IsSameOrUnder(selected, oldDir)
            ? RebasePath(selected, oldDir, newDir)
            : null;
        await RebuildFolderTreeAsync(reselect);
        if (reselect is not null || IsTagBrowse || !string.IsNullOrWhiteSpace(SearchQuery) || SelectedFolder is null)
        {
            await ApplyFilterAsync();
        }
    }

    public static string RebasePath(string path, string oldDir, string newDir)
    {
        var rel = Path.GetRelativePath(oldDir, path);
        return rel == "." ? newDir : Path.Combine(newDir, rel);
    }

    private static void CancelPendingEdits(IList<FolderNode> nodes)
    {
        foreach (var node in nodes.ToList())
        {
            node.IsEditing = false;
            if (node.IsPending)
            {
                nodes.Remove(node);
                continue;
            }

            CancelPendingEdits(node.Children);
        }
    }

    private static bool RemoveNode(IList<FolderNode> nodes, FolderNode target)
    {
        if (nodes.Remove(target))
        {
            return true;
        }

        foreach (var node in nodes)
        {
            if (RemoveNode(node.Children, target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Ids a tile drag carries (the selection when the tile is part of it).</summary>
    public IReadOnlyList<string> DragAssetIds(AssetItem item) =>
        AssetDrag.Ids(item.Id, item.IsSelected, _selection.Where(s => !s.IsFolderHeader).Select(s => s.Id));
}
