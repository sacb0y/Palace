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

/// <summary>Library: mosaic selection (single, toggle, range) and selection targets.</summary>
public partial class LibraryViewModel
{
    private void RefreshSelectionSummary()
    {
        SelectionSummary = SelectMode.Summary(IsSelectMode, _selection.Count);
        ShowTagSelectedCta = SelectMode.ShowBatchTagCta(IsSelectMode, _selection.Count);
    }

    public void SetSelection(IEnumerable<AssetItem> items, bool loadPreview = true)
    {
        _selection = items.Where(item => !item.IsFolderHeader).ToList();
        foreach (var asset in Assets)
        {
            asset.IsSelected = _selection.Contains(asset);
        }

        HasSelection = _selection.Count > 0;
        RefreshSelectionSummary();
        CanEditNotes = _selection.Count == 1;
        var next = _selection.Count == 1 ? _selection[0] : _selection.LastOrDefault();
        _selectionAnchor = next;
        if (!loadPreview)
        {
            return;
        }

        if (!ReferenceEquals(SelectedAsset, next))
        {
            SelectedAsset = next;
        }
        else
        {
            _ = LoadPreviewAsync(next);
        }
    }

    /// <summary>
    /// Contiguous mosaic span from the drag origin through the last highlighted
    /// item. Folder headers are skipped. The origin stays the Shift/range anchor.
    /// Live drag passes <paramref name="loadPreview"/> false so hover does not
    /// re-query the catalog on every move.
    /// </summary>
    public void ApplyMosaicRange(int from, int to, bool loadPreview = true)
    {
        var flags = Assets.Select(asset => asset.IsFolderHeader).ToList();
        var indexes = RangeSelect.ContiguousIndexes(flags, from, to);
        SetSelection(indexes.Select(i => Assets[i]), loadPreview);
        if ((uint)from < (uint)Assets.Count && !Assets[from].IsFolderHeader)
        {
            _selectionAnchor = Assets[from];
        }
    }

    public void SelectAsset(AssetItem item, bool toggle, bool range = false)
    {
        if (item.IsFolderHeader)
        {
            return;
        }

        if (range && _selectionAnchor is not null)
        {
            var from = Assets.IndexOf(_selectionAnchor);
            var to = Assets.IndexOf(item);
            if (from >= 0 && to >= 0)
            {
                ApplyMosaicRange(from, to);
                return;
            }
        }

        if (!toggle)
        {
            foreach (var asset in Assets)
            {
                asset.IsSelected = asset == item;
            }

            _selection = [item];
            _selectionAnchor = item;
        }
        else
        {
            item.IsSelected = !item.IsSelected;
            _selection = Assets.Where(a => a.IsSelected).ToList();
            if (item.IsSelected)
            {
                _selectionAnchor = item;
            }
        }

        HasSelection = _selection.Count > 0;
        RefreshSelectionSummary();
        CanEditNotes = _selection.Count == 1;
        var next = _selection.Count == 1 ? _selection[0] : _selection.LastOrDefault();
        if (!ReferenceEquals(SelectedAsset, next))
        {
            SelectedAsset = next;
        }
        else
        {
            _ = LoadPreviewAsync(next);
        }
    }

    /// <summary>Resolved selection for Move dialog title and file ops (Library page + VM).</summary>
    internal async Task<IReadOnlyList<Asset>> SelectedAssetsAsync()
    {
        if (_selection.Count > 0)
        {
            return await _catalog.GetAssetsByIdsAsync(
                _selection.Where(s => !s.IsFolderHeader && !string.IsNullOrEmpty(s.Id)).Select(s => s.Id));
        }

        if (SelectedAsset is not null)
        {
            var one = await _catalog.GetAssetByIdAsync(SelectedAsset.Id);
            return one is null ? [] : [one];
        }

        if (SelectedFolder is not null)
        {
            return await _catalog.GetAssetsAsync(
                folderPrefix: SelectedFolder.Path,
                projectId: AppServices.CurrentProject.Id);
        }

        return [];
    }

    private void ClearSelection()
    {
        foreach (var asset in Assets)
        {
            asset.IsSelected = false;
        }

        _selection.Clear();
        _selectionAnchor = null;
        HasSelection = false;
        RefreshSelectionSummary();
        CanEditNotes = false;
        SelectedAsset = null;
    }

    /// <summary>Select every non-header tile in the visible mosaic.</summary>
    [RelayCommand]
    private void SelectAllVisible()
    {
        var tiles = Assets.Where(a => !a.IsFolderHeader).ToList();
        SetSelection(tiles);
        RequestRestoreMosaicSelection?.Invoke();
    }

