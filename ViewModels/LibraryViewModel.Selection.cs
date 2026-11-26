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
        CanToolbarAddToRoom = SelectCommandBar.ToolbarAddToRoomEnabled(IsSelectMode, _selection.Count > 0);
        // Keep rating off until LoadPreviewAsync finishes and _previewTargets match
        // this selection — otherwise OnPreviewRatingChanged can write the stale set.
        CanEditRating = PreviewPersist.ShouldEnableRatingEdit(
            _selection.Count,
            PreviewTargetsMatchSelection());
        IsMultiSelectPreview = SelectCommandBar.ShowMultiSelectPreview(_selection.Count);
        ShowSinglePreviewFileName = _selection.Count == 1;
        AddTagButtonLabel = IsMultiSelectPreview ? "Add tags to all" : "Add tags";
        // Computed SelectionCount is not [ObservableProperty] — notify for ⋮ reveal.
        OnPropertyChanged(nameof(SelectionCount));
        RebuildMultiSelectThumbs();
    }

    private bool PreviewTargetsMatchSelection() =>
        PreviewPersist.PreviewTargetsMatchSelection(
            _previewTargets.Select(t => t.Id).ToList(),
            _selection.Select(s => s.Id).ToList());

    private void RebuildMultiSelectThumbs()
    {
        MultiSelectThumbs.Clear();
        MultiSelectThumb0 = null;
        MultiSelectThumb1 = null;
        MultiSelectThumb2 = null;
        HasMultiSelectThumb0 = false;
        HasMultiSelectThumb1 = false;
        HasMultiSelectThumb2 = false;
        MultiSelectThumb0Angle = MultiSelectThumb1Angle = MultiSelectThumb2Angle = 0;
        MultiSelectThumb0Left = MultiSelectThumb1Left = MultiSelectThumb2Left = 0;
        MultiSelectThumb0Top = MultiSelectThumb1Top = MultiSelectThumb2Top = 0;
        if (!IsMultiSelectPreview)
        {
            MultiSelectDetail = "";
            return;
        }

        var take = SelectCommandBar.StackedThumbCount(_selection.Count);
        for (var i = 0; i < take; i++)
        {
            MultiSelectThumbs.Add(_selection[i]);
        }

        var layout = SelectCommandBar.FanLayout(take);
        if (take >= 1)
        {
            MultiSelectThumb0 = _selection[0];
            HasMultiSelectThumb0 = true;
            MultiSelectThumb0Angle = layout[0].Angle;
            MultiSelectThumb0Left = layout[0].Left;
            MultiSelectThumb0Top = layout[0].Top;
        }

        if (take >= 2)
        {
            MultiSelectThumb1 = _selection[1];
            HasMultiSelectThumb1 = true;
            MultiSelectThumb1Angle = layout[1].Angle;
            MultiSelectThumb1Left = layout[1].Left;
            MultiSelectThumb1Top = layout[1].Top;
        }

        if (take >= 3)
        {
            MultiSelectThumb2 = _selection[2];
            HasMultiSelectThumb2 = true;
            MultiSelectThumb2Angle = layout[2].Angle;
            MultiSelectThumb2Left = layout[2].Left;
            MultiSelectThumb2Top = layout[2].Top;
        }
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

    /// <summary>
    /// Done / toggle-off exits select mode without clearing the selection.
    /// Esc uses <see cref="ClearSelectionAndExit"/> (modifier-click plan).
    /// </summary>
    [RelayCommand]
    public void ExitSelectMode()
    {
        if (!IsSelectMode)
        {
            return;
        }

        IsSelectMode = false;
        _selectEntryKind = SelectEntryKind.None;
        // OnIsSelectModeChanged refreshes; call again so the bar still shows "N selected"
        // after Done (selection kept, select mode off).
        RefreshSelectionSummary();
    }

    /// <summary>Record how select mode was entered (modifier vs purposeful).</summary>
    public void MarkSelectEntry(SelectEntryKind kind) => _selectEntryKind = kind;

    /// <summary>Current entry kind (tests / page plain-click branch).</summary>
    internal SelectEntryKind SelectEntryKind => _selectEntryKind;

    /// <summary>Focused tile for Preview-follow and keyboard extend.</summary>
    internal AssetItem? SelectionFocus => _selectionFocus;

    /// <summary>
    /// Enter select mode from a modifier click / purposeful path. Entry kind is
    /// stamped only when entering from off (or kind is still None) — later
    /// modifier clicks must not overwrite purposeful entry.
    /// </summary>
    public void EnsureSelectMode(SelectEntryKind kind)
    {
        var next = SelectMode.NextEntryKind(IsSelectMode, _selectEntryKind, kind);
        if (!IsSelectMode)
        {
            IsSelectMode = true;
        }

        MarkSelectEntry(next);
    }

    /// <summary>Discard the dropped-selection slot after a newer user action.</summary>
    public void DiscardDroppedSelection() => DroppedSelection.NotifyOtherAction();

    /// <summary>
    /// Capture the current selection into the one-slot dropped buffer (folder/filter
    /// change), then clear. No-op when empty.
    /// </summary>
    public void CaptureAndDropSelection()
    {
        if (_selection.Count > 0)
        {
            DroppedSelection.Capture(
                _selection.Where(s => !s.IsFolderHeader && !string.IsNullOrEmpty(s.Id)).Select(s => s.Id));
        }

        ClearSelection();
        if (IsSelectMode)
        {
            IsSelectMode = false;
            _selectEntryKind = SelectEntryKind.None;
        }
    }

    /// <summary>
    /// Ctrl+Z path: restore the one-slot dropped selection when it is still the
    /// most recent action. Returns true when handled (caller must not FileOp undo).
    /// Restores only ids still present in the current mosaic.
    /// </summary>
    public bool TryRestoreDroppedSelection()
    {
        var ids = DroppedSelection.TryTakeRestore();
        if (ids is null || ids.Count == 0)
        {
            return false;
        }

        RestoreSelectionByIds(ids, loadPreview: true);
        // No ids still visible under the current folder/filter — fall through to FileOp.
        if (_selection.Count == 0)
        {
            return false;
        }

        EnsureSelectMode(SelectMode.EntryFromPurposeful());
        RequestRestoreMosaicSelection?.Invoke();
        return true;
    }

    /// <summary>True selection size (not loaded-chunk <c>Assets.Where(IsSelected)</c>).</summary>
    public int SelectionCount => _selection.Count(s => !s.IsFolderHeader);

    public void SetSelection(IEnumerable<AssetItem> items, bool loadPreview = true)
    {
        ApplySelectionList(items.Where(item => !item.IsFolderHeader).ToList(), loadPreview);
    }

    /// <summary>
    /// ItemsView SelectionChanged only sees displayed tiles. In select mode,
    /// merge with previously selected tiles that are hidden under collapsed
    /// sections so a mosaic rebuild cannot clear them.
    /// </summary>
    public void SetSelectionFromItemsView(IEnumerable<AssetItem> visibleSelected, bool loadPreview = true)
    {
        var visible = visibleSelected.Where(item => !item.IsFolderHeader).ToList();
        if (!SelectMode.MergesHiddenSelection(IsSelectMode) || _mosaicOrder.Count == 0)
        {
            ApplySelectionList(visible, loadPreview);
            return;
        }

        var displayed = DisplayedForSelection(refresh: loadPreview);
        var merged = MosaicSelection.MergeVisibleWithHiddenSelected(
            visible,
            _selection,
            displayed,
            item => item.IsFolderHeader);
        ApplySelectionList(merged, loadPreview);
    }

    /// <summary>
    /// Re-resolve <c>_selection</c> from asset ids against the ordered mosaic
    /// (same instances after collapse filter; ids if tiles were remapped).
    /// Keeps the prior Shift-click anchor when it still lives in the ordered list.
    /// </summary>
    public void RestoreSelectionByIds(IReadOnlyCollection<string> ids, bool loadPreview = false)
    {
        IReadOnlyList<AssetItem> ordered = _mosaicOrder.Count > 0 ? _mosaicOrder : Assets;
        var resolved = MosaicSelection.ResolveByIds(
            ordered,
            ids,
            item => item.IsFolderHeader,
            item => item.Id);
        ApplySelectionList(resolved, loadPreview, keepAnchorIfInOrder: true);
    }

    private void ApplySelectionList(List<AssetItem> items, bool loadPreview, bool keepAnchorIfInOrder = false)
    {
        var priorAnchor = _selectionAnchor;
        var priorFocus = _selectionFocus;
        _selection = items;
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
        IReadOnlyList<AssetItem> ordered = _mosaicOrder.Count > 0 ? _mosaicOrder : Assets;
        // Collapse restore: do not move the Shift-click origin onto LastOrDefault
        // (often a hidden tile). Keep the prior anchor when it is still in order.
        _selectionAnchor = keepAnchorIfInOrder
            ? MosaicSelection.ResolveAnchorAfterRestore(priorAnchor, ordered, next)
            : next;
        _selectionFocus = keepAnchorIfInOrder
            ? MosaicSelection.ResolveAnchorAfterRestore(priorFocus, ordered, next)
            : next;
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

    /// <summary>
    /// Select / toggle / range a mosaic tile. <paramref name="addRange"/> (Ctrl+Shift)
    /// unions the anchor..item span with the existing selection and keeps the anchor.
    /// Shift range (<paramref name="range"/> without add) replaces and keeps the anchor.
    /// </summary>
    public void SelectAsset(AssetItem item, bool toggle, bool range = false, bool addRange = false)
    {
        if (item.IsFolderHeader)
        {
            return;
        }

        DroppedSelection.NotifyOtherAction();

        // Shift range needs an anchor: last plain/Ctrl click, or the focused / preview tile.
        if ((range || addRange) && _selectionAnchor is null)
        {
            _selectionAnchor = _selectionFocus
                ?? SelectedAsset
                ?? _selection.FirstOrDefault(a => !a.IsFolderHeader);
        }

        if ((range || addRange) && _selectionAnchor is not null)
        {
            var ordered = _mosaicOrder.Count > 0 ? (IReadOnlyList<AssetItem>)_mosaicOrder : Assets;
            var fromOrdered = MosaicSelection.IndexInOrdered(ordered, _selectionAnchor);
            var toOrdered = MosaicSelection.IndexInOrdered(ordered, item);
            if (fromOrdered >= 0 && toOrdered >= 0)
            {
                var flags = ordered.Select(asset => asset.IsFolderHeader).ToList();
                var priorAnchor = _selectionAnchor;
                if (addRange)
                {
                    var priorIndexes = new List<int>();
                    for (var i = 0; i < ordered.Count; i++)
                    {
                        if (!ordered[i].IsFolderHeader && ordered[i].IsSelected)
                        {
                            priorIndexes.Add(i);
                        }
                    }

                    // Also include anything in _selection that may be hidden.
                    for (var i = 0; i < ordered.Count; i++)
                    {
                        if (!ordered[i].IsFolderHeader
                            && _selection.Contains(ordered[i])
                            && !priorIndexes.Contains(i))
                        {
                            priorIndexes.Add(i);
                        }
                    }

                    var union = MosaicModifierSelect.UnionRange(
                        flags, priorIndexes, fromOrdered, toOrdered);
                    SetSelection(union.Select(i => ordered[i]), loadPreview: false);
                }
                else
                {
                    var indexes = MosaicModifierSelect.RangeIndexes(flags, fromOrdered, toOrdered);
                    SetSelection(indexes.Select(i => ordered[i]), loadPreview: false);
                }

                // Keep Shift anchor; Preview/focus follow the clicked (range end) tile.
                _selectionAnchor = priorAnchor;
                _selectionFocus = item;
                FinishSelectionChange(loadPreview: true);
                return;
            }
        }

        // Non-range: focus moves with the click target.
        SetSelectionFocus(item);

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
            // Prefer SetAssetSelected for Ctrl+pointer — toggle-from-current races
            // ItemsView Multiple's own pointer toggle (net zero). Kept for keyboard.
            SetAssetSelected(item, wantSelected: !item.IsSelected, loadPreview: true);
            return;
        }

        FinishSelectionChange(loadPreview: true);
    }

    /// <summary>
    /// Deterministic Ctrl+click: set membership from the model, move anchor.
    /// Does not flip-from-current (avoids double-toggle with ItemsView Multiple).
    /// </summary>
    public void SetAssetSelected(AssetItem item, bool wantSelected, bool loadPreview = true)
    {
        if (item.IsFolderHeader)
        {
            return;
        }

        DroppedSelection.NotifyOtherAction();
        SetSelectionFocus(item);
        _selectionAnchor = item;

        var displayed = DisplayedForSelection(refresh: loadPreview);
        var selected = new HashSet<AssetItem>();
        foreach (var asset in Assets)
        {
            if (asset.IsSelected && !ReferenceEquals(asset, item))
            {
                selected.Add(asset);
            }
        }

        foreach (var asset in _mosaicOrder)
        {
            if (!asset.IsFolderHeader
                && asset.IsSelected
                && !displayed.Contains(asset)
                && !ReferenceEquals(asset, item))
            {
                selected.Add(asset);
            }
        }

        item.IsSelected = wantSelected;
        if (wantSelected)
        {
            selected.Add(item);
        }

        _selection = selected.ToList();
        FinishSelectionChange(loadPreview);
    }

    /// <summary>
    /// Plain tile-body click while select mode is on. Modifier-entered → replace
    /// + exit; purposeful → toggle. Checkbox callers use <see cref="SelectAsset"/> toggle.
    /// </summary>
    public void ApplyTileBodyClick(AssetItem item)
    {
        if (item.IsFolderHeader)
        {
            return;
        }

        if (SelectMode.TileBodyClickReplacesAndExits(_selectEntryKind))
        {
            SelectAsset(item, toggle: false);
            IsSelectMode = false;
            _selectEntryKind = SelectEntryKind.None;
            RefreshSelectionSummary();
            RequestRestoreMosaicSelection?.Invoke();
            return;
        }

        SelectAsset(item, toggle: true);
        MaybeExitWhenSelectionEmpty();
        RequestRestoreMosaicSelection?.Invoke();
    }

    /// <summary>Update focus for Preview-follow without changing selection.</summary>
    public void SetSelectionFocus(AssetItem? item)
    {
        if (item is { IsFolderHeader: true })
        {
            return;
        }

        _selectionFocus = item;
        if (item is null || !IsSelectMode || _selection.Count <= 1)
        {
            return;
        }

        // Preview follows the focused tile while multi-select is active.
        if (!ReferenceEquals(SelectedAsset, item))
        {
            SelectedAsset = item;
        }
        else
        {
            _ = LoadPreviewAsync(item);
        }
    }

    private void FinishSelectionChange(bool loadPreview)
    {
        HasSelection = _selection.Count > 0;
        RefreshSelectionSummary();
        CanEditNotes = _selection.Count == 1;
        var focus = _selectionFocus is not null && _selection.Contains(_selectionFocus)
            ? _selectionFocus
            : null;
        var next = _selection.Count == 1
            ? _selection[0]
            : focus ?? _selection.LastOrDefault();
        if (!loadPreview)
        {
            MaybeExitWhenSelectionEmpty();
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

        MaybeExitWhenSelectionEmpty();
    }

    private void MaybeExitWhenSelectionEmpty()
    {
        if (!SelectMode.ExitsWhenSelectionEmpty(IsSelectMode, _selection.Count))
        {
            return;
        }

        IsSelectMode = false;
        _selectEntryKind = SelectEntryKind.None;
        RefreshSelectionSummary();
    }

    private static int IndexInOrdered(IReadOnlyList<AssetItem> ordered, AssetItem item) =>
        MosaicSelection.IndexInOrdered(ordered, item);

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
        _selectionFocus = null;
        InvalidateDisplayedSelectionCache();
        HasSelection = false;
        RefreshSelectionSummary();
        CanEditNotes = false;
        CanEditRating = false;
        PreviewRatingMixed = false;
        RatingCaption = SelectCommandBar.RatingCaption(false);
        MultiSelectDetail = "";
        SelectedAsset = null;
    }

    /// <summary>
    /// Select all mosaic assets including collapsed sections (same as Ctrl+A /
    /// <see cref="SelectAllMosaicAssets"/>).
    /// </summary>
    [RelayCommand]
    private void SelectAllVisible()
    {
        SelectAllMosaicAssets();
        RequestRestoreMosaicSelection?.Invoke();
    }

    /// <summary>
    /// Clear tiles and exit select mode (batch menu Clear + Esc).
    /// Done keeps the selection — see <see cref="ExitSelectMode"/>.
    /// </summary>
    [RelayCommand]
    private void ClearSelectionAndExit()
    {
        DroppedSelection.NotifyOtherAction();
        ClearSelection();
        if (SelectMode.ExitsSelectModeOnClear(IsSelectMode))
        {
            IsSelectMode = false;
            _selectEntryKind = SelectEntryKind.None;
        }

        RequestRestoreMosaicSelection?.Invoke();
    }

    /// <summary>
    /// Batch Keep: leave files on disk (same as Review Keep) and clear them
    /// from the selection so the action is real, not toast-only.
    /// </summary>
    [RelayCommand]
    private void KeepSelected()
    {
        var kept = _selection.Where(s => !s.IsFolderHeader).ToList();
        if (kept.Count == 0)
        {
            Notify("Select assets first.");
            return;
        }

        ClearSelection();
        RequestRestoreMosaicSelection?.Invoke();
        Notify(kept.Count == 1 ? "Kept 1 file." : $"Kept {kept.Count} files.");
    }

    /// <summary>
    /// Batch Reject: move each selected file into a sibling <c>_rejected</c>
    /// via <see cref="BatchRejectFlow"/>. Multi-folder runs fold into one
    /// journal batch when absorb is safe; otherwise stop with
    /// <see cref="BatchReject.InterruptedMessage"/> (Warning Notify, not Undo toast).
    /// </summary>
    [RelayCommand]
    private Task RejectSelectedAsync() =>
        ErrorReporter.RunAsync("Reject", Notify, async () =>
        {
            var selectedTiles = _selection.Where(s => !s.IsFolderHeader).ToList();
            var ids = selectedTiles
                .Where(s => !string.IsNullOrEmpty(s.Id))
                .Select(s => s.Id)
                .ToList();
            if (ids.Count == 0)
            {
                Notify("Select assets first.");
                return;
            }

            var collapsed = CountCollapsedAmongTargetIds(ids);
            if (SelectMode.NeedsBatchConfirm(ids.Count, collapsed))
            {
                if (RequestConfirm is null
                    || !await RequestConfirm(
                        "Reject",
                        BatchReject.ConfirmBody(ids.Count, collapsed),
                        "Reject"))
                {
                    return;
                }
            }

            var assets = await _catalog.GetAssetsByIdsAsync(ids);
            if (assets.Count == 0)
            {
                Notify("Select assets first.");
                return;
            }

            var already = assets.Count(a => ReviewReject.IsAlreadyRejected(a.Path));
            var movers = assets.Where(a => !ReviewReject.IsAlreadyRejected(a.Path)).ToList();
            if (movers.Count == 0)
            {
                Notify(BatchReject.ToastMessage(0, already, failed: 0));
                return;
            }

            var projectId = AppServices.CurrentProject.Id;
            var folderGroups = movers
                .GroupBy(a => ReviewReject.DestinationDir(a.Path) ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(g => new BatchRejectFlow.FolderGroup(
                    DestinationDir: g.Key,
                    ParentDir: ReviewReject.ParentDir(g.First().Path) ?? "",
                    Assets: g.ToList()))
                .ToList();
            var folderCount = folderGroups.Count(g => !string.IsNullOrEmpty(g.DestinationDir));

            var token = BeginBusyCancelable(
                movers.Count == 1 ? "Rejecting…" : $"Rejecting {movers.Count}…");
            BatchRejectFlow.RunResult run = new(0, 0, [], [], false, false, null);
            try
            {
                run = await BatchRejectFlow.RunAsync(
                    folderGroups,
                    async (parent, name) =>
                    {
                        var created = await _fileOps.CreateFolderAsync(parent, name, projectId);
                        if (created.Ok && !string.IsNullOrWhiteSpace(created.Path))
                        {
                            await RebuildFolderTreeAsync();
                        }

                        return new BatchRejectFlow.CreateFolderResult(
                            created.Ok,
                            created.Path,
                            created.Message);
                    },
                    async (assets, dest, absorb, prior, ct) =>
                    {
                        var result = await _fileOps.MoveAssetsAsync(
                            assets,
                            dest,
                            projectId,
                            cancellationToken: ct,
                            absorbCreatedFolder: absorb,
                            absorbPriorBatchId: prior);
                        if (result.Moves.Count > 0)
                        {
                            await ApplyMovesToUiAsync(result.Moves, result.Path);
                        }

                        return new BatchRejectFlow.MoveResult(
                            result.Moves.Count,
                            result.BatchId,
                            result.AbsorbedPriorBatch,
                            result.Message);
                    },
                    () => _catalog.GetUndoableFileOpsAsync(projectId),
                    token);
            }
            finally
            {
                await UiDispatch.RunAsync(() =>
                {
                    EndBusyCancelable();
                    if (!string.IsNullOrWhiteSpace(run.LastMessage))
                    {
                        StatusText = run.LastMessage!;
                    }
                });
            }

            await RefreshUndoStateAsync();
            var moved = run.Moved;
            var failed = run.Failed;
            var failNotes = run.FailNotes;
            var undoBatchIds = run.UndoBatchIds;
            if (run.Interrupted)
            {
                // Tip may be an intervening op — plain warning, not the Undo toast.
                Notify(
                    BatchReject.InterruptedMessage(moved),
                    Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                return;
            }

            var toast = BatchReject.ToastMessage(moved, already, failed, folderCount);
            if (run.Cancelled && moved == 0)
            {
                Notify("Reject cancelled.");
                return;
            }

            if (run.Cancelled)
            {
                toast = $"{toast.TrimEnd('.')} (cancelled).";
            }

            if (moved > 0 && undoBatchIds.Count > 0)
            {
                if (failNotes.Count > 0)
                {
                    toast = $"{toast.TrimEnd('.')} ({failNotes[0]}).";
                }

                ShowUndoToastFor(
                    toast,
                    offerUndo: true,
                    dismissSeconds: BatchReject.UndoToastSeconds(folderCount));
            }
            else
            {
                Notify(
                    failNotes.Count > 0 ? $"{toast.TrimEnd('.')} ({failNotes[0]})." : toast,
                    failed > 0
                        ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning
                        : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
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
