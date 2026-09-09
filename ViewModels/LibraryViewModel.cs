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
    private List<TagPickItem> _tagPicks = [];
    private List<AssetItem> _selection = [];
    private List<AssetItem> _previewTargets = [];
    private AssetItem? _selectionAnchor;
    private bool _suppressFilter;
    private int _filterEpoch;

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
    public Action<GalleryViewModel>? RequestOpenGalleryWindow { get; set; }

    public ObservableCollection<FolderNode> FolderTree { get; } = [];
    public ObservableCollection<TagTreeNode> TagTree { get; } = [];
    public ObservableCollection<PathCrumb> Breadcrumbs { get; } = [];

    [ObservableProperty]
    public partial ObservableCollection<AssetItem> Assets { get; set; } = [];
    public ObservableCollection<AssignedTagItem> AssignedTags { get; } = [];
    public ObservableCollection<PromptSuggestion> Suggestions { get; } = [];
    public ObservableCollection<TagPickItem> AllTags { get; } = [];
    public ObservableCollection<TagPickItem> TagSuggestions { get; } = [];
    public ObservableCollection<OrganizePreviewItem> OrganizePreview { get; } = [];

    [ObservableProperty]
    public partial FolderNode? SelectedFolder { get; set; }

    [ObservableProperty]
    public partial TagTreeNode? SelectedTag { get; set; }

    [ObservableProperty]
    public partial bool IsTagBrowse { get; set; }

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
    public partial string? PreviewPath { get; set; }

    [ObservableProperty]
    public partial string? PreviewPrompt { get; set; }

    [ObservableProperty]
    public partial string? PreviewNegative { get; set; }

    [ObservableProperty]
    public partial string? PreviewModel { get; set; }

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
        if (!_suppressFilter)
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
        LoadRowHeight();
        await RefreshQuietAsync();
    }

    public async Task RefreshQuietAsync(string? preferredFolderPath = null)
    {
        var projectId = AppServices.CurrentProject.Id;
        var sources = await _catalog.GetSourceFoldersAsync(projectId);
        var assets = await _catalog.GetAssetsAsync(projectId: projectId);
        var tags = await _catalog.GetTagsAsync();
        var memberships = await _catalog.GetMembershipsAsync();
        var selectedPath = preferredFolderPath ?? SelectedFolder?.Path;
        var selectedTagId = SelectedTag?.TagId;
        var pickItems = BuildTagPicks(tags, memberships);

        await UiDispatch.RunAsync(() =>
        {
            _allAssets = assets;
            _suppressFilter = true;
            RebuildTree(sources, assets);
            TagTreeBuilder.Replace(TagTree, tags, memberships);
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
        IsBusy = true;
        try
        {
            StatusText = "Scanning…";
            var report = await _scan.ScanAllAsync(
                new Progress<string>(p => StatusText = $"Scanning {Path.GetFileName(p)}"),
                projectId: AppServices.CurrentProject.Id);
            await RefreshQuietAsync();
            StatusText = $"Indexed {report.Added} new, {report.Updated} updated, {report.Orphaned} missing.";
        }
        finally
        {
            IsBusy = false;
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

        var query = TagQuery.Trim();
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
        await UiDispatch.RunAsync(() =>
        {
            TagQuery = "";
            SelectedPickTag = null;
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

        var existing = await _catalog.FindTagByNameAsync(NewTagName);
        var tag = existing ?? await _catalog.CreateTagAsync(NewTagName.Trim());
        await AssignToSelectionAsync(targets, tag.Id, tag.Name, TagSource.Manual);
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

        IsBusy = true;
        try
        {
            var applied = await _organize.ApplyAsync(OrganizePreview.ToList());
            await RefreshQuietAsync();
            StatusText = $"Moved {applied} files. Last batch is undoable.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UndoOrganizeAsync()
    {
        IsBusy = true;
        try
        {
            var count = await _organize.UndoLastAsync();
            await RefreshQuietAsync();
            StatusText = count == 0 ? "Nothing to undo." : $"Restored {count} files.";
            ShowOrganizePanel = false;
        }
        finally
        {
            IsBusy = false;
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
        var files = new List<IStorageItem>();
        foreach (var asset in targets)
        {
            if (!File.Exists(asset.Path))
            {
                continue;
            }

            try
            {
                files.Add(await StorageFile.GetFileFromPathAsync(asset.Path));
            }
            catch
            {
                // Skip files the package cannot open.
            }
        }

        if (files.Count == 0)
        {
            Notify("Nothing to copy.");
            return;
        }

        var package = new DataPackage();
        package.SetStorageItems(files);
        Clipboard.SetContent(package);
        Notify($"Copied {files.Count} file(s).");
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

        var package = new DataPackage();
        package.SetText(string.Join(Environment.NewLine, targets.Select(t => t.Path)));
        Clipboard.SetContent(package);
        Notify(targets.Count == 1 ? "Copied path." : $"Copied {targets.Count} paths.");
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
            if (!File.Exists(asset.Path))
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
            if (File.Exists(asset.Path))
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
    }

    public void NavigateBreadcrumb(PathCrumb crumb)
    {
        if (IsTagBrowse)
        {
            SelectedTag = crumb.Path.Length == 0 ? null : TagTreeBuilder.Find(TagTree, crumb.Path);
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
        IsBusy = true;
        try
        {
            StatusText = $"Scanning {source.Path}…";
            var report = await _scan.ScanSourceAsync(source);
            await RefreshQuietAsync(source.Path);
            StatusText = $"Indexed {report.Added} files from {Path.GetFileName(source.Path)}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyFilterAsync()
    {
        var epoch = Interlocked.Increment(ref _filterEpoch);
        IEnumerable<Asset> source = _allAssets;
        if (IsTagBrowse && SelectedTag?.TagId is { } tagId)
        {
            var ids = new List<string> { tagId };
            ids.AddRange(await _catalog.GetDescendantTagIdsAsync(tagId));
            var tagged = await _catalog.GetAssetsForTagsAsync(ids, AppServices.CurrentProject.Id);
            var set = tagged.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
            source = source.Where(a => set.Contains(a.Id));
        }
        else if (!IsTagBrowse && SelectedFolder is not null)
        {
            var folder = SelectedFolder;
            source = source.Where(a => AssetIsInFolder(a, folder));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var hits = await _catalog.SearchAsync(SearchQuery, AppServices.CurrentProject.Id);
            var set = hits.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
            source = source.Where(a => set.Contains(a.Id));
        }

        var items = source.Select(ToItem).ToList();
        var search = SearchQuery;
        var tagName = SelectedTag?.Name;
        var folderName = SelectedFolder?.Name;
        var tagBrowse = IsTagBrowse && SelectedTag?.TagId is not null;
        var hasFolder = SelectedFolder is not null;
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

            ClearSelection();
            Assets = new ObservableCollection<AssetItem>(items);
            if (!string.IsNullOrWhiteSpace(search))
            {
                StatusText = $"{items.Count} search results";
            }
            else if (tagBrowse)
            {
                StatusText = $"{items.Count} tagged {tagName}";
            }
            else if (hasFolder)
            {
                StatusText = $"{items.Count} in {folderName}";
            }
            else
            {
                StatusText = items.Count == 0
                    ? "Add a folder to start your library."
                    : $"{items.Count} assets";
            }
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
        var previewPath = asset.IsOrphan ? null : asset.Path;
        var canEdit = total == 1;
        await UiDispatch.RunAsync(() =>
        {
            CanEditNotes = canEdit;
            PreviewPath = previewPath;
            IsVideoPreview = asset.Kind == AssetKind.Video && previewPath is not null;
            IsImagePreview = asset.Kind is AssetKind.Image or AssetKind.Gif && previewPath is not null;
            PreviewPrompt = asset.Prompt;
            PreviewNegative = asset.NegativePrompt;
            PreviewModel = asset.Model;
            PreviewSeed = asset.Seed;
            PreviewNotes = asset.Notes;
            PreviewRating = asset.Rating ?? 0;

            AssignedTags.Clear();
            foreach (var entry in union.Values
                .OrderByDescending(v => v.Sample.TagPriority)
                .ThenBy(v => v.Sample.TagName, StringComparer.OrdinalIgnoreCase))
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
        PreviewPath = null;
        PreviewPrompt = null;
        PreviewNegative = null;
        PreviewModel = null;
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
        await UiDispatch.RunAsync(() =>
        {
            TagTreeBuilder.Replace(TagTree, tags, memberships);
            if (IsTagBrowse)
            {
                SelectedTag = selectedTagId is null ? null : TagTreeBuilder.Find(TagTree, selectedTagId);
            }

            ReplaceTagPicks(pickItems);
        });
    }

    partial void OnTagQueryChanged(string value) => ApplyTagSuggestionFilter(value);

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

    private static bool AssetIsInFolder(Asset asset, FolderNode folder)
    {
        var folderPath = NormalizeDir(folder.Path);
        var assetDir = NormalizeDir(Path.GetDirectoryName(asset.Path) ?? "");
        if (string.Equals(assetDir, folderPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = folderPath + Path.DirectorySeparatorChar;
        return asset.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDir(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
            return _allAssets.Where(a => AssetIsInFolder(a, SelectedFolder)).ToList();
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

        StatusText = targets.Count == 1
            ? $"Tagged 1 image with {tagName}"
            : $"Tagged {targets.Count} images with {tagName}";
    }

    private AssetItem ToItem(Asset asset)
    {
        var thumb = asset.ContentHash is null ? null : _thumbs.PathForHash(asset.ContentHash);
        var thumbPath = thumb is not null && File.Exists(thumb) ? thumb : null;
        var width = asset.Width;
        var height = asset.Height;
        if (width is not > 0 || height is not > 0)
        {
            var probed = ImageDimensions.TryRead(thumbPath) ?? ImageDimensions.TryRead(asset.Path);
            if (probed is { } size)
            {
                width = size.Width;
                height = size.Height;
                asset.Width = size.Width;
                asset.Height = size.Height;
            }
        }

        return new AssetItem
        {
            Id = asset.Id,
            SourceFolderId = asset.SourceFolderId,
            FileName = asset.FileName,
            Path = asset.Path,
            ThumbPath = thumbPath,
            Kind = asset.Kind,
            IsOrphan = asset.IsOrphan,
            Model = asset.Model,
            Prompt = asset.Prompt,
            OrganizeError = asset.OrganizeError,
            Width = width,
            Height = height
        };
    }

    private void RebuildTree(IReadOnlyList<SourceFolder> sources, IReadOnlyList<Asset> assets)
    {
        FolderTree.Clear();
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

            FolderTree.Add(root);
        }
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
            if (SelectedTag?.TagId is { } tagId)
            {
                Breadcrumbs.Add(new PathCrumb { Name = SelectedTag.Name, Path = tagId });
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
        if (start is null || Assets.Count == 0)
        {
            Notify("Select an asset first.");
            return false;
        }

        var snapshot = Assets.ToList();
        var index = snapshot.FindIndex(a => a.Id == start.Id);
        gallery = new GalleryViewModel(snapshot, index < 0 ? 0 : index, _catalog);
        return true;
    }
}