    /// <summary>
    /// Clear tiles and exit select mode (batch menu / Esc companion).
    /// Esc alone keeps the selection — see <see cref="ExitSelectMode"/>.
    /// </summary>
    [RelayCommand]
    private void ClearSelectionAndExit()
    {
        ClearSelection();
        if (SelectMode.ExitsSelectModeOnClear(IsSelectMode))
        {
            IsSelectMode = false;
        }

        RequestRestoreMosaicSelection?.Invoke();
    }

    /// <summary>Esc exits select mode without clearing the selection.</summary>
    [RelayCommand]
    private void ExitSelectMode()
    {
        if (!SelectMode.ExitsSelectModeOnEscape(IsSelectMode, IsGalleryOverlayOpen))
        {
            return;
        }

        IsSelectMode = false;
    }

    /// <summary>
    /// Batch Keep: leave files on disk (same as Review Keep). Toast only.
    /// </summary>
    [RelayCommand]
    private void KeepSelected()
    {
        var count = _selection.Count(s => !s.IsFolderHeader);
        if (count == 0)
        {
            Notify("Select assets first.");
            return;
        }

        Notify(count == 1 ? "Kept 1 file." : $"Kept {count} files.");
    }

    /// <summary>
    /// Batch Reject: move each selected file into a sibling <c>_rejected</c>
    /// (same destination as Review). Undo toast dismisses after 5 s.
    /// </summary>
    [RelayCommand]
    private Task RejectSelectedAsync() =>
        ErrorReporter.RunAsync("Reject", Notify, async () =>
        {
            var ids = _selection
                .Where(s => !s.IsFolderHeader && !string.IsNullOrEmpty(s.Id))
                .Select(s => s.Id)
                .ToList();
            if (ids.Count == 0)
            {
                Notify("Select assets first.");
                return;
            }

            var assets = await _catalog.GetAssetsByIdsAsync(ids);
            if (assets.Count == 0)
            {
                Notify("Select assets first.");
                return;
            }

            var already = assets.Where(a => ReviewReject.IsAlreadyRejected(a.Path)).ToList();
            var movers = assets.Where(a => !ReviewReject.IsAlreadyRejected(a.Path)).ToList();
            if (movers.Count == 0)
            {
                Notify(already.Count == 1
                    ? "Already in _rejected."
                    : $"All {already.Count} already in _rejected.");
                return;
            }

            var projectId = AppServices.CurrentProject.Id;
            var groups = movers
                .GroupBy(a => ReviewReject.DestinationDir(a.Path) ?? "", StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToList();

            BeginBusy(movers.Count == 1 ? "Rejecting…" : $"Rejecting {movers.Count}…");
            var moved = 0;
            string? lastMessage = null;
            string? lastBatchId = null;
            try
            {
                foreach (var group in groups)
                {
                    var dest = group.Key;
                    var parent = ReviewReject.ParentDir(group.First().Path);
                    if (parent is null)
                    {
                        continue;
                    }

                    string? absorb = null;
                    if (!Directory.Exists(dest))
                    {
                        var created = await _fileOps.CreateFolderAsync(
                            parent,
                            ReviewReject.FolderName,
                            projectId);
                        if (!created.Ok || string.IsNullOrWhiteSpace(created.Path))
                        {
                            if (!Directory.Exists(dest))
                            {
                                Notify(created.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                                continue;
                            }
                        }
                        else
                        {
                            absorb = created.Path;
                            await RebuildFolderTreeAsync();
                        }
                    }

                    var result = await _fileOps.MoveAssetsAsync(
                        group.ToList(),
                        dest,
                        projectId,
                        absorbCreatedFolder: absorb);
                    if (result.Moves.Count > 0)
                    {
                        moved += result.Moves.Count;
                        lastMessage = result.Message;
                        lastBatchId = result.BatchId;
                        await ApplyMovesToUiAsync(result.Moves, result.Path);
                    }
                    else if (!result.Ok)
                    {
                        Notify(result.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                    }
                }
            }
            finally
            {
                await UiDispatch.RunAsync(() =>
                {
                    EndBusy();
                    if (!string.IsNullOrWhiteSpace(lastMessage))
                    {
                        StatusText = lastMessage!;
                    }
                });
            }

            await RefreshUndoStateAsync();
            if (moved > 0 && lastBatchId is not null)
            {
                // Spec: Reject offers a 5 s undo window.
                ShowUndoToastFor(
                    moved == 1 ? "Rejected 1 file." : $"Rejected {moved} files.",
                    offerUndo: true,
                    dismissSeconds: 5);
            }
            else if (already.Count > 0 && moved == 0)
            {
                Notify("Nothing rejected.");
            }
        });

    private IReadOnlyList<AssetItem> TagTargets()
    {
        if (_selection.Count > 0)
        {
            return _selection;
        }

        if (_previewTargets.Count > 0)
        {
            return _previewTargets;
        }

        return SelectedAsset is null ? [] : [SelectedAsset];
    }
}
