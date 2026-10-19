using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Windows.Storage;

namespace Palace.ViewModels;

/// <summary>Result of the Move dialog: destination path, and a folder created in that dialog to fold into the Move Undo batch.</summary>
public readonly record struct MoveDestinationChoice(string Path, string? AbsorbCreatedFolder = null);

/// <summary>
/// Library: Move to (recent destinations, Move dialog, new folder) and Undo for file operations.
/// The file work lives in <see cref="FileOpService"/>; this partial only drives the UI and keeps the
/// mosaic / folder tree in step without a full reload.
/// </summary>
public partial class LibraryViewModel
{
    private IReadOnlyList<string> _moveRecents = [];
    private int _undoToastEpoch;
    private CancellationTokenSource? _undoToastHideCts;
    private int _undoToastPaused;

    /// <summary>Page shows the Move dialog; returns the chosen folder (and optional create-folder absorb path) or null.</summary>
    public Func<Task<MoveDestinationChoice?>>? RequestPickMoveDestination { get; set; }

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

    /// <summary>False after an Undo itself (no nested Undo action on the toast).</summary>
    [ObservableProperty]
    public partial bool ShowUndoToastAction { get; set; } = true;

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

            var pick = await RequestPickMoveDestination();
            if (pick is null || string.IsNullOrWhiteSpace(pick.Value.Path))
            {
                return;
            }

