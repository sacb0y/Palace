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
    private int _busyDepth;
    private const int MosaicChunkSize = 80;

    public LibraryViewModel(
        CatalogService catalog,
        AccessService access,
        ScanService scan,
        OrganizeService organize,
        ThumbnailService thumbs,
        WatcherService watchers)
    {
        _catalog = catalog;
        _access = access;
        _scan = scan;
        _organize = organize;
        _thumbs = thumbs;
        _watchers = watchers;
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
        if (!CanEditNotes || SelectedAsset is null)
        {
            return;
        }

        _ = _catalog.UpdateNotesAndRatingAsync(SelectedAsset.Id, value, (int)Math.Round(PreviewRating));
    }

    partial void OnPreviewRatingChanged(double value)
    {
        if (!CanEditNotes || SelectedAsset is null)
        {
            return;
        }

        _ = _catalog.UpdateNotesAndRatingAsync(SelectedAsset.Id, PreviewNotes, (int)Math.Round(value));
    }

    public void SetSelection(IEnumerable<AssetItem> items)
    {
        _selection = items.ToList();
        foreach (var asset in Assets)
        {
            asset.IsSelected = _selection.Contains(asset);
        }

        HasSelection = _selection.Count > 0;
        CanEditNotes = _selection.Count == 1;
        var next = _selection.Count == 1 ? _selection[0] : _selection.LastOrDefault();
        _selectionAnchor = next;
        if (!ReferenceEquals(SelectedAsset, next))
        {
            SelectedAsset = next;
        }
        else
        {
            _ = LoadPreviewAsync(next);
        }
    }

    public void SelectAsset(AssetItem item, bool toggle, bool range = false)
    {
        if (range && _selectionAnchor is not null)
        {
            var list = Assets.ToList();
            var from = list.IndexOf(_selectionAnchor);
            var to = list.IndexOf(item);
            if (from >= 0 && to >= 0)
            {
                var lo = Math.Min(from, to);
                var hi = Math.Max(from, to);
                for (var i = 0; i < list.Count; i++)
                {
                    list[i].IsSelected = i >= lo && i <= hi;
                }

                _selection = list.Where(a => a.IsSelected).ToList();
                HasSelection = _selection.Count > 0;
                CanEditNotes = _selection.Count == 1;
                if (!ReferenceEquals(SelectedAsset, item))
                {
                    SelectedAsset = item;
                }
                else
                {
                    _ = LoadPreviewAsync(item);
                }

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

    public async Task LoadAsync()
    {
        BeginBusy("Loading…");
        try
        {
            LoadRowHeight();
            LoadRecentTags();
            await RefreshQuietAsync();
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
            var assets = await _catalog.GetAssetsAsync(projectId: projectId);
            var tags = await _catalog.GetTagsAsync();
            var memberships = await _catalog.GetMembershipsAsync();
            var selectedPath = preferredFolderPath ?? SelectedFolder?.Path;
            var selectedTagId = SelectedTag?.TagId;
            var filterIds = SelectedFilterTags.Select(t => t.TagId).ToList();
            var pickItems = BuildTagPicks(tags, memberships);
            var tree = BuildFolderNodes(sources, assets);

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

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var folder = await _access.PickFolderAsync();
        if (folder is null)
        {
            return;
        }

        var existing = (await _catalog.GetSourceFoldersAsync(AppServices.CurrentProject.Id))
            .FirstOrDefault(s => string.Equals(s.Path, folder.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Notify("That folder is already in the library.");
            return;
        }

        var source = new SourceFolder
        {
            Id = PalaceDb.NewId(),
            Path = folder.Path,
            AccessToken = _access.Remember(folder),
            FolderTemplate = "{Character}/{tags:2}",
            FileTemplate = "{Character}-{tags}.{ext}",
            ProjectId = AppServices.CurrentProject.Id
        };
        await _catalog.UpsertSourceFolderAsync(source);
        await ScanFolderAsync(source);
        await _watchers.RestartAsync();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        BeginBusy("Scanning…");
        try
        {
            var report = await _scan.ScanAllAsync(
                new Progress<string>(p => StatusText = $"Scanning {Path.GetFileName(p)}"),
                projectId: AppServices.CurrentProject.Id);
            await RefreshQuietAsync();
            StatusText = $"Indexed {report.Added} new, {report.Updated} updated, {report.Orphaned} missing.";
        }
        finally
        {
            EndBusy();
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await ApplyFilterAsync();
    }

    [RelayCommand]
    private async Task AssignFromQueryAsync()
    {
        var targets = TagTargets();
        if (targets.Count == 0 || string.IsNullOrWhiteSpace(TagQuery))
        {
            return;
        }

        var names = TagNameList.Split(TagQuery);
        if (names.Count == 0)
        {
            return;
        }

        var assigned = new List<string>();
        foreach (var query in names)
        {
            var match = FindTagPick(query);
            string tagId;
            string tagName;
            if (match is null)
            {
                var existing = await _catalog.FindTagByNameAsync(query);
                var created = existing ?? await _catalog.CreateTagAsync(query);
                tagId = created.Id;
                tagName = created.Name;
            }
            else
            {
                tagId = match.TagId;
                tagName = match.Name;
            }

            await AssignToSelectionAsync(targets, tagId, tagName, TagSource.Manual);
            assigned.Add(tagName);
        }

        await UiDispatch.RunAsync(() =>
        {
            TagQuery = "";
            SelectedPickTag = null;
            if (assigned.Count > 1)
            {
                StatusText = targets.Count == 1
                    ? $"Tagged 1 image with {string.Join(", ", assigned)}"
                    : $"Tagged {targets.Count} images with {string.Join(", ", assigned)}";
            }
        });
        await LoadPreviewAsync(SelectedAsset);
        await ReloadTagCatalogAsync();
    }

    [RelayCommand]
    private async Task CreateAndAssignTagAsync()
    {
        var targets = TagTargets();
        if (targets.Count == 0 || string.IsNullOrWhiteSpace(NewTagName))
        {
            return;
        }

        var names = TagNameList.Split(NewTagName);
        if (names.Count == 0)
        {
            return;
        }

        foreach (var name in names)
        {
            var existing = await _catalog.FindTagByNameAsync(name);
            var tag = existing ?? await _catalog.CreateTagAsync(name);
            await AssignToSelectionAsync(targets, tag.Id, tag.Name, TagSource.Manual);
        }

        await UiDispatch.RunAsync(() => NewTagName = "");
        await LoadPreviewAsync(SelectedAsset);
        await ReloadTagCatalogAsync();
    }

    [RelayCommand]
    private async Task RemoveAssignedTagAsync(AssignedTagItem? item)
    {
        var targets = TagTargets();
        if (targets.Count == 0 || item is null)
        {
            return;
        }

        foreach (var asset in targets)
        {
            await _catalog.RemoveTagAsync(asset.Id, item.TagId);
        }

        StatusText = targets.Count == 1
            ? $"Removed {item.TagName} from 1 image"
            : $"Removed {item.TagName} from {targets.Count} images";
        await LoadPreviewAsync(SelectedAsset);
    }

    [RelayCommand]
    private async Task AcceptSuggestionAsync(PromptSuggestion? suggestion)
    {
        var targets = TagTargets();
        if (targets.Count == 0 || suggestion is null)
        {
            return;
        }

        string tagId;
        string tagName = suggestion.Token;
        if (suggestion.ExistingTagId is not null)
        {
            tagId = suggestion.ExistingTagId;
            tagName = suggestion.ExistingTagName ?? suggestion.Token;
        }
        else
        {
            var created = await _catalog.CreateTagAsync(suggestion.Token);
            tagId = created.Id;
            tagName = created.Name;
        }

        await AssignToSelectionAsync(targets, tagId, tagName, TagSource.Prompt);
        await LoadPreviewAsync(SelectedAsset);
        await ReloadTagCatalogAsync();
    }

    [RelayCommand]
    private async Task PreviewOrganizeAsync()
    {
        var targets = await SelectedAssetsAsync();
        if (targets.Count == 0)
        {
            Notify("Select assets or a folder to organize.");
            return;
        }

        if (RequestOrganizeChoice is null)
        {
            return;
        }

        var choice = await RequestOrganizeChoice();
        if (choice is null)
        {
            return;
        }

        var preview = await _organize.DryRunAsync(targets, choice);
        OrganizePreview.Clear();
        foreach (var item in preview)
        {
            OrganizePreview.Add(item);
        }

        ShowOrganizePanel = true;
        StatusText = $"Organize preview: {preview.Count} items.";
    }

    [RelayCommand]
    private async Task ApplyOrganizeAsync()
    {
        if (OrganizePreview.Count == 0)
        {
            return;
        }

        BeginBusy("Organizing…");
        try
        {
            var applied = await _organize.ApplyAsync(OrganizePreview.ToList());
            await RefreshQuietAsync();
            StatusText = $"Moved {applied} files. Last batch is undoable.";
        }
        finally
        {
            EndBusy();
        }
    }

    [RelayCommand]
    private async Task UndoOrganizeAsync()
    {
        BeginBusy("Undoing…");
        try
        {
            var count = await _organize.UndoLastAsync();
            await RefreshQuietAsync();
            StatusText = count == 0 ? "Nothing to undo." : $"Restored {count} files.";
            ShowOrganizePanel = false;
        }
        finally
        {
            EndBusy();
        }
    }

    [RelayCommand]
    private async Task AddSelectionToRoomAsync()
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
    }

    [RelayCommand]
    private void OpenOverlay(AssetItem? item)
    {
        if (!TryCreateGallery(item, out var gallery))
        {
            return;
        }

        OverlayGallery = gallery;
        IsGalleryOverlayOpen = true;
        HydrateAfterLibraryOpen(item ?? gallery.Current);
    }

    [RelayCommand]
    private void CloseOverlay()
    {
        IsGalleryOverlayOpen = false;
        OverlayGallery = null;
    }

    [RelayCommand]
    private void OpenInNewWindow(AssetItem? item)
    {
        if (RequestOpenGalleryWindow is null || !TryCreateGallery(item, out var gallery))
        {
            return;
        }

        RequestOpenGalleryWindow(gallery);
        HydrateAfterLibraryOpen(item ?? gallery.Current);
    }

    [RelayCommand]
    private async Task ShowInExplorerAsync()
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
    }

    [RelayCommand]
    private async Task CopyFilesAsync()
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
    }

    [RelayCommand]
    private async Task CopyPathAsync()
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
    }

    [RelayCommand]
    private async Task MoveToFolderAsync()
    {
        var targets = await SelectedAssetsAsync();
        if (targets.Count == 0)
        {
            Notify("Select assets first.");
            return;
        }

        var folder = await _access.PickFolderAsync();
        if (folder is null)
        {
            return;
        }

        var destRoot = folder.Path;
        Directory.CreateDirectory(destRoot);
        var moved = 0;
        foreach (var asset in targets)
        {
            if (!CloudFile.Exists(asset.Path))
            {
                continue;
            }

            var dest = Path.Combine(destRoot, Path.GetFileName(asset.Path));
            if (string.Equals(Path.GetFullPath(asset.Path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            dest = PathSafe.UniquePath(dest);
            try
            {
                File.Move(asset.Path, dest);
                await _catalog.UpdateAssetPathAsync(asset.Id, dest);
                moved++;
            }
            catch (Exception ex)
            {
                await _catalog.SetOrganizeErrorAsync(asset.Id, ex.Message);
            }
        }

        CloseOverlay();
        await RefreshQuietAsync();
        StatusText = moved == 0 ? "No files moved." : $"Moved {moved} file(s).";
    }

    [RelayCommand]
    private async Task DeleteFilesAsync()
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

        var removed = new List<string>();
        foreach (var asset in targets)
        {
            if (CloudFile.Exists(asset.Path))
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(asset.Path);
                    await file.DeleteAsync(StorageDeleteOption.Default);
                }
                catch (Exception ex)
                {
                    Notify($"Could not delete {asset.FileName}: {ex.Message}");
                    continue;
                }
            }

            _thumbs.TryDelete(asset.ContentHash);
            removed.Add(asset.Id);
        }

        if (removed.Count > 0)
        {
            await _catalog.DeleteAssetsAsync(removed);
        }

        CloseOverlay();
        await RefreshQuietAsync();
        await AppServices.Rooms.RefreshAsync();
        Notify(removed.Count == 0 ? "Nothing deleted." : $"Deleted {removed.Count} file(s).");
    }

    [RelayCommand]
    private void FocusAssignTag()
    {
        RequestFocusAssignTag?.Invoke();
        RequestOpenAssignPanel?.Invoke();
        RebuildAssignPanel();
    }

    public void ApplySingleTagFilter(string tagId, string name)
    {
        if (string.IsNullOrEmpty(tagId))
        {
            return;
        }

        _suppressFilter = true;
        var needBrowse = !IsTagBrowse;
        try
        {
            if (TagFilterMode == TagFilterMode.None)
            {
                TagFilterMode = TagFilterMode.Any;
            }

            var node = TagTreeBuilder.Find(TagTree, tagId)
                ?? new TagTreeNode { TagId = tagId, Name = name };
            SelectedTag = node;
            RestoreFilterSelection([tagId]);
            if (SelectedFilterTags.Count == 0)
            {
                SelectedFilterTags.Add(ToFilterChip(node));
                StampFilterSelected(TagTree, [tagId]);
            }

            SyncFilterFlags();
            RebuildBreadcrumbs();
        }
        finally
        {
            _suppressFilter = false;
        }

        if (needBrowse)
        {
            IsTagBrowse = true;
            return;
        }

        _ = ApplyFilterAsync();
    }

    public void ToggleFilterTag(TagTreeNode? node)
    {
        if (node?.TagId is null)
        {
            return;
        }

        var existing = SelectedFilterTags.FirstOrDefault(t => t.TagId == node.TagId);
        if (existing is not null)
        {
            SelectedFilterTags.Remove(existing);
            node.IsFilterSelected = false;
        }
        else
        {
            SelectedFilterTags.Add(ToFilterChip(node));
            node.IsFilterSelected = true;
            SelectedTag = node;
        }

        SyncFilterFlags();
        RebuildBreadcrumbs();
        if (!_suppressFilter)
        {
            _ = ApplyFilterAsync();
        }
    }

    [RelayCommand]
    private void RemoveFilterTag(TagChipItem? chip)
    {
        if (chip is null)
        {
            return;
        }

        SelectedFilterTags.Remove(chip);
        var node = TagTreeBuilder.Find(TagTree, chip.TagId);
        if (node is not null)
        {
            node.IsFilterSelected = false;
        }

        if (SelectedTag?.TagId == chip.TagId)
        {
            SelectedTag = SelectedFilterTags.Count == 0
                ? null
                : TagTreeBuilder.Find(TagTree, SelectedFilterTags[^1].TagId);
        }

        SyncFilterFlags();
        RebuildBreadcrumbs();
        _ = ApplyFilterAsync();
    }

    [RelayCommand]
    private async Task ToggleAssignChipAsync(TagChipItem? chip)
    {
        var targets = TagTargets();
        if (chip is null || targets.Count == 0)
        {
            return;
        }

        if (chip.IsAssigned && !chip.IsPartial)
        {
            await RemoveAssignedTagAsync(new AssignedTagItem { TagId = chip.TagId, TagName = chip.Name });
            return;
        }

        await AssignToSelectionAsync(targets, chip.TagId, chip.Name, TagSource.Manual);
        await LoadPreviewAsync(SelectedAsset);
        RebuildAssignPanel();
    }

    public void NavigateBreadcrumb(PathCrumb crumb)
    {
        if (IsTagBrowse)
        {
            SelectedFilterTags.Clear();
            if (crumb.Path.Length > 0)
            {
                var tagNode = TagTreeBuilder.Find(TagTree, crumb.Path);
                if (tagNode?.TagId is not null)
                {
                    SelectedFilterTags.Add(ToFilterChip(tagNode));
                    SelectedTag = tagNode;
                }
            }
            else
            {
                SelectedTag = null;
            }

            RestoreFilterSelection(SelectedFilterTags.Select(t => t.TagId).ToList());
            SyncFilterFlags();
            RebuildBreadcrumbs();
            _ = ApplyFilterAsync();
            return;
        }

        if (string.IsNullOrEmpty(crumb.Path))
        {
            SelectedFolder = null;
            return;
        }

        var node = FindNode(FolderTree, crumb.Path);
        if (node is not null)
        {
            SelectedFolder = node;
        }
    }

    private async Task ScanFolderAsync(SourceFolder source)
    {
        BeginBusy($"Scanning {source.Path}…");
        try
        {
            var report = await _scan.ScanSourceAsync(source);
            await RefreshQuietAsync(source.Path);
            StatusText = $"Indexed {report.Added} files from {Path.GetFileName(source.Path)}.";
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task ApplyFilterAsync()
    {
        BeginBusy("Loading…");
        var epoch = Interlocked.Increment(ref _filterEpoch);
        try
        {
            var projectId = AppServices.CurrentProject.Id;
            var search = SearchQuery;
            var folderName = SelectedFolder?.Name;
            var filterIds = SelectedFilterTags.Select(t => t.TagId).ToList();
            var filterNames = SelectedFilterTags.Select(t => t.Name).ToList();
            var tagBrowse = IsTagBrowse && filterIds.Count > 0;
            var folderPath = !IsTagBrowse ? SelectedFolder?.Path : null;
            var matchMode = TagFilterMode;

            IReadOnlyList<Asset> assets;
            if (IsTagBrowse && filterIds.Count > 0)
            {
                var memberships = _memberships.Count > 0 ? _memberships : (await _catalog.GetMembershipsAsync()).ToList();
                var sets = TagFilter.ExpandEach(filterIds, memberships);
                assets = await _catalog.GetAssetsForTagFilterAsync(sets, matchMode, projectId);
            }
            else if (folderPath is not null)
            {
                assets = await _catalog.GetAssetsAsync(folderPrefix: folderPath, projectId: projectId);
            }
            else
            {
                assets = _allAssets.Count > 0
                    ? _allAssets
                    : await _catalog.GetAssetsAsync(projectId: projectId);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var hits = await _catalog.SearchAsync(search, projectId);
                var set = hits.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
                assets = assets.Where(a => set.Contains(a.Id)).ToList();
            }

            if (epoch != _filterEpoch)
            {
                return;
            }

            var items = await Task.Run(() => assets.Select(ToItem).ToList());
            if (epoch != _filterEpoch)
            {
                return;
            }

            var joiner = matchMode == TagFilterMode.All
                ? " + "
                : matchMode == TagFilterMode.None ? " except " : " or ";
            var doneStatus = !string.IsNullOrWhiteSpace(search)
                ? $"{items.Count} search results"
                : tagBrowse
                    ? $"{items.Count} tagged {string.Join(joiner, filterNames)}"
                    : folderPath is not null
                        ? $"{items.Count} in {folderName}"
                        : items.Count == 0
                            ? "Add a folder to start your library."
                            : $"{items.Count} assets";

            await PopulateMosaicAsync(items, epoch, doneStatus);
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task PopulateMosaicAsync(IReadOnlyList<AssetItem> items, int epoch, string doneStatus)
    {
        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _filterEpoch)
            {
                return;
            }

            ClearSelection();
            Assets.Clear();
            MosaicReset?.Invoke();
        });

        var total = items.Count;
        for (var i = 0; i < total; i += MosaicChunkSize)
        {
            if (epoch != _filterEpoch)
            {
                return;
            }

            var end = Math.Min(i + MosaicChunkSize, total);
            await UiDispatch.RunAsync(() =>
            {
                if (epoch != _filterEpoch)
                {
                    return;
                }

                for (var n = i; n < end; n++)
                {
                    Assets.Add(items[n]);
                }

                StatusText = $"Showing {end} of {total}";
                MosaicChunkAppended?.Invoke();
            });

            await UiDispatch.YieldAsync();
        }

        if (epoch != _filterEpoch)
        {
            return;
        }

        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _filterEpoch)
            {
                return;
            }

            StatusText = doneStatus;
        });
    }

    private async Task LoadPreviewAsync(AssetItem? item)
    {
        if (item is null)
        {
            await UiDispatch.RunAsync(ClearPreview);
            return;
        }

        var asset = await _catalog.GetAssetByIdAsync(item.Id);
        if (asset is null)
        {
            return;
        }

        var targets = _selection.Count > 0 ? _selection.ToList() : [item];
        _previewTargets = targets;
        var perAsset = new List<IReadOnlyList<AssignedTag>>();
        foreach (var target in targets)
        {
            perAsset.Add(await _catalog.GetAssignedTagsAsync(target.Id));
        }

        var total = targets.Count;
        var union = new Dictionary<string, (AssignedTag Sample, int Count, HashSet<TagSource> Sources)>(StringComparer.Ordinal);
        foreach (var list in perAsset)
        {
            foreach (var tag in list)
            {
                if (!union.TryGetValue(tag.TagId, out var current))
                {
                    union[tag.TagId] = (tag, 1, [tag.Source]);
                }
                else
                {
                    current.Sources.Add(tag.Source);
                    union[tag.TagId] = (current.Sample, current.Count + 1, current.Sources);
                }
            }
        }

        var existing = await _catalog.GetTagsAsync();
        var suggestions = PromptTagSuggester.Suggest(asset.Prompt, existing);
        var hydrateOnOpen = AccessService.WouldHydrateOnOpen(asset.Path);
        var previewPath = asset.IsOrphan
            ? null
            : hydrateOnOpen ? item.ThumbPath : asset.Path;
        var canEdit = total == 1;
        await UiDispatch.RunAsync(() =>
        {
            CanEditNotes = canEdit;
            PreviewPath = previewPath;
            IsVideoPreview = !hydrateOnOpen && asset.Kind == AssetKind.Video && previewPath is not null;
            IsImagePreview = previewPath is not null &&
                (hydrateOnOpen || asset.Kind is AssetKind.Image or AssetKind.Gif);
            ShowPreviewPlaceholder = GalleryMedia.ShowPlaceholderTile(
                hydrateOnOpen || asset.IsOnlineOnly, asset.IsOrphan, !string.IsNullOrEmpty(previewPath));
            PreviewPlaceholderKind = asset.Kind;
            PreviewPrompt = asset.Prompt;
            PreviewNegative = asset.NegativePrompt;
            PreviewModel = asset.Model;
            PreviewFileName = asset.FileName;
            PreviewSeed = asset.Seed;
            PreviewNotes = asset.Notes;
            PreviewRating = asset.Rating ?? 0;

            AssignedTags.Clear();
            foreach (var entry in union.Values
                .OrderBy(v => v.Sample.TagName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(v => v.Sample.TagName, StringComparer.Ordinal))
            {
                var tag = entry.Sample;
                var groups = tag.ParentNames.Count > 0 ? $" ({string.Join(", ", tag.ParentNames)})" : "";
                var isPartial = total > 1 && entry.Count < total;
                var source = entry.Sources.Count == 1 ? entry.Sources.First() : tag.Source;
                AssignedTags.Add(new AssignedTagItem
                {
                    TagId = tag.TagId,
                    TagName = tag.TagName,
                    Display = tag.TagName + groups,
                    Source = source,
                    SourceLabel = entry.Sources.Count == 1
                        ? (source == TagSource.Implied ? "Implied" : source.ToString())
                        : "Mixed",
                    EffectiveColor = tag.EffectiveColor,
                    IsPartial = isPartial,
                    CountLabel = isPartial ? $"{entry.Count}/{total}" : ""
                });
            }

            AssignedTagsSummary = AssignedTags.Count == 0
                ? "No tags assigned"
                : string.Join(", ", AssignedTags.Select(t => t.TagName));

            Suggestions.Clear();
            foreach (var suggestion in suggestions)
            {
                if (AssignedTags.Any(t => t.TagName.Equals(suggestion.Token, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Suggestions.Add(suggestion);
            }

            RebuildAssignPanel();
        });
    }

    private void ClearPreview()
    {
        AssignedTags.Clear();
        Suggestions.Clear();
        _previewTargets = [];
        AssignedTagsSummary = "";
        IsImagePreview = false;
        IsVideoPreview = false;
        ShowPreviewPlaceholder = false;
        PreviewPath = null;
        PreviewPrompt = null;
        PreviewNegative = null;
        PreviewModel = null;
        PreviewFileName = null;
        PreviewSeed = null;
        PreviewNotes = null;
        PreviewRating = 0;
    }

    public Task ReloadAssignedTagsAsync() => LoadPreviewAsync(SelectedAsset);

    public async Task ReloadTagCatalogAsync()
    {
        var tags = await _catalog.GetTagsAsync();
        var memberships = await _catalog.GetMembershipsAsync();
        var pickItems = BuildTagPicks(tags, memberships);
        var selectedTagId = SelectedTag?.TagId;
        var filterIds = SelectedFilterTags.Select(t => t.TagId).ToList();
        await UiDispatch.RunAsync(() =>
        {
            _tags = tags.ToList();
            _memberships = memberships.ToList();
            TagTreeBuilder.Replace(TagTree, tags, memberships);
            ApplyLibraryTreeColors();
            RestoreFilterSelection(filterIds);
            if (IsTagBrowse)
            {
                SelectedTag = selectedTagId is null ? null : TagTreeBuilder.Find(TagTree, selectedTagId);
            }

            ReplaceTagPicks(pickItems);
            RebuildAssignPanel();
        });
    }

    partial void OnTagQueryChanged(string value)
    {
        ApplyTagSuggestionFilter(value);
        RebuildAssignPanel();
    }

    private void ReplaceTagPicks(List<TagPickItem> pickItems)
    {
        _tagPicks = pickItems;
        AllTags.Clear();
        foreach (var pick in pickItems)
        {
            AllTags.Add(pick);
        }

        ApplyTagSuggestionFilter(TagQuery);
    }

    private void ApplyTagSuggestionFilter(string? query)
    {
        var q = (query ?? "").Trim();
        IEnumerable<TagPickItem> source = _tagPicks;
        if (q.Length > 0)
        {
            source = _tagPicks
                .Select(t => (Item: t, Score: ScoreTagPick(t, q)))
                .Where(x => x.Score > 0)
                .OrderBy(x => x.Score)
                .ThenBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Item);
        }

        var items = q.Length == 0 ? new List<TagPickItem>() : source.Take(40).ToList();
        TagSuggestions.Clear();
        foreach (var item in items)
        {
            TagSuggestions.Add(item);
        }

        TagSuggestionsSummary = q.Length == 0
            ? "Type to find a tag"
            : items.Count == 0
                ? "No matching tags"
                : string.Join(", ", items.Select(t => t.Name));
    }

    private static int ScoreTagPick(TagPickItem tag, string query)
    {
        if (tag.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (tag.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return tag.Display.Contains(query, StringComparison.OrdinalIgnoreCase) ? 3 : 0;
    }

    private TagPickItem? FindTagPick(string query)
    {
        if (SelectedPickTag is not null &&
            (SelectedPickTag.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ||
             SelectedPickTag.Display.Equals(query, StringComparison.OrdinalIgnoreCase)))
        {
            return SelectedPickTag;
        }

        return _tagPicks.FirstOrDefault(t => t.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? _tagPicks.FirstOrDefault(t => t.Display.Equals(query, StringComparison.OrdinalIgnoreCase));
    }

    private static List<TagPickItem> BuildTagPicks(IReadOnlyList<Tag> tags, IReadOnlyList<TagMembership> memberships)
    {
        var byId = tags.ToDictionary(t => t.Id);
        var colors = CatalogService.MapEffectiveColors(tags, memberships);
        var picks = new List<TagPickItem>();
        foreach (var tag in tags.OrderByDescending(t => t.Priority).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var parents = memberships
                .Where(m => m.ChildId == tag.Id && byId.ContainsKey(m.ParentId))
                .Select(m => byId[m.ParentId].Name)
                .ToList();
            var extra = parents.Count > 0 ? $" ({string.Join(", ", parents)})" : "";
            picks.Add(new TagPickItem
            {
                TagId = tag.Id,
                Name = tag.Name,
                Display = tag.Name + extra,
                EffectiveColor = colors.GetValueOrDefault(tag.Id)
            });
        }

        return picks;
    }

    private async Task<IReadOnlyList<Asset>> SelectedAssetsAsync()
    {
        if (_selection.Count > 0)
        {
            return await _catalog.GetAssetsByIdsAsync(_selection.Select(s => s.Id));
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
        CanEditNotes = false;
        SelectedAsset = null;
    }

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

    private async Task AssignToSelectionAsync(IReadOnlyList<AssetItem> targets, string tagId, string tagName, TagSource source)
    {
        foreach (var asset in targets)
        {
            await _catalog.AssignTagAsync(asset.Id, tagId, source);
        }

        if (source == TagSource.Manual)
        {
            RememberRecent(tagId);
        }

        StatusText = targets.Count == 1
            ? $"Tagged 1 image with {tagName}"
            : $"Tagged {targets.Count} images with {tagName}";
    }

    private void LoadRecentTags()
    {
        _recentTagIds = RecentTags.Parse(
            ApplicationData.Current.LocalSettings.Values[RecentTags.SettingsKey] as string).ToList();
    }

    private void RememberRecent(string tagId)
    {
        _recentTagIds = RecentTags.Remember(_recentTagIds, tagId).ToList();
        ApplicationData.Current.LocalSettings.Values[RecentTags.SettingsKey] = RecentTags.Serialize(_recentTagIds);
    }

    private void SyncFilterFlags()
    {
        HasTagFilters = SelectedFilterTags.Count > 0;
        ShowTagFilterChips = HasTagFilters && IsTagBrowse;
        TagFilterSummary = SelectedFilterTags.Count == 0
            ? ""
            : string.Join(TagFilterMode == TagFilterMode.All ? " + " : TagFilterMode == TagFilterMode.None ? " except " : " or ",
                SelectedFilterTags.Select(t => t.Name));
    }

    private static TagChipItem ToFilterChip(TagTreeNode node) => new()
    {
        TagId = node.TagId ?? "",
        Name = node.Name,
        EffectiveColor = node.EffectiveColor,
        IsStarred = node.IsStarred,
        IsFilterSelected = true,
        AutomationPrefix = "BtnTagFilter_"
    };

    private TagChipItem ToAssignChip(TagPanelChip chip)
    {
        var assigned = AssignedTags.FirstOrDefault(t => t.TagId == chip.TagId);
        return new TagChipItem
        {
            TagId = chip.TagId,
            Name = chip.Name,
            EffectiveColor = chip.EffectiveColor,
            IsStarred = chip.IsStarred,
            IsAssigned = assigned is not null,
            IsPartial = assigned?.IsPartial == true,
            AutomationPrefix = "BtnAssignChip_"
        };
    }

    public void RebuildAssignPanelPublic() => RebuildAssignPanel();

    private void RebuildAssignPanel()
    {
        var recommended = new List<string>();
        foreach (var suggestion in Suggestions)
        {
            if (suggestion.ExistingTagId is { } id)
            {
                recommended.Add(id);
            }
        }

        foreach (var tag in AssignedTags.Where(t => t.IsPartial))
        {
            recommended.Add(tag.TagId);
        }

        var query = TagNameList.Split(TagQuery);
        var panelQuery = query.Count == 1 ? query[0] : query.Count == 0 ? TagQuery : "";
        var colors = _tags.Count == 0
            ? null
            : CatalogService.MapEffectiveColors(_tags, _memberships);
        var model = TagPanelBuilder.Build(
            _tags,
            _memberships,
            _recentTagIds,
            recommended,
            panelQuery,
            colors);

        var collapsed = AssignGroups
            .Where(g => !g.IsExpanded)
            .Select(g => g.GroupId ?? g.Name)
            .ToHashSet(StringComparer.Ordinal);
        ReplaceChips(StarredAssignTags, model.Starred);
        ReplaceChips(RecentAssignTags, model.Recent);
        ReplaceChips(RecommendedAssignTags, model.Recommended);
        AssignGroups.Clear();
        foreach (var group in model.Groups)
        {
            var item = new TagBoardGroup
            {
                GroupId = group.GroupId,
                Name = group.Name,
                Color = group.Color,
                IsUngrouped = group.IsUngrouped,
                IsExpanded = !collapsed.Contains(group.GroupId ?? group.Name)
            };
            foreach (var chip in group.Chips)
            {
                item.Chips.Add(ToAssignChip(chip));
            }

            AssignGroups.Add(item);
        }
    }

    private void ReplaceChips(ObservableCollection<TagChipItem> target, IReadOnlyList<TagPanelChip> source)
    {
        target.Clear();
        foreach (var chip in source)
        {
            target.Add(ToAssignChip(chip));
        }
    }

    private void ApplyLibraryTreeColors()
    {
        if (_tags.Count == 0)
        {
            return;
        }

        var colors = CatalogService.MapEffectiveColors(_tags, _memberships);
        ApplyLibraryTreeColors(TagTree, colors);
    }

    private static void ApplyLibraryTreeColors(
        IEnumerable<TagTreeNode> nodes,
        IReadOnlyDictionary<string, string?> colors)
    {
        foreach (var node in nodes)
        {
            if (node.TagId is { } id)
            {
                node.EffectiveColor = colors.GetValueOrDefault(id);
            }

            ApplyLibraryTreeColors(node.Children, colors);
        }
    }

    private void RestoreFilterSelection(IReadOnlyList<string> filterIds)
    {
        var keep = filterIds.ToList();
        SelectedFilterTags.Clear();
        foreach (var id in keep)
        {
            var node = TagTreeBuilder.Find(TagTree, id);
            if (node?.TagId is null)
            {
                continue;
            }

            node.IsFilterSelected = true;
            SelectedFilterTags.Add(ToFilterChip(node));
        }

        StampFilterSelected(TagTree, keep.ToHashSet(StringComparer.Ordinal));
        SyncFilterFlags();
    }

    private static void StampFilterSelected(IEnumerable<TagTreeNode> nodes, HashSet<string> selected)
    {
        foreach (var node in nodes)
        {
            node.IsFilterSelected = node.TagId is { } id && selected.Contains(id);
            StampFilterSelected(node.Children, selected);
        }
    }

    private AssetItem ToItem(Asset asset) => AssetItemMapper.FromAsset(asset, _thumbs);

    private void ReplaceFolderTree(IReadOnlyList<FolderNode> roots)
    {
        FolderTree.Clear();
        foreach (var root in roots)
        {
            FolderTree.Add(root);
        }
    }

    private static List<FolderNode> BuildFolderNodes(IReadOnlyList<SourceFolder> sources, IReadOnlyList<Asset> assets)
    {
        var roots = new List<FolderNode>();
        foreach (var source in sources)
        {
            var root = new FolderNode
            {
                Name = Path.GetFileName(source.Path.TrimEnd('\\')) is { Length: > 0 } n ? n : source.Path,
                Path = source.Path,
                SourceFolderId = source.Id
            };
            var dirs = assets.Where(a => a.SourceFolderId == source.Id)
                .Select(a => Path.GetDirectoryName(a.Path) ?? source.Path)
                .Where(d => d.StartsWith(source.Path, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs)
            {
                EnsurePath(root, source.Path, dir);
            }

            roots.Add(root);
        }

        return roots;
    }

    private static void EnsurePath(FolderNode root, string sourcePath, string fullDir)
    {
        if (string.Equals(fullDir, sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relative = Path.GetRelativePath(sourcePath, fullDir);
        if (relative.StartsWith(".."))
        {
            return;
        }

        var current = root;
        var acc = sourcePath;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            acc = Path.Combine(acc, part);
            var next = current.Children.FirstOrDefault(c => c.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                next = new FolderNode { Name = part, Path = acc, SourceFolderId = root.SourceFolderId };
                current.Children.Add(next);
            }

            current = next;
        }
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new PathCrumb { Name = "Library", Path = "" });
        if (IsTagBrowse)
        {
            foreach (var chip in SelectedFilterTags)
            {
                Breadcrumbs.Add(new PathCrumb { Name = chip.Name, Path = chip.TagId });
            }

            return;
        }

        var node = SelectedFolder;
        if (node is null)
        {
            return;
        }

        var chain = new List<FolderNode>();
        CollectChain(FolderTree, node.Path, chain);
        if (chain.Count == 0)
        {
            Breadcrumbs.Add(new PathCrumb { Name = node.Name, Path = node.Path });
            return;
        }

        foreach (var item in chain)
        {
            Breadcrumbs.Add(new PathCrumb { Name = item.Name, Path = item.Path });
        }
    }

    private static bool CollectChain(IEnumerable<FolderNode> nodes, string path, List<FolderNode> chain)
    {
        foreach (var node in nodes)
        {
            chain.Add(node);
            if (string.Equals(node.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (CollectChain(node.Children, path, chain))
            {
                return true;
            }

            chain.RemoveAt(chain.Count - 1);
        }

        return false;
    }

    private static FolderNode? FindNode(IEnumerable<FolderNode> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var child = FindNode(node.Children, path);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private void LoadRowHeight()
    {
        var key = RowHeightKey(AppServices.CurrentProject.Id);
        MosaicRowHeight = ApplicationData.Current.LocalSettings.Values[key] switch
        {
            double d => Math.Clamp(d, 96, 280),
            float f => Math.Clamp(f, 96, 280),
            int i => Math.Clamp(i, 96, 280),
            _ => 140
        };
    }

    private static string RowHeightKey(string projectId) => $"MosaicRowHeight_{projectId}";

    private void Notify(string message)
    {
        InfoMessage = message;
        ShowInfo = true;
        StatusText = message;
    }

    private bool TryCreateGallery(AssetItem? item, out GalleryViewModel gallery)
    {
        gallery = null!;
        var start = item ?? SelectedAsset ?? _selection.LastOrDefault();
        if (!GalleryMedia.CanOpen(Assets.Count, start is not null))
        {
            Notify("Select an asset first.");
            return false;
        }

        var snapshot = Assets.ToList();
        var index = GalleryMedia.StartIndex(snapshot.Select(a => a.Id).ToList(), start!.Id);
        gallery = new GalleryViewModel(snapshot, index < 0 ? 0 : index, _catalog);
        return true;
    }

    private static void HydrateAfterLibraryOpen(AssetItem? item)
    {
        if (item is null || !AccessService.WouldHydrateOnOpen(item.Path))
        {
            return;
        }

        _ = AppServices.Hydration.HydrateAfterOpenAsync(item.Id);
    }
}
