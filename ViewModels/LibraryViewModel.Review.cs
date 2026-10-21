using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;

namespace Palace.ViewModels;

/// <summary>
/// Library: Review mode — swipe / hotkey keep·reject·skip over a queue.
/// Reject moves into sibling <c>_rejected</c>; Skip leaves the file and is undoable.
/// Touch swipe classify is <see cref="TouchGestures.ClassifyReviewSwipe"/> (Gesture PR).
/// Queue is frozen at start (filter/sort mid-review does not rebuild it).
/// </summary>
public partial class LibraryViewModel
{
    private ReviewSession? _review;
    private List<AssetItem> _reviewItems = [];
    private readonly ReviewApplyGate _reviewApply = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReviewProgressText))]
    [NotifyPropertyChangedFor(nameof(ReviewSkippedLabel))]
    [NotifyPropertyChangedFor(nameof(ShowReviewSkipped))]
    [NotifyPropertyChangedFor(nameof(ShowReviewChrome))]
    [NotifyPropertyChangedFor(nameof(ShowReviewDecideButtons))]
    [NotifyPropertyChangedFor(nameof(ShowReviewUndoButton))]
    [NotifyPropertyChangedFor(nameof(CanReviewDecide))]
    [NotifyPropertyChangedFor(nameof(CanReviewUndo))]
    public partial bool IsReviewMode { get; set; }

    public string ReviewProgressText => _review?.ProgressText ?? "";

    public string ReviewSkippedLabel => _review?.ReviewSkippedLabel ?? "";

    public bool ShowReviewSkipped => _review?.CanReviewSkipped == true;

    public bool ShowReviewChrome => IsReviewMode && IsGalleryOverlayOpen;

    /// <summary>
    /// Reject / Keep / Skip stay visible for the open item; disable while busy
    /// (no visibility blink).
    /// </summary>
    public bool ShowReviewDecideButtons =>
        IsReviewMode && IsGalleryOverlayOpen && _review is not null && !_review.IsComplete;

    public bool CanReviewDecide =>
        ShowReviewDecideButtons && !_reviewApply.IsBusy && _review?.CanDecide == true;

    /// <summary>Undo stays visible after the last item while history remains.</summary>
    public bool ShowReviewUndoButton =>
        IsReviewMode && IsGalleryOverlayOpen && _review?.CanUndo == true;

    public bool CanReviewUndo => ShowReviewUndoButton && !_reviewApply.IsBusy;

    /// <summary>
    /// Start Review on the selection when tiles are selected; otherwise the
    /// visible mosaic (non-headers, not already in <c>_rejected</c>). Opens the
    /// overlay on the first item. Queue is frozen for the session.
    /// </summary>
    [RelayCommand]
    private void StartReview()
    {
        var items = ReviewTargets();
        if (items.Count == 0)
        {
            Notify("Select assets or open a folder with files to review.");
            return;
        }

        _review = new ReviewSession(items.Select(i => i.Id));
        var order = _review.Queue
            .Select((id, index) => (id, index))
            .ToDictionary(x => x.id, x => x.index, StringComparer.Ordinal);
        _reviewItems = items
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .Where(i => order.ContainsKey(i.Id))
            .OrderBy(i => order[i.Id])
            .ToList();
        if (_reviewItems.Count == 0)
        {
            _review = null;
            Notify("Select assets or open a folder with files to review.");
            return;
        }

        IsReviewMode = true;
        OverlayGallery = new GalleryViewModel(_reviewItems, 0, _catalog);
        IsGalleryOverlayOpen = true;
        HydrateAfterLibraryOpen(_reviewItems[0]);
        BumpReview();
    }

    [RelayCommand]
    private void EndReview() => EndReviewCore(announce: false);

    /// <summary>
    /// Clears the session. Does <b>not</b> release <see cref="_reviewApply"/> —
    /// an in-flight Reject/Undo task’s <c>finally</c> owns that.
    /// </summary>
    private void EndReviewCore(bool announce)
    {
        var rejected = _review?.RejectedCount ?? 0;
        var hadSession = _review is not null;
        _review = null;
        _reviewItems = [];
        IsReviewMode = false;
        BumpReview();
        if (announce && hadSession)
        {
            Notify($"Review ended ({rejected} rejected)");
        }
    }

    [RelayCommand]
    private Task ReviewKeepAsync() => ApplyReviewDecisionAsync(ReviewDecision.Keep);

    [RelayCommand]
    private Task ReviewRejectAsync() => ApplyReviewDecisionAsync(ReviewDecision.Reject);

    [RelayCommand]
    private Task ReviewSkipAsync() => ApplyReviewDecisionAsync(ReviewDecision.Skip);

    [RelayCommand]
    private void ReviewSkipped()
    {
        if (_review is null || !_review.CanReviewSkipped || _reviewApply.IsBusy)
        {
            return;
        }

        var next = _review.StartSkippedPass();
        if (next is null)
        {
            return;
        }

        var byId = _reviewItems.ToDictionary(i => i.Id, StringComparer.Ordinal);
        _reviewItems = next.Queue
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();
        if (_reviewItems.Count == 0)
        {
            EndReviewCore(announce: false);
            return;
        }

        _review = next;
        OverlayGallery = new GalleryViewModel(_reviewItems, 0, _catalog);
        IsGalleryOverlayOpen = true;
        HydrateAfterLibraryOpen(_reviewItems[0]);
        BumpReview();
    }

    [RelayCommand]
    private Task ReviewUndoAsync() =>
        ErrorReporter.RunAsync("Undo review", Notify, async () =>
        {
            var session = _review;
            if (session is null || !_reviewApply.TryBeginBusy())
            {
                return;
            }

            try
            {
                BumpReview();
                var projectId = AppServices.CurrentProject.Id;
                var (rolledBack, entry, message) = await ReviewUndoFlow.TryUndoAsync(
                    session,
                    async batchId =>
                    {
                        var result = await _fileOps.UndoBatchAsync(projectId, batchId);
                        if (!ReferenceEquals(_review, session))
                        {
                            return new ReviewUndoFlow.DiskUndoResult(result.Ok, result.Message);
                        }

                        if (result.Ok && result.Moves.Count > 0)
                        {
                            await ApplyMovesToUiAsync(result.Moves, destinationDir: null);
                            OverlayGallery?.ApplyPathMoves(result.Moves);
                        }

                        await RefreshUndoStateAsync();
                        return new ReviewUndoFlow.DiskUndoResult(result.Ok, result.Message);
                    });

                if (!ReferenceEquals(_review, session))
                {
                    return;
                }

                if (!rolledBack)
                {
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        Notify(message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                    }

                    return;
                }

                BumpReview();
                SyncReviewGallery(preferId: entry?.AssetId);
            }
            finally
            {
                _reviewApply.End();
                BumpReview();
            }
        });

    /// <summary>
    /// Overlay hotkeys X / K / S and Ctrl+Z while Review is active.
    /// Ignores text focus and key auto-repeat for decision keys.
    /// Swallows Ctrl+Z while busy so Review undo cannot get stuck behind a
    /// fall-through global Undo.
    /// </summary>
    public bool TryHandleReviewShortcut(
        bool controlDown,
        int keyCode,
        bool focusIsTextInput = false,
        bool isRepeat = false)
    {
        if (!IsReviewMode || focusIsTextInput)
        {
            return false;
        }

        if (ReviewHotkeys.IsUndoChord(controlDown, keyCode))
        {
            if (_reviewApply.IsBusy)
            {
                return true;
            }

            if (!CanReviewUndo)
            {
                // Let the page Ctrl+Z undo the global file-op journal instead.
                return false;
            }

            if (ReviewUndoCommand.CanExecute(null))
            {
                ReviewUndoCommand.Execute(null);
            }

            return true;
        }

        if (isRepeat && ReviewHotkeys.IsDecisionKey(keyCode, controlDown))
        {
            return true;
        }

        var action = ReviewHotkeys.FromKey(keyCode, controlDown);
        if (!ReviewSession.TryToDecision(action, out var decision))
        {
            return false;
        }

        _ = ApplyReviewDecisionAsync(decision);
        return true;
    }

    public void CommitReviewSwipe(TouchGestures.SwipeAction action)
    {
        if (!CanReviewDecide || !ReviewSession.TryToDecision(action, out var decision))
        {
            return;
        }

        _ = ApplyReviewDecisionAsync(decision);
    }

    private Task ApplyReviewDecisionAsync(ReviewDecision decision) =>
        ErrorReporter.RunAsync("Review", Notify, async () =>
        {
            var session = _review;
            if (session is null
                || !_reviewApply.TryBegin(session.CurrentId, out var assetId))
            {
                return;
            }

            try
            {
                BumpReview();
                var item = FindReviewItem(assetId);
                if (item is null)
                {
                    return;
                }

                string? moveBatchId = null;
                if (decision == ReviewDecision.Reject)
                {
                    if (ReviewReject.IsAlreadyRejected(item.Path))
                    {
                        Notify("Already in _rejected.");
                        return;
                    }

                    var dest = ReviewReject.DestinationDir(item.Path);
                    var parent = ReviewReject.ParentDir(item.Path);
                    if (dest is null || parent is null)
                    {
                        Notify("Can't reject this file.");
                        return;
                    }

                    string? absorb = null;
                    if (!Directory.Exists(dest))
                    {
                        var created = await _fileOps.CreateFolderAsync(
                            parent,
                            ReviewReject.FolderName,
                            AppServices.CurrentProject.Id);
                        if (!ReferenceEquals(_review, session))
                        {
                            return;
                        }

                        if (!created.Ok || string.IsNullOrWhiteSpace(created.Path))
                        {
                            if (!Directory.Exists(dest))
                            {
                                Notify(created.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                                return;
                            }
                        }
                        else
                        {
                            absorb = created.Path;
                            await RebuildFolderTreeAsync();
                            if (!ReferenceEquals(_review, session))
                            {
                                return;
                            }
                        }
                    }

                    BeginBusy("Rejecting…");
                    FileOpResult? result = null;
                    try
                    {
                        var assets = await _catalog.GetAssetsByIdsAsync([assetId]);
                        if (!ReferenceEquals(_review, session))
                        {
                            return;
                        }

                        result = await _fileOps.MoveAssetsAsync(
                            assets,
                            dest,
                            AppServices.CurrentProject.Id,
                            absorbCreatedFolder: absorb);
                        if (!ReferenceEquals(_review, session))
                        {
                            return;
                        }

                        if (result.Moves.Count > 0)
                        {
                            await ApplyMovesToUiAsync(result.Moves, result.Path);
                            if (!ReferenceEquals(_review, session))
                            {
                                return;
                            }

                            OverlayGallery?.ApplyPathMoves(result.Moves);
                        }
                    }
                    finally
                    {
                        var status = result?.Message;
                        await UiDispatch.RunAsync(() =>
                        {
                            EndBusy();
                            if (!string.IsNullOrWhiteSpace(status))
                            {
                                StatusText = status!;
                            }
                        });
                    }

                    if (!ReferenceEquals(_review, session))
                    {
                        return;
                    }

                    await RefreshUndoStateAsync();
                    if (!ReferenceEquals(_review, session))
                    {
                        return;
                    }

                    if (result is null || !result.Ok || result.Moves.Count == 0)
                    {
                        if (result is not null)
                        {
                            Notify(result.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                        }

                        return;
                    }

                    moveBatchId = result.BatchId;
                }

                if (!ReferenceEquals(_review, session)
                    || !_reviewApply.StillOnCaptured(session.CurrentId))
                {
                    return;
                }

                if (!session.TryDecide(decision, moveBatchId))
                {
                    return;
                }

                BumpReview();
                if (session.IsComplete)
                {
                    return;
                }

                SyncReviewGallery();
            }
            finally
            {
                _reviewApply.End();
                BumpReview();
            }
        });

    private AssetItem? FindReviewItem(string assetId) =>
        _reviewItems.FirstOrDefault(i => string.Equals(i.Id, assetId, StringComparison.Ordinal));

    private List<AssetItem> ReviewTargets()
    {
        IEnumerable<AssetItem> source = _selection.Count > 0
            ? _selection
            : Assets;
        return source
            .Where(a => !a.IsFolderHeader && !ReviewReject.IsAlreadyRejected(a.Path))
            .ToList();
    }

    private void SyncReviewGallery(string? preferId = null)
    {
        if (_review is null || OverlayGallery is null)
        {
            return;
        }

        var id = preferId ?? _review.CurrentId;
        if (id is null)
        {
            return;
        }

        OverlayGallery.TryShowAsset(id);
        HydrateAfterLibraryOpen(OverlayGallery.Current);
    }

    private void BumpReview()
    {
        OnPropertyChanged(nameof(ReviewProgressText));
        OnPropertyChanged(nameof(ReviewSkippedLabel));
        OnPropertyChanged(nameof(ShowReviewSkipped));
        OnPropertyChanged(nameof(ShowReviewChrome));
        OnPropertyChanged(nameof(ShowReviewDecideButtons));
        OnPropertyChanged(nameof(ShowReviewUndoButton));
        OnPropertyChanged(nameof(CanReviewDecide));
        OnPropertyChanged(nameof(CanReviewUndo));
    }

    partial void OnIsGalleryOverlayOpenChanged(bool value)
    {
        if (!value && IsReviewMode)
        {
            EndReviewCore(announce: true);
        }

        OnPropertyChanged(nameof(ShowReviewChrome));
    }
}
