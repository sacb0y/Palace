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

public partial class LibraryViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly AccessService _access;
    private readonly ScanService _scan;
    private readonly OrganizeService _organize;
    private readonly ThumbnailService _thumbs;
    private readonly WatcherService _watchers;
    private readonly FileOpService _fileOps;
    private IReadOnlyList<Asset> _allAssets = [];
    private List<Tag> _tags = [];
    private List<TagMembership> _memberships = [];
    private List<TagPickItem> _tagPicks = [];
    private List<string> _recentTagIds = [];
    private List<AssetItem> _selection = [];
    private List<AssetItem> _previewTargets = [];
    private AssetItem? _selectionAnchor;
    /// <summary>Cached display set for drag-select / collapsed counts; cleared when Assets change.</summary>
    private HashSet<AssetItem>? _displayedSelectionCache;
    private bool _suppressFilter;
    private int _filterEpoch;
    private int _previewEpoch;
    private bool _applyingPreview;
    private string? _previewAssetId;
    private int _busyDepth;
    /// <summary>Busy-bar Cancel token for long reject/move loops (field so Cancel can fire).</summary>
    private CancellationTokenSource? _busyCancelCts;
    private Dictionary<string, bool> _sourceIsCloud = new(StringComparer.Ordinal);
    private HashSet<string> _localSourceIds = new(StringComparer.Ordinal);
    private string _sectionBrowsePath = "";
    private HashSet<string> _collapsedSections = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Full mosaic order including tiles under collapsed headers (view-only collapse).</summary>
    private List<AssetItem> _mosaicOrder = [];
    private const int MosaicChunkSize = 80;
    /// <summary>Collapse prefs save failure Notify once per app session (log every time).</summary>
    private static bool s_collapseSaveNotifyShown;

    /// <summary>
    /// LibraryPage captures scroll offset and returns a guard generation so a
    /// stale Ended after leave/return cannot decrement a newer toggle's depth.
    /// </summary>
    public Func<int>? SectionToggleStarting { get; set; }

    /// <summary>
    /// Fired right after <c>_filterEpoch</c> bumps for a section republish so the
    /// page can ignore MosaicRestoreReady from a superseded toggle.
    /// </summary>
    public Action<int>? SectionToggleEpochArmed { get; set; }

    /// <summary>
    /// Fired after mosaic chunks finish (and after selection restore on section
    /// republish) so the page can restore scroll/focus. Argument is the
    /// <c>_filterEpoch</c> for this populate — ignore when superseded.
    /// </summary>
    public Action<int>? MosaicRestoreReady { get; set; }

    /// <summary>
    /// Always fired when a section-toggle republish finishes (success, throw, or
    /// superseded). Args: filter epoch, guard generation from Starting.
    /// </summary>
    public Action<int, int>? SectionToggleEnded { get; set; }

    /// <summary>Section path to refocus after collapse toggle; cleared by the page.</summary>
    public string? PendingSectionFocusPath { get; set; }

    public LibraryViewModel(
        CatalogService catalog,
        AccessService access,
        ScanService scan,
        OrganizeService organize,
        ThumbnailService thumbs,
        WatcherService watchers,
        FileOpService fileOps)
    {
        _catalog = catalog;
        _access = access;
        _scan = scan;
        _organize = organize;
        _thumbs = thumbs;
        _watchers = watchers;
        _fileOps = fileOps;
    }

    public Func<Task<OrganizeChoice?>>? RequestOrganizeChoice { get; set; }
    public Func<string, string, string, Task<bool>>? RequestConfirm { get; set; }
    public Func<IReadOnlyList<Room>, Task<Room?>>? RequestPickRoom { get; set; }
    public Action? RequestFocusAssignTag { get; set; }
    public Action? RequestOpenAssignPanel { get; set; }
    public Action? RequestRestoreMosaicSelection { get; set; }
    public Action<GalleryViewModel>? RequestOpenGalleryWindow { get; set; }
    public Action? MosaicReset { get; set; }
    public Action? MosaicChunkAppended { get; set; }

    public ObservableCollection<FolderNode> FolderTree { get; } = [];
    public ObservableCollection<TagTreeNode> TagTree { get; } = [];
    public ObservableCollection<TagChipItem> BrowseTags { get; } = [];
    public ObservableCollection<PathCrumb> Breadcrumbs { get; } = [];

    public ObservableCollection<AssetItem> Assets { get; } = [];
    public ObservableCollection<AssignedTagItem> AssignedTags { get; } = [];
    public ObservableCollection<PromptSuggestion> Suggestions { get; } = [];
    public ObservableCollection<TagPickItem> AllTags { get; } = [];
    public ObservableCollection<TagPickItem> TagSuggestions { get; } = [];
    public ObservableCollection<TagChipItem> SelectedFilterTags { get; } = [];
    public ObservableCollection<TagChipItem> StarredAssignTags { get; } = [];
    public ObservableCollection<TagChipItem> RecentAssignTags { get; } = [];
    public ObservableCollection<TagChipItem> RecommendedAssignTags { get; } = [];
    public ObservableCollection<TagBoardGroup> AssignGroups { get; } = [];
    public ObservableCollection<OrganizePreviewItem> OrganizePreview { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccentScan))]
    public partial FolderNode? SelectedFolder { get; set; }

    /// <summary>Scan uses AccentButtonStyle when the library has no folder roots yet.</summary>
    public bool AccentScan => FolderTree.Count == 0;

    [ObservableProperty]
    public partial TagTreeNode? SelectedTag { get; set; }

    [ObservableProperty]
    public partial bool IsTagBrowse { get; set; }

    [ObservableProperty]
    public partial TagFilterMode TagFilterMode { get; set; } = TagFilterMode.All;

    [ObservableProperty]
    public partial bool HasTagFilters { get; set; }

    [ObservableProperty]
    public partial bool ShowTagFilterChips { get; set; }

    [ObservableProperty]
    public partial string TagFilterSummary { get; set; } = "";

    [ObservableProperty]
    public partial double MosaicRowHeight { get; set; } = 140;

    /// <summary>When true, folder browse shows one continuous mosaic (no section headers).</summary>
    [ObservableProperty]
    public partial bool FlattenSubfolders { get; set; }

    /// <summary>True while the current folder browse has subfolder section chrome.</summary>
    [ObservableProperty]
    public partial bool ShowFolderSectionsChrome { get; set; }

    /// <summary>Long scan notice text for the footer Details link; null when compact.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusDetails))]
    public partial string? StatusDetails { get; set; }

    public bool HasStatusDetails => !string.IsNullOrWhiteSpace(StatusDetails);

    [ObservableProperty]
    public partial MosaicSort MosaicSort { get; set; } = MosaicSortOrder.Default;

    /// <summary>Sort field choices (kept for tests / Tags migration; UI uses <see cref="SortButtonLabel"/>).</summary>
    public IReadOnlyList<string> SortLabels => MosaicSortOrder.FieldLabels;

    /// <summary>Index into <see cref="SortLabels"/>; picking a field starts in its natural direction.</summary>
    public int MosaicSortIndex
    {
        get => MosaicSortOrder.FieldIndexOf(MosaicSort);
        set
        {
            if (!MosaicSortOrder.ShouldApplyFieldIndex(value, MosaicSort, out var next))
            {
                return;
            }

            MosaicSort = next;
        }
    }

    public bool IsSortDescending => MosaicSortOrder.IsDescending(MosaicSort);

    /// <summary>"Newest first", "A to Z", … for the current direction.</summary>
    public string SortDirectionLabel => MosaicSortOrder.DirectionLabel(MosaicSort);

    public string SortDescendingLabel =>
        MosaicSortOrder.DirectionLabel(MosaicSortOrder.Compose(MosaicSortOrder.FieldOf(MosaicSort), descending: true));

    public string SortAscendingLabel =>
        MosaicSortOrder.DirectionLabel(MosaicSortOrder.Compose(MosaicSortOrder.FieldOf(MosaicSort), descending: false));

    /// <summary>DropDownButton face matching the folder-tree sort pattern.</summary>
    public string SortButtonLabel => MosaicSortOrder.ButtonLabel(MosaicSort);

    [RelayCommand]
    private void ToggleSortDirection() => MosaicSort = MosaicSortOrder.ToggleDirection(MosaicSort);

    [RelayCommand]
    private void SetMosaicSortField(string? fieldName)
    {
        if (!Enum.TryParse<MosaicSortField>(fieldName, ignoreCase: true, out var field))
        {
            return;
        }

        if (MosaicSortOrder.FieldOf(MosaicSort) == field)
        {
            return;
        }

        MosaicSort = MosaicSortOrder.Compose(field, MosaicSortOrder.DefaultDescending(field));
    }

    [RelayCommand]
    private void SetMosaicSortDescending(string? descending)
    {
        var wantDesc = string.Equals(descending, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(descending, "1", StringComparison.OrdinalIgnoreCase);
        if (IsSortDescending == wantDesc)
        {
            return;
        }

        MosaicSort = MosaicSortOrder.Compose(MosaicSortOrder.FieldOf(MosaicSort), wantDesc);
    }

    [ObservableProperty]
    public partial AssetItem? SelectedAsset { get; set; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Add a folder to start your library.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Busy bar shows Cancel when a cancellable op (batch Reject) is running.</summary>
    [ObservableProperty]
    public partial bool CanCancelBusy { get; set; }

    [ObservableProperty]
    public partial bool IsImagePreview { get; set; }

    [ObservableProperty]
    public partial bool IsVideoPreview { get; set; }

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial bool IsSelectMode { get; set; }

    [ObservableProperty]
    public partial string SelectionSummary { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowTagSelectedCta { get; set; }

    [ObservableProperty]
    public partial bool ShowPreviewPlaceholder { get; set; }

    [ObservableProperty]
    public partial AssetKind PreviewPlaceholderKind { get; set; }

    [ObservableProperty]
    public partial string? PreviewPath { get; set; }

    [ObservableProperty]
    public partial string? PreviewPrompt { get; set; }

    [ObservableProperty]
    public partial string? PreviewNegative { get; set; }

    [ObservableProperty]
    public partial string? PreviewModel { get; set; }

    [ObservableProperty]
    public partial string? PreviewFileName { get; set; }

    [ObservableProperty]
    public partial string? PreviewSeed { get; set; }

    [ObservableProperty]
    public partial bool ShowGeneration { get; set; }

    [ObservableProperty]
    public partial string? PreviewNotes { get; set; }

    [ObservableProperty]
    public partial double PreviewRating { get; set; }

    [ObservableProperty]
    public partial string? NewTagName { get; set; }

    [ObservableProperty]
    public partial string TagQuery { get; set; } = "";

    [ObservableProperty]
    public partial TagPickItem? SelectedPickTag { get; set; }

    [ObservableProperty]
    public partial string AssignedTagsSummary { get; set; } = "";

    /// <summary>Show the comma summary only when there are no chips ("No tags assigned").</summary>
    [ObservableProperty]
    public partial bool ShowAssignedTagsSummary { get; set; } = true;

    [ObservableProperty]
    public partial string TagSuggestionsSummary { get; set; } = "";

    /// <summary>Live region text when the assign flyout toggles a tag.</summary>
    [ObservableProperty]
    public partial string AssignAnnouncement { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowOrganizePanel { get; set; }

    [ObservableProperty]
    public partial string InfoMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowInfo { get; set; }

    /// <summary>Severity for <see cref="ShowInfo"/> (Warning for decode failures, Informational for tips).</summary>
    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Controls.InfoBarSeverity InfoSeverity { get; set; } =
        Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool CanEditNotes { get; set; }

    [ObservableProperty]
    public partial GalleryViewModel? OverlayGallery { get; set; }

    [ObservableProperty]
    public partial bool IsGalleryOverlayOpen { get; set; }

    /// <summary>True when the shared FileOp journal has something to reverse (Move, Organize, folders).</summary>
    public bool CanUndo => CanUndoFileOp;

    partial void OnSelectedFolderChanged(FolderNode? value)
    {
        StampFolderSelection(value);
        RebuildBreadcrumbs();
        if (!_suppressFilter)
        {
            _ = ApplyFilterAsync();
        }
    }

    private void StampFolderSelection(FolderNode? selected)
    {
        void Walk(IEnumerable<FolderNode> nodes)
        {
            foreach (var node in nodes)
            {
                node.IsSelected = selected is not null
                    && string.Equals(node.Path, selected.Path, StringComparison.OrdinalIgnoreCase);
                Walk(node.Children);
            }
        }

        Walk(FolderTree);
    }

    private void NotifyAccentScan() => OnPropertyChanged(nameof(AccentScan));

    partial void OnSelectedTagChanged(TagTreeNode? value)
    {
        RebuildBreadcrumbs();
    }

    partial void OnTagFilterModeChanged(TagFilterMode value)
    {
        if (!_suppressFilter && IsTagBrowse)
        {
            _ = ApplyFilterAsync();
        }
    }

    partial void OnIsTagBrowseChanged(bool value)
    {
        LoadMosaicSort();
        _suppressFilter = true;
        if (value)
        {
            SelectedFolder = null;
        }
        else
        {
            SelectedTag = null;
        }

        _suppressFilter = false;
        SyncFilterFlags();
        RebuildBreadcrumbs();
        _ = ApplyFilterAsync();
    }

    partial void OnMosaicSortChanged(MosaicSort value)
    {
        OnPropertyChanged(nameof(MosaicSortIndex));
        OnPropertyChanged(nameof(IsSortDescending));
        OnPropertyChanged(nameof(SortDirectionLabel));
        OnPropertyChanged(nameof(SortDescendingLabel));
        OnPropertyChanged(nameof(SortAscendingLabel));
        OnPropertyChanged(nameof(SortButtonLabel));
        if (_suppressFilter)
        {
            return;
        }

        if (AppServices.CurrentProject is { } project)
        {
            ApplicationData.Current.LocalSettings.Values[MosaicSortOrder.SettingsKey(project.Id, MosaicSortViewNow)] =
                MosaicSortOrder.Persist(value);
        }

        _ = ApplyFilterAsync();
    }

    partial void OnMosaicRowHeightChanged(double value)
    {
        var clamped = Math.Clamp(value, 96, 280);
        if (Math.Abs(clamped - value) > 0.01)
        {
            MosaicRowHeight = clamped;
            return;
        }

        if (AppServices.CurrentProject is { } project)
        {
            ApplicationData.Current.LocalSettings.Values[RowHeightKey(project.Id)] = clamped;
        }
    }

    partial void OnFlattenSubfoldersChanged(bool value)
    {
        if (_suppressFilter || string.IsNullOrEmpty(_sectionBrowsePath))
        {
            return;
        }

        SaveFlatten(_sectionBrowsePath, value);
        _ = ApplyFilterAsync();
    }

    partial void OnSelectedAssetChanged(AssetItem? value) => _ = LoadPreviewAsync(value);

    partial void OnPreviewNotesChanged(string? value)
    {
        if (!PreviewPersist.ShouldWriteNotes(_applyingPreview, CanEditNotes, SelectedAsset?.Id, _previewAssetId)
            || SelectedAsset is null)
        {
            return;
        }

        _ = _catalog.UpdateNotesAndRatingAsync(SelectedAsset.Id, value, (int)Math.Round(PreviewRating));
    }

    partial void OnPreviewRatingChanged(double value)
    {
        if (!PreviewPersist.ShouldWriteNotes(_applyingPreview, CanEditNotes, SelectedAsset?.Id, _previewAssetId)
            || SelectedAsset is null)
        {
            return;
        }

        _ = _catalog.UpdateNotesAndRatingAsync(SelectedAsset.Id, PreviewNotes, (int)Math.Round(value));
    }

    partial void OnIsSelectModeChanged(bool value) => RefreshSelectionSummary();

    public async Task LoadAsync()
    {
        BeginBusy("Loading…");
        try
        {
            LoadRowHeight();
            LoadMosaicSort();
            LoadRecentTags();
            LoadMoveRecents();
            LoadFolderSort();
            await RefreshQuietAsync();
            await RefreshUndoStateAsync();
        }
        finally
        {
            EndBusy();
        }
    }

    public async Task RefreshQuietAsync(string? preferredFolderPath = null)
    {
        BeginBusy("Loading…");
        try
        {
            var projectId = AppServices.CurrentProject.Id;
            var sources = await _catalog.GetSourceFoldersAsync(projectId);
            StampSourceCloud(sources);
            var assets = await _catalog.GetAssetsAsync(projectId: projectId);
            var tags = await _catalog.GetTagsAsync();
            var memberships = await _catalog.GetMembershipsAsync();
            var selectedPath = preferredFolderPath ?? SelectedFolder?.Path;
            var selectedTagId = SelectedTag?.TagId;
            var filterIds = SelectedFilterTags.Select(t => t.TagId).ToList();
            var pickItems = BuildTagPicks(tags, memberships);
            var treeSort = FolderSort;
            var refresh = Interlocked.Increment(ref _folderRefreshGeneration);
            var generation = Interlocked.Increment(ref _folderTreeGeneration);
            var tree = await Task.Run(() => BuildFolderNodes(sources, assets, treeSort));
            var resort = false;
            var stale = false;

            await UiDispatch.RunAsync(() =>
            {
                // A newer refresh is on its way with fresher catalog data: drop this one.
                if (refresh != Volatile.Read(ref _folderRefreshGeneration))
                {
                    stale = true;
                    return;
                }

                // The sort changed, or a tree rebuild (built from the old assets) started while this
                // tree was built: show it now, then rebuild from the fresh assets in the current order.
                resort = treeSort != FolderSort || generation != Volatile.Read(ref _folderTreeGeneration);
                _allAssets = assets;
                _tags = tags.ToList();
                _memberships = memberships.ToList();
                _suppressFilter = true;
                ReplaceFolderTree(tree);
                TagTreeBuilder.Replace(TagTree, tags, memberships);
                ApplyLibraryTreeColors();
                RestoreFilterSelection(filterIds);
                ReplaceTagPicks(pickItems);

                if (IsTagBrowse)
                {
                    SelectedTag = selectedTagId is null ? null : TagTreeBuilder.Find(TagTree, selectedTagId);
                    SelectedFolder = null;
                }
                else
                {
                    SelectedTag = null;
                    SelectedFolder = selectedPath is null ? null : FindNode(FolderTree, selectedPath);
                }

                _suppressFilter = false;
                RebuildBreadcrumbs();
            });
            if (stale)
            {
                return;
            }

            await ApplyFilterAsync();
            if (resort)
            {
                await RebuildFolderTreeAsync();
            }
        }
        finally
        {
            EndBusy();
        }
    }

    public void BeginBusy(string? status = null)
    {
        if (status is not null)
        {
            StatusText = status;
        }

        Interlocked.Increment(ref _busyDepth);
        IsBusy = true;
    }

    /// <summary>
    /// Begin busy with a Cancel button on the library progress bar. Callers must
    /// <see cref="EndBusyCancelable"/> in finally (clears the token).
    /// </summary>
    public CancellationToken BeginBusyCancelable(string? status = null)
    {
        _busyCancelCts?.Cancel();
        _busyCancelCts?.Dispose();
        _busyCancelCts = new CancellationTokenSource();
        CanCancelBusy = true;
        BeginBusy(status);
        return _busyCancelCts.Token;
    }

    public void EndBusyCancelable()
    {
        CanCancelBusy = false;
        var cts = _busyCancelCts;
        _busyCancelCts = null;
        cts?.Dispose();
        EndBusy();
    }

    [RelayCommand]
    private void CancelBusy() => _busyCancelCts?.Cancel();

    public void EndBusy()
    {
        if (Interlocked.Decrement(ref _busyDepth) <= 0)
        {
            Interlocked.Exchange(ref _busyDepth, 0);
            IsBusy = false;
            CanCancelBusy = false;
            // Drop a stale Cancel token so CancelBusy cannot fire after EndBusy.
            var cts = _busyCancelCts;
            _busyCancelCts = null;
            cts?.Dispose();
        }
    }

    public void Notify(string message) =>
        Notify(message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);

    public void Notify(string message, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity)
    {
        // One toast slot: a status InfoBar replaces the undo toast.
        ShowUndoToast = false;
        InfoMessage = message;
        InfoSeverity = severity;
        ShowInfo = true;
        // Persistent status (counts, scan) stays on StatusText; don't mirror tips into it.
        if (severity is Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning
            or Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error)
        {
            StatusText = message;
        }
    }

}
