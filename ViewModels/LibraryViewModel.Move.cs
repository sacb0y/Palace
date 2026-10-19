using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Windows.Storage;

namespace Palace.ViewModels;

/// <summary>
/// Library: Move to (recent destinations, Move dialog, new folder) and Undo for file operations.
/// The file work lives in <see cref="FileOpService"/>; this partial only drives the UI and keeps the
/// mosaic / folder tree in step without a full reload.
/// </summary>
public partial class LibraryViewModel
{
    private IReadOnlyList<string> _moveRecents = [];
    private int _undoToastEpoch;

    /// <summary>Page shows the Move dialog; returns the chosen folder or null.</summary>
    public Func<Task<string?>>? RequestPickMoveDestination { get; set; }

    /// <summary>Recent Move destinations that still exist, newest first (context menu "Move to recent").</summary>
    public ObservableCollection<MoveDestinationItem> MoveRecents { get; } = [];

    [ObservableProperty]
    public partial bool CanUndoFileOp { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UndoToolTip))]
    public partial string UndoLabel { get; set; } = "Nothing to undo";

    public string UndoToolTip => $"{UndoLabel} (Ctrl+Z)";

    [ObservableProperty]
    public partial bool ShowUndoToast { get; set; }

    [ObservableProperty]
    public partial string UndoToastMessage { get; set; } = "";

    /// <summary>Local watched folders of the current project (Move dialog, recents filter).</summary>
    public IReadOnlyList<FolderNode> LocalFolderRoots =>
        FolderTree.Where(root => root.SourceFolderId is { } id && _localSourceIds.Contains(id)).ToList();

    [RelayCommand]
    private Task MoveToFolderAsync() =>
        ErrorReporter.RunAsync("Move to folder", Notify, async () =>
        {
            if (!HasMoveTargets())
            {
                Notify("Select assets first.");
                return;
            }

            if (RequestPickMoveDestination is null)
            {
                return;
            }

            var destination = await RequestPickMoveDestination();
            if (string.IsNullOrWhiteSpace(destination))
            {
                return;
            }

            await MoveSelectionToAsync(destination);
        });

    [RelayCommand]
    private Task MoveToRecentAsync(string? destination) =>
        ErrorReporter.RunAsync("Move to folder", Notify, async () =>
        {
            if (string.IsNullOrWhiteSpace(destination))
            {
                return;
            }

            await MoveSelectionToAsync(destination);
        });

    [RelayCommand]
    private Task UndoFileOpAsync() =>
        ErrorReporter.RunAsync("Undo", Notify, async () =>
        {
            var projectId = AppServices.CurrentProject.Id;
            BeginBusy("Undoing…");
            try
            {
                ShowUndoToast = false;
                var peek = await _fileOps.PeekUndoAsync(projectId);
                var result = await _fileOps.UndoLastAsync(projectId);
                if (peek?.Kind == FileOpKind.Move && result.Moves.Count > 0)
                {
                    await ApplyMovesToUiAsync(result.Moves, destinationDir: null);
                }
                else if (result.BatchId is not null)
                {
                    await RefreshQuietAsync();
                }

                Notify(result.Message);
            }
            finally
            {
                EndBusy();
                await RefreshUndoStateAsync();
            }
        });

    /// <summary>Move dialog "New folder": create <paramref name="name"/> under <paramref name="parentDir"/>.</summary>
    public async Task<FileOpResult> CreateFolderAsync(string parentDir, string name)
    {
        var result = await _fileOps.CreateFolderAsync(parentDir, name, AppServices.CurrentProject.Id);
        if (result.Ok)
        {
            await RefreshUndoStateAsync();
        }

        return result;
    }

    /// <summary>Drag and drop / CLI-style entry: move these catalog ids into a folder.</summary>
    public Task MoveAssetIdsToAsync(IReadOnlyList<string> assetIds, string destinationDir) =>
        ErrorReporter.RunAsync("Move to folder", Notify, async () =>
        {
            var assets = await _catalog.GetAssetsByIdsAsync(assetIds);
            await MoveAssetsToAsync(assets, destinationDir);
        });

    private bool HasMoveTargets() =>
        _selection.Any(s => !s.IsFolderHeader) || SelectedAsset is { IsFolderHeader: false };

    private async Task MoveSelectionToAsync(string destinationDir)
    {
        // Move acts on picked tiles only — never a whole folder because nothing was selected.
        var ids = _selection.Count > 0
            ? _selection.Where(s => !s.IsFolderHeader && !string.IsNullOrEmpty(s.Id)).Select(s => s.Id).ToList()
            : SelectedAsset is { IsFolderHeader: false } one ? [one.Id] : [];
        if (ids.Count == 0)
        {
            Notify("Select assets first.");
            return;
        }

        var assets = await _catalog.GetAssetsByIdsAsync(ids);
        await MoveAssetsToAsync(assets, destinationDir);
    }

    private async Task MoveAssetsToAsync(IReadOnlyList<Asset> assets, string destinationDir)
    {
        if (assets.Count == 0)
        {
            Notify("Select assets first.");
            return;
        }

        BeginBusy(assets.Count == 1 ? "Moving…" : $"Moving {assets.Count}…");
        FileOpResult result;
        try
        {
            result = await _fileOps.MoveAssetsAsync(assets, destinationDir, AppServices.CurrentProject.Id);
            if (result.Moves.Count > 0)
            {
                CloseOverlay();
                await ApplyMovesToUiAsync(result.Moves, result.Path);
                PushMoveRecent(result.Path!);
            }
        }
        finally
        {
            EndBusy();
        }

        await RefreshUndoStateAsync();
        if (result.Ok && result.BatchId is not null)
        {
            ShowUndoToastFor(result.Message);
        }
        else
        {
            Notify(result.Message);
        }
    }

    /// <summary>
    /// Keep <see cref="_allAssets"/>, the folder tree and the visible mosaic in step after files moved,
    /// without <see cref="RefreshQuietAsync"/> or a source scan.
    /// </summary>
    private async Task ApplyMovesToUiAsync(IReadOnlyList<FileOpMove> moves, string? destinationDir)
    {
        var byId = moves.ToDictionary(m => m.AssetId, StringComparer.Ordinal);
        foreach (var asset in _allAssets)
        {
            if (byId.TryGetValue(asset.Id, out var move))
            {
                asset.Path = move.NewPath;
                asset.FileName = Path.GetFileName(move.NewPath);
                asset.SourceFolderId = move.NewSourceId;
                asset.IsOrphan = false;
            }
        }

        await RebuildFolderTreeAsync();

        var viewFolder = !IsTagBrowse ? SelectedFolder?.Path : null;
        var flat = !string.IsNullOrWhiteSpace(SearchQuery) || IsTagBrowse;
        // Undo moves files back to many folders; let the view query decide what is visible.
        var plan = destinationDir is null
            ? (flat || viewFolder is null ? MosaicMoveUpdate.UpdateInPlace : MosaicMoveUpdate.Requery)
            : MosaicMove.Plan(viewFolder, flat, destinationDir);
        switch (plan)
        {
            case MosaicMoveUpdate.Prune:
                await RemoveDeletedFromUiAsync(moves.Select(m => m.AssetId).ToList(), pruneAllAssets: false);
                break;
            case MosaicMoveUpdate.Requery:
                await UiDispatch.RunAsync(ClearSelection);
                await ApplyFilterAsync();
                break;
            default:
                await UiDispatch.RunAsync(() =>
                {
                    foreach (var item in Assets)
                    {
                        if (!item.IsFolderHeader && byId.TryGetValue(item.Id, out var move))
                        {
                            item.Path = move.NewPath;
                            item.FileName = Path.GetFileName(move.NewPath);
                            item.SourceFolderId = move.NewSourceId;
                        }
                    }

                    ClearSelection();
                });
                break;
        }
    }

    /// <summary>Rebuild the folder tree from the in-memory catalog, keeping the selected folder.</summary>
    private async Task RebuildFolderTreeAsync()
    {
        var projectId = AppServices.CurrentProject.Id;
        var sources = await _catalog.GetSourceFoldersAsync(projectId);
        var assets = _allAssets;
        var tree = await Task.Run(() => BuildFolderNodes(sources, assets));
        await UiDispatch.RunAsync(() =>
        {
            var selectedPath = SelectedFolder?.Path;
            _suppressFilter = true;
            ReplaceFolderTree(tree);
            SelectedFolder = selectedPath is null || IsTagBrowse ? null : FindNode(FolderTree, selectedPath);
            _suppressFilter = false;
            RebuildBreadcrumbs();
        });
    }

    public async Task RefreshUndoStateAsync()
    {
        var batch = await _fileOps.PeekUndoAsync(AppServices.CurrentProject.Id);
        await UiDispatch.RunAsync(() =>
        {
            CanUndoFileOp = batch is not null;
            UndoLabel = FileOpService.UndoLabel(batch);
        });
    }

    private void ShowUndoToastFor(string message)
    {
        var epoch = Interlocked.Increment(ref _undoToastEpoch);
        _ = UiDispatch.RunAsync(() =>
        {
            UndoToastMessage = message;
            ShowUndoToast = true;
            StatusText = message;
        });
        _ = HideUndoToastLaterAsync(epoch);
    }

    private async Task HideUndoToastLaterAsync(int epoch)
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        if (epoch == Volatile.Read(ref _undoToastEpoch))
        {
            await UiDispatch.RunAsync(() => ShowUndoToast = false);
        }
    }

    private void LoadMoveRecents()
    {
        var project = AppServices.CurrentProject;
        _moveRecents = project is null
            ? []
            : DestinationRecents.Parse(ApplicationData.Current.LocalSettings.Values[DestinationRecents.SettingsKey(project.Id)]);
    }

    private void PushMoveRecent(string destination)
    {
        _moveRecents = DestinationRecents.Push(_moveRecents, destination);
        SaveMoveRecents();
    }

    private void SaveMoveRecents()
    {
        if (AppServices.CurrentProject is { } project)
        {
            ApplicationData.Current.LocalSettings.Values[DestinationRecents.SettingsKey(project.Id)] =
                DestinationRecents.Serialize(_moveRecents);
        }
    }

    /// <summary>Refresh <see cref="MoveRecents"/> (existing folders inside local watched roots).</summary>
    public void RefreshMoveRecents()
    {
        var roots = LocalFolderRoots.Select(r => r.Path).ToList();
        var visible = DestinationRecents.Visible(_moveRecents, roots, Directory.Exists);
        MoveRecents.Clear();
        foreach (var path in visible)
        {
            MoveRecents.Add(MoveDestinationItem.For(path, roots));
        }
    }
}
