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
        SelectionSummary = SelectMode.Summary(IsSelectMode, _selection.Count, CountCollapsedSelected());
        ShowTagSelectedCta = SelectMode.ShowBatchTagCta(IsSelectMode, _selection.Count);
    }

    private void InvalidateDisplayedSelectionCache() => _displayedSelectionCache = null;

    /// <summary>
    /// Displayed Assets set for selection / collapsed counts. Cached across
    /// live drag-select updates (<c>loadPreview: false</c>); rebuilt when Assets change.
    /// </summary>
    private HashSet<AssetItem> DisplayedForSelection(bool refresh = false)
    {
        if (refresh || _displayedSelectionCache is null)
        {
            _displayedSelectionCache = new HashSet<AssetItem>(Assets);
        }

        return _displayedSelectionCache;
    }

    /// <summary>
    /// Selected tiles under collapsed section headers (in <c>_selection</c> /
    /// ordered mosaic but not in display <see cref="Assets"/>).
    /// </summary>
    internal int CountCollapsedSelected() =>
        CountCollapsedAmongTargetIds(_selection.Select(s => s.Id));

    /// <summary>
    /// Hidden mosaic tiles among catalog/delete targets (by id — not
    /// <c>_selection</c>, so confirms stay correct if targets differ).
    /// </summary>
    internal int CountCollapsedAmongTargetIds(IEnumerable<string> targetIds)
    {
        if (_mosaicOrder.Count == 0)
        {
            return 0;
        }

        return SelectMode.CountCollapsedAmongTargets(
            _mosaicOrder,
            item => item.IsFolderHeader,
            item => item.Id,
            DisplayedForSelection(),
            targetIds);
    }

    /// <summary>Esc exits select mode without clearing the selection (matches hold/Ctrl+A entry).</summary>
    public void ExitSelectMode()
    {
        if (!SelectMode.ExitsSelectModeOnEscape(IsSelectMode, IsGalleryOverlayOpen))
        {
            return;
        }

        IsSelectMode = false;
        // OnIsSelectModeChanged refreshes; call again so the bar still shows "N selected"
        // after Esc (selection kept, select mode off).
        RefreshSelectionSummary();
    }

    public void SetSelection(IEnumerable<AssetItem> items, bool loadPreview = true)
    {
        _selection = items.Where(item => !item.IsFolderHeader).ToList();
        var selected = new HashSet<AssetItem>(_selection);
        // Live drag reuses the display HashSet; end of drag / normal select rebuilds it.
        var displayed = DisplayedForSelection(refresh: loadPreview);
        foreach (var asset in Assets)
        {
            asset.IsSelected = selected.Contains(asset);
        }

        // Hidden (collapsed-section) tiles stay in _selection / ordered list.
        foreach (var asset in _mosaicOrder)
        {
            if (!asset.IsFolderHeader && !displayed.Contains(asset))
            {
                asset.IsSelected = selected.Contains(asset);
            }
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
    /// item. Folder headers are skipped. Indexes are into the display
    /// <see cref="Assets"/> list; the range expands through the full ordered
    /// mosaic so collapsed-section tiles between the ends still count.
    /// Live drag passes <paramref name="loadPreview"/> false so hover does not
    /// re-query the catalog on every move (and reuses the display HashSet cache).
    /// </summary>
    public void ApplyMosaicRange(int from, int to, bool loadPreview = true)
    {
        var ordered = _mosaicOrder.Count > 0 ? (IReadOnlyList<AssetItem>)_mosaicOrder : Assets;
        if ((uint)from >= (uint)Assets.Count || (uint)to >= (uint)Assets.Count)
        {
            return;
        }

        var fromOrdered = IndexInOrdered(ordered, Assets[from]);
        var toOrdered = IndexInOrdered(ordered, Assets[to]);
        if (fromOrdered < 0 || toOrdered < 0)
        {
            return;
        }

        var flags = ordered.Select(asset => asset.IsFolderHeader).ToList();
        var indexes = RangeSelect.ContiguousIndexes(flags, fromOrdered, toOrdered);
        SetSelection(indexes.Select(i => ordered[i]), loadPreview);
        if (!Assets[from].IsFolderHeader)
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

        var displayed = DisplayedForSelection(refresh: true);
        if (!toggle)
        {
            foreach (var asset in Assets)
            {
                asset.IsSelected = asset == item;
            }

            foreach (var asset in _mosaicOrder)
            {
                if (!asset.IsFolderHeader && !displayed.Contains(asset))
                {
                    asset.IsSelected = false;
                }
            }

            _selection = [item];
            _selectionAnchor = item;
        }
        else
        {
            item.IsSelected = !item.IsSelected;
            var selected = new HashSet<AssetItem>();
            foreach (var asset in Assets)
            {
                if (asset.IsSelected)
                {
                    selected.Add(asset);
                }
            }

            foreach (var asset in _mosaicOrder)
            {
                if (!asset.IsFolderHeader && asset.IsSelected && !displayed.Contains(asset))
                {
                    selected.Add(asset);
                }
            }

            if (item.IsSelected)
            {
                selected.Add(item);
                _selectionAnchor = item;
            }
            else
            {
                selected.Remove(item);
            }

            _selection = selected.ToList();
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

    private static int IndexInOrdered(IReadOnlyList<AssetItem> ordered, AssetItem item)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ReferenceEquals(ordered[i], item))
            {
                return i;
            }
        }

        return -1;
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

        foreach (var asset in _mosaicOrder)
        {
            if (!asset.IsFolderHeader)
            {
                asset.IsSelected = false;
            }
        }

        _selection.Clear();
        _selectionAnchor = null;
        InvalidateDisplayedSelectionCache();
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
