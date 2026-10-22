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

/// <summary>Library: file actions (rooms, Explorer, copy, delete). Move lives in <c>LibraryViewModel.Move</c>.</summary>
public partial class LibraryViewModel
{
    [RelayCommand]
    private Task AddSelectionToRoomAsync() =>
        ErrorReporter.RunAsync("Add selection to room", Notify, async () =>
        {
            if (RequestPickRoom is null || _selection.Count == 0)
            {
                Notify("Select assets first.");
                return;
            }

            var rooms = await _catalog.GetRoomsAsync(AppServices.CurrentProject.Id);
            if (rooms.Count == 0)
            {
                Notify("Create a room in the Rooms page first.");
                return;
            }

            var room = await RequestPickRoom(rooms);
            if (room is null)
            {
                return;
            }

            var existing = await _catalog.GetRoomItemsAsync(room.Id);
            var sort = existing.Count;
            foreach (var item in _selection)
            {
                await _catalog.AddToRoomAsync(room.Id, item.Id, "Pins", sort++);
            }

            Notify($"Added {_selection.Count} to {room.Name}.");
            await AppServices.Rooms.RefreshAsync();
        });

    [RelayCommand]
    private Task ShowInExplorerAsync() =>
        ErrorReporter.RunAsync("Show in explorer", Notify, async () =>
        {
            var targets = await SelectedAssetsAsync();
            if (targets.Count == 0)
            {
                Notify("Select assets first.");
                return;
            }

            var opened = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in targets)
            {
                var dir = Path.GetDirectoryName(asset.Path);
                if (string.IsNullOrEmpty(dir) || !opened.Add(dir))
                {
                    continue;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{asset.Path}\"",
                    UseShellExecute = true
                });
            }
        });

    [RelayCommand]
    private Task CopyFilesAsync() =>
        ErrorReporter.RunAsync("Copy files", Notify, async () =>
        {
            var targets = await SelectedAssetsAsync();
            var paths = AccessService.FilterCopyPaths(targets.Select(t => t.Path), out var skipped);
            var files = new List<IStorageItem>();
            foreach (var path in paths)
            {
                if (!CloudFile.Exists(path))
                {
                    continue;
                }

                try
                {
                    files.Add(await StorageFile.GetFileFromPathAsync(path));
                }
                catch
                {
                    // Skip files the package cannot open.
                }
            }

            if (files.Count == 0)
            {
                Notify(skipped > 0 ? AccessService.OnlineOnlyCopyWarning : "Nothing to copy.");
                return;
            }

            var package = new DataPackage();
            package.SetStorageItems(files);
            Clipboard.SetContent(package);
            Notify(skipped > 0 ? AccessService.OnlineOnlyCopyWarning : $"Copied {files.Count} file(s).");
        });

    [RelayCommand]
    private Task CopyPathAsync() =>
        ErrorReporter.RunAsync("Copy path", Notify, async () =>
        {
            var targets = await SelectedAssetsAsync();
            if (targets.Count == 0)
            {
                Notify("Select assets first.");
                return;
            }

            var paths = AccessService.FilterCopyPaths(targets.Select(t => t.Path), out var skipped);
            if (paths.Count == 0)
            {
                Notify(skipped > 0 ? AccessService.OnlineOnlyCopyWarning : "Nothing to copy.");
                return;
            }

            var package = new DataPackage();
            package.SetText(string.Join(Environment.NewLine, paths));
            Clipboard.SetContent(package);
            Notify(skipped > 0
                ? AccessService.OnlineOnlyCopyWarning
                : paths.Count == 1 ? "Copied path." : $"Copied {paths.Count} paths.");
        });

    [RelayCommand]
    private Task DeleteFilesAsync() =>
        ErrorReporter.RunAsync("Delete files", Notify, async () =>
        {
            var targets = await SelectedAssetsAsync();
            if (targets.Count == 0 || RequestConfirm is null)
            {
                Notify("Select assets first.");
                return;
            }

            var preview = targets.Count == 1
                ? targets[0].FileName
                : $"{targets.Count} files ({string.Join(", ", targets.Take(3).Select(t => t.FileName))}{(targets.Count > 3 ? ", …" : "")})";
            if (!await RequestConfirm(
                    "Delete",
                    $"Send {preview} to the Recycle Bin? This also removes them from the catalog.",
                    "Delete"))
            {
                return;
            }

            // Suppress watcher scan/refresh flood (parent LastWrite + N× RefreshQuiet).
            // Recycle + catalog delete, then prune mosaic in place — no ScanSource / full reload.
            BeginBusy(targets.Count == 1 ? "Deleting…" : $"Deleting {targets.Count}…");
            var removed = new List<string>();
            try
            {
                using (_watchers.SuppressNotifications())
                {
                    foreach (var asset in targets)
                    {
                        if (CloudFile.Exists(asset.Path))
                        {
                            try
                            {
                                // StorageFile.DeleteAsync recycles without hydrating On-Demand
                                // placeholders (same as single-file delete). Do not Open/read.
                                var file = await StorageFile.GetFileFromPathAsync(asset.Path);
                                await file.DeleteAsync(StorageDeleteOption.Default);
                            }
                            catch (Exception ex)
                            {
                                Notify($"Could not delete {asset.FileName}: {ex.Message}");
                                continue;
                            }
                        }

                        _thumbs.TryDelete(asset.ContentHash, asset.Path);
                        removed.Add(asset.Id);
                    }

                    if (removed.Count > 0)
                    {
                        await _catalog.DeleteAssetsAsync(removed);
                        await RemoveDeletedFromUiAsync(removed);
                    }
                }

                CloseOverlay();
                if (removed.Count > 0)
                {
                    await AppServices.Rooms.RefreshAsync();
                }

                Notify(removed.Count == 0 ? "Nothing deleted." : $"Deleted {removed.Count} file(s).");
            }
            finally
            {
                EndBusy();
            }
        });

    /// <summary>
    /// Drop deleted ids from <see cref="_allAssets"/>, the full ordered mosaic,
    /// and the visible mosaic without re-querying the whole project.
    /// </summary>
    private async Task RemoveDeletedFromUiAsync(IReadOnlyList<string> removedIds, bool pruneAllAssets = true)
    {
        if (removedIds.Count == 0)
        {
            return;
        }

        var remove = new HashSet<string>(removedIds, StringComparer.Ordinal);
        if (pruneAllAssets)
        {
            _allAssets = MosaicDelete.ExceptIds(_allAssets, a => a.Id, remove);
        }

        await UiDispatch.RunAsync(() =>
        {
            if (_mosaicOrder.Count > 0)
            {
                _mosaicOrder = MosaicDelete.PruneItems(
                    _mosaicOrder,
                    item => item.IsFolderHeader,
                    item => item.Id,
                    remove);
                MosaicDelete.RecomputeFolderItemCounts(
                    _mosaicOrder,
                    item => item.IsFolderHeader,
                    (item, count) => item.FolderItemCount = count);
            }

            var pruned = MosaicDelete.PruneItems(
                Assets,
                item => item.IsFolderHeader,
                item => item.Id,
                remove);

            var changed = pruned.Count != Assets.Count;
            if (changed)
            {
                // Prefer RemoveAt so ItemsView keeps realized tiles that stay.
                var keep = new HashSet<AssetItem>(pruned);
                for (var i = Assets.Count - 1; i >= 0; i--)
                {
                    if (!keep.Contains(Assets[i]))
                    {
                        Assets.RemoveAt(i);
                    }
                }
            }

            // Assets is the display subset of _mosaicOrder (shared AssetItem refs).
            Debug.Assert(
                _mosaicOrder.Count == 0
                || Assets.All(a => _mosaicOrder.Contains(a)),
                "Assets must stay a subset of _mosaicOrder after prune.");

            ClearSelection();
            if (changed)
            {
                MosaicChunkAppended?.Invoke();
            }

            var assetCount = _mosaicOrder.Count > 0
                ? _mosaicOrder.Count(item => !item.IsFolderHeader)
                : Assets.Count(item => !item.IsFolderHeader);
            StatusText = !string.IsNullOrWhiteSpace(SearchQuery)
                ? $"{assetCount} search results"
                : IsTagBrowse && SelectedFilterTags.Count > 0
                    ? $"{assetCount} tagged"
                    : SelectedFolder is not null
                        ? $"{assetCount} in {SelectedFolder.Name}"
                        : assetCount == 0
                            ? "Add a folder to start your library."
                            : $"{assetCount} assets";
        });
    }
}
