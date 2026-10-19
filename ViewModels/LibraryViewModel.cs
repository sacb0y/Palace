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
    private bool _suppressFilter;
    private int _filterEpoch;
    private int _previewEpoch;
    private bool _applyingPreview;
    private string? _previewAssetId;
    private int _busyDepth;
    private Dictionary<string, bool> _sourceIsCloud = new(StringComparer.Ordinal);
    private HashSet<string> _localSourceIds = new(StringComparer.Ordinal);
    private const int MosaicChunkSize = 80;

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
    public partial FolderNode? SelectedFolder { get; set; }

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

    [ObservableProperty]
    public partial MosaicSort MosaicSort { get; set; } = MosaicSortOrder.Default;

    public IReadOnlyList<string> SortLabels => MosaicSortOrder.Labels;

    public int MosaicSortIndex
    {
        get => MosaicSortOrder.IndexOf(MosaicSort);
        set
        {
            if (!MosaicSortOrder.ShouldApplyIndex(value, MosaicSort, out var next))
            {
                return;
            }

            MosaicSort = next;
        }
    }

    [ObservableProperty]
    public partial AssetItem? SelectedAsset { get; set; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Add a folder to start your library.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

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

    [ObservableProperty]
    public partial string TagSuggestionsSummary { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowOrganizePanel { get; set; }

    [ObservableProperty]
    public partial string InfoMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowInfo { get; set; }

    [ObservableProperty]
    public partial bool CanEditNotes { get; set; }

    [ObservableProperty]
    public partial GalleryViewModel? OverlayGallery { get; set; }

    [ObservableProperty]
    public partial bool IsGalleryOverlayOpen { get; set; }

    public bool CanUndo => true;

    partial void OnSelectedFolderChanged(FolderNode? value)
    {
        RebuildBreadcrumbs();
        if (!_suppressFilter)
        {
            _ = ApplyFilterAsync();
        }
    }

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
        if (_suppressFilter)
        {
            return;
        }

        if (AppServices.CurrentProject is { } project)
        {
            ApplicationData.Current.LocalSettings.Values[MosaicSortOrder.SettingsKey(project.Id)] =
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
            var tree = await Task.Run(() => BuildFolderNodes(sources, assets, treeSort));

            await UiDispatch.RunAsync(() =>
            {
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
            await ApplyFilterAsync();
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

    public void EndBusy()
    {
        if (Interlocked.Decrement(ref _busyDepth) <= 0)
        {
            Interlocked.Exchange(ref _busyDepth, 0);
            IsBusy = false;
        }
    }

    public void Notify(string message)
    {
        InfoMessage = message;
        ShowInfo = true;
        StatusText = message;
    }
}
