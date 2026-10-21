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
/// </summary>
public partial class LibraryViewModel
{
    private ReviewSession? _review;
    private List<AssetItem> _reviewItems = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReviewProgressText))]
    [NotifyPropertyChangedFor(nameof(ReviewSkippedLabel))]
    [NotifyPropertyChangedFor(nameof(ShowReviewSkipped))]
    [NotifyPropertyChangedFor(nameof(ShowReviewChrome))]
    [NotifyPropertyChangedFor(nameof(CanReviewDecide))]
    [NotifyPropertyChangedFor(nameof(CanReviewUndo))]
    public partial bool IsReviewMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReviewProgressText))]
    [NotifyPropertyChangedFor(nameof(ReviewSkippedLabel))]
    [NotifyPropertyChangedFor(nameof(ShowReviewSkipped))]
    [NotifyPropertyChangedFor(nameof(CanReviewDecide))]
    [NotifyPropertyChangedFor(nameof(CanReviewUndo))]
    public partial int ReviewRevision { get; set; }

    public string ReviewProgressText => _review?.ProgressText ?? "";

    public string ReviewSkippedLabel => _review?.ReviewSkippedLabel ?? "";

    public bool ShowReviewSkipped => _review?.CanReviewSkipped == true;

    public bool ShowReviewChrome => IsReviewMode && IsGalleryOverlayOpen;

    public bool CanReviewDecide => IsReviewMode && _review?.CanDecide == true;

    public bool CanReviewUndo => IsReviewMode && _review?.CanUndo == true;

    /// <summary>
    /// Start Review on the selection when tiles are selected; otherwise the
    /// visible mosaic (non-headers). Opens the overlay on the first item.
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
    private void EndReview()
    {
        _review = null;
        _reviewItems = [];
        IsReviewMode = false;
        BumpReview();
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
        if (_review is null || !_review.CanReviewSkipped)
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
            EndReview();
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
            if (_review is null || !_review.TryUndo(out var decision, out var assetId))
            {
                return;
            }

            if (decision == ReviewDecision.Reject)
            {
                var projectId = AppServices.CurrentProject.Id;
                var peek = await _fileOps.PeekUndoAsync(projectId);
                var result = await _fileOps.UndoLastAsync(projectId);
                if (peek?.Kind == FileOpKind.Move && result.Moves.Count > 0)
                {
                    await ApplyMovesToUiAsync(result.Moves, destinationDir: null);
                    OverlayGallery?.ApplyPathMoves(result.Moves);
                }
                else if (!result.Ok)
                {
                    Notify(result.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                }

                await RefreshUndoStateAsync();
            }

            BumpReview();
            SyncReviewGallery(preferId: assetId);
        });

    /// <summary>Overlay hotkeys X / K / S and Ctrl+Z while Review is active.</summary>
    public bool TryHandleReviewShortcut(bool controlDown, int keyCode)
    {
        if (!IsReviewMode)
        {
            return false;
        }

        if (ReviewHotkeys.IsUndoChord(controlDown, keyCode))
        {
            if (CanReviewUndo && ReviewUndoCommand.CanExecute(null))
            {
                ReviewUndoCommand.Execute(null);
            }

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
            if (_review is null || !_review.CanDecide || OverlayGallery?.Current is null)
            {
                return;
            }

            var current = OverlayGallery.Current;
            if (decision == ReviewDecision.Reject)
            {
                if (ReviewReject.IsAlreadyRejected(current.Path))
                {
                    Notify("Already in _rejected.");
                    return;
                }

                var dest = ReviewReject.DestinationDir(current.Path);
                var parent = ReviewReject.ParentDir(current.Path);
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
                    }
                }

                BeginBusy("Rejecting…");
                FileOpResult? result = null;
                try
                {
                    var assets = await _catalog.GetAssetsByIdsAsync([current.Id]);
                    result = await _fileOps.MoveAssetsAsync(
                        assets,
                        dest,
                        AppServices.CurrentProject.Id,
                        absorbCreatedFolder: absorb);
                    if (result.Moves.Count > 0)
                    {
                        await ApplyMovesToUiAsync(result.Moves, result.Path);
                        OverlayGallery.ApplyPathMoves(result.Moves);
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

                await RefreshUndoStateAsync();
                if (result is null || !result.Ok || result.Moves.Count == 0)
                {
                    if (result is not null)
                    {
                        Notify(result.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                    }

                    return;
                }
            }

            if (!_review.TryDecide(decision))
            {
                return;
            }

            BumpReview();
            if (_review.IsComplete)
            {
                return;
            }

            SyncReviewGallery();
        });

    private List<AssetItem> ReviewTargets()
    {
        if (_selection.Count > 0)
        {
            return _selection.Where(a => !a.IsFolderHeader).ToList();
        }

        return Assets.Where(a => !a.IsFolderHeader).ToList();
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
        ReviewRevision++;
        OnPropertyChanged(nameof(ReviewProgressText));
        OnPropertyChanged(nameof(ReviewSkippedLabel));
        OnPropertyChanged(nameof(ShowReviewSkipped));
        OnPropertyChanged(nameof(ShowReviewChrome));
        OnPropertyChanged(nameof(CanReviewDecide));
        OnPropertyChanged(nameof(CanReviewUndo));
    }

    partial void OnIsGalleryOverlayOpenChanged(bool value)
    {
        if (!value && IsReviewMode)
        {
            EndReview();
        }

        OnPropertyChanged(nameof(ShowReviewChrome));
    }
}