            await MoveSelectionToAsync(pick.Value.Path, pick.Value.AbsorbCreatedFolder);
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
            FileOpResult result = FileOpResult.Fail("Nothing to undo.");
            try
            {
                ShowUndoToast = false;
                var peek = await _fileOps.PeekUndoAsync(projectId);
                result = await _fileOps.UndoLastAsync(projectId);
                if (peek?.Kind == FileOpKind.Move && result.Moves.Count > 0)
                {
                    await ApplyMovesToUiAsync(result.Moves, destinationDir: null);
                }
                else if (peek?.Kind is FileOpKind.TagAssign or FileOpKind.TagRemove or FileOpKind.TagDelete)
                {
                    await ReloadTagCatalogAsync();
                    await LoadPreviewAsync(SelectedAsset);
                    if (AppServices.Tags is not null)
                    {
                        await AppServices.Tags.RefreshAsync();
                    }
                }
                else if (result.BatchId is not null)
                {
                    await RefreshQuietAsync();
                }

                await RefreshUndoStateAsync();
                if (result.Ok)
                {
                    ShowUndoToastFor(result.Message, offerUndo: false);
                }
                else
                {
                    Notify(result.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                }
            }
            finally
            {
                EndBusy();
                // EndBusy does not clear StatusText; mosaic refresh may leave "Undoing…" stuck.
                if (StatusText is "Undoing…" or "Undoing.")
                {
                    StatusText = result.Ok ? result.Message : "Ready.";
                }

                await RefreshUndoStateAsync();
            }
        });

    /// <summary>Move dialog "New folder": create <paramref name="name"/> under <paramref name="parentDir"/>.</summary>
    public async Task<FileOpResult> CreateFolderAsync(string parentDir, string name)
    {
        var result = await _fileOps.CreateFolderAsync(parentDir, name, AppServices.CurrentProject.Id);
        if (result.Ok)
        {
            await RebuildFolderTreeAsync();
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

    private async Task MoveSelectionToAsync(string destinationDir, string? absorbCreatedFolder = null)
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
        await MoveAssetsToAsync(assets, destinationDir, absorbCreatedFolder);
    }

    private async Task MoveAssetsToAsync(
        IReadOnlyList<Asset> assets,
        string destinationDir,
        string? absorbCreatedFolder = null)
    {
        if (assets.Count == 0)
        {
            Notify("Select assets first.");
            return;
        }

        BeginBusy(assets.Count == 1 ? "Moving…" : $"Moving {assets.Count}…");
        FileOpResult? result = null;
        try
        {
            result = await _fileOps.MoveAssetsAsync(
                assets,
                destinationDir,
                AppServices.CurrentProject.Id,
                absorbCreatedFolder: absorbCreatedFolder);
            if (result.Moves.Count > 0)
            {
                CloseOverlay();
                await ApplyMovesToUiAsync(result.Moves, result.Path);
                PushMoveRecent(result.Path!);
            }
        }
        finally
        {
            // EndBusy does not clear StatusText. In-place mosaic updates never replace "Moving…",
            // and a requery can finish on a pool thread — write the outcome on the UI thread.
            var status = result?.Message;
            await UiDispatch.RunAsync(() =>
            {
                EndBusy();
                if (!string.IsNullOrWhiteSpace(status))
                {
                    StatusText = status;
                }
            });
        }

        await RefreshUndoStateAsync();
        if (result is { Ok: true, BatchId: not null })
        {
            ShowUndoToastFor(result.Message);
        }
        else if (result is not null)
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

        var viewedBefore = !IsTagBrowse ? SelectedFolder?.Path : null;
        await RebuildFolderTreeAsync();

        var viewFolder = !IsTagBrowse ? SelectedFolder?.Path : null;
        var flat = !string.IsNullOrWhiteSpace(SearchQuery) || IsTagBrowse;
        // The rebuild drops a folder the move emptied and clears the selection; re-run the query so
        // the mosaic shows what the breadcrumbs now show instead of keeping the old folder's tiles.
        // Undo moves files back to many folders; let the view query decide what is visible.
        var plan = viewedBefore is not null && viewFolder is null
            ? MosaicMoveUpdate.Requery
            : destinationDir is null
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

    /// <summary>
    /// Rebuild the folder tree from the in-memory catalog, keeping the selected folder. A result
    /// overtaken by a newer rebuild or refresh (say a second sort change during a slow disk
    /// listing) is dropped, so the last request always wins.
    /// </summary>
    private async Task RebuildFolderTreeAsync(string? selectPath = null)
    {
        var generation = Interlocked.Increment(ref _folderTreeGeneration);
        var projectId = AppServices.CurrentProject.Id;
        var sources = await _catalog.GetSourceFoldersAsync(projectId);
        // Snapshot so a concurrent rename/move mutating Asset.Path cannot mix old/new paths in one build.
        var assets = _allAssets.ToList();
        var treeSort = FolderSort;
        var tree = await Task.Run(() => BuildFolderNodes(sources, assets, treeSort));
        await UiDispatch.RunAsync(() =>
        {
            if (generation != Volatile.Read(ref _folderTreeGeneration))
            {
                return;
            }

            var selectedPath = selectPath ?? SelectedFolder?.Path;
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

    private void ShowUndoToastFor(string message, bool offerUndo = true)
    {
        var epoch = Interlocked.Increment(ref _undoToastEpoch);
        _undoToastHideCts?.Cancel();
        _ = UiDispatch.RunAsync(() =>
        {
            // One toast slot: transient Success replaces a lingering InfoBar.
            ShowInfo = false;
            UndoToastMessage = message;
            ShowUndoToastAction = offerUndo && CanUndoFileOp;
            ShowUndoToast = true;
        });
        _ = HideUndoToastLaterAsync(epoch);
    }

    /// <summary>Pause auto-dismiss while the toast is hovered or focused (WCAG 2.2.1).</summary>
    public void PauseUndoToastTimer()
    {
        Interlocked.Increment(ref _undoToastPaused);
        _undoToastHideCts?.Cancel();
    }

    public void ResumeUndoToastTimer()
    {
        if (Interlocked.Decrement(ref _undoToastPaused) > 0)
        {
            return;
        }

        Interlocked.Exchange(ref _undoToastPaused, 0);
        if (ShowUndoToast)
        {
            _ = HideUndoToastLaterAsync(Volatile.Read(ref _undoToastEpoch));
        }
    }

    /// <summary>Shared InfUndo toast for file ops and tag assign/remove/delete.</summary>
    public void OfferUndoToast(string message) => ShowUndoToastFor(message);

    private async Task HideUndoToastLaterAsync(int epoch)
    {
        _undoToastHideCts?.Cancel();
        _undoToastHideCts = new CancellationTokenSource();
        var token = _undoToastHideCts.Token;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (epoch != Volatile.Read(ref _undoToastEpoch) || Volatile.Read(ref _undoToastPaused) > 0)
        {
            return;
        }

        await UiDispatch.RunAsync(() =>
        {
            if (epoch == Volatile.Read(ref _undoToastEpoch))
            {
                ShowUndoToast = false;
            }
        });
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
