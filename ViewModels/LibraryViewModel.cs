using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
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
    private List<AssetItem> _selection = [];
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

    public ObservableCollection<FolderNode> FolderTree { get; } = [];
    public ObservableCollection<TagTreeNode> TagTree { get; } = [];
    public ObservableCollection<PathCrumb> Breadcrumbs { get; } = [];

    [ObservableProperty]
    public partial ObservableCollection<AssetItem> Assets { get; set; } = [];
    public ObservableCollection<AssignedTagItem> AssignedTags { get; } = [];
    public ObservableCollection<PromptSuggestion> Suggestions { get; } = [];
    public ObservableCollection<TagPickItem> AllTags { get; } = [];
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
    public partial TagPickItem? SelectedPickTag { get; set; }

    [ObservableProperty]
    public partial bool ShowOrganizePanel { get; set; }

    [ObservableProperty]
    public partial string InfoMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowInfo { get; set; }

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
        if (SelectedAsset is null)
        {
            return;
        }

        _ = _catalog.UpdateNotesAndRatingAsync(SelectedAsset.Id, value, (int)Math.Round(PreviewRating));
    }

    partial void OnPreviewRatingChanged(double value)
    {
        if (SelectedAsset is null)
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
        SelectedAsset = _selection.Count == 1 ? _selection[0] : _selection.LastOrDefault();
        _selectionAnchor = SelectedAsset;
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
                SelectedAsset = item;
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
        SelectedAsset = _selection.Count == 1 ? _selection[0] : _selection.LastOrDefault();
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
            AllTags.Clear();
            foreach (var pick in pickItems)
            {
                AllTags.Add(pick);
            }

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
    private async Task AssignPickedTagAsync()
    {
        if (SelectedAsset is null || SelectedPickTag is null)
        {
            return;
        }

        await _catalog.AssignTagAsync(SelectedAsset.Id, SelectedPickTag.TagId, TagSource.Manual);
        await LoadPreviewAsync(SelectedAsset);
        await ReloadTagPicksAsync();
    }

    [RelayCommand]
    private async Task CreateAndAssignTagAsync()
    {
        if (SelectedAsset is null || string.IsNullOrWhiteSpace(NewTagName))
        {
            return;
        }

        var existing = await _catalog.FindTagByNameAsync(NewTagName);
        var tag = existing ?? await _catalog.CreateTagAsync(NewTagName.Trim());
        await _catalog.AssignTagAsync(SelectedAsset.Id, tag.Id, TagSource.Manual);
        NewTagName = "";
        await LoadPreviewAsync(SelectedAsset);
        await ReloadTagPicksAsync();
    }

    [RelayCommand]
    private async Task RemoveAssignedTagAsync(AssignedTagItem? item)
    {
        if (SelectedAsset is null || item is null)
        {
            return;
        }

        await _catalog.RemoveTagAsync(SelectedAsset.Id, item.TagId);
        await LoadPreviewAsync(SelectedAsset);
    }

    [RelayCommand]
    private async Task AcceptSuggestionAsync(PromptSuggestion? suggestion)
    {
        if (SelectedAsset is null || suggestion is null)
        {
            return;
        }

        string tagId;
        if (suggestion.ExistingTagId is not null)
        {
            tagId = suggestion.ExistingTagId;
        }
        else
        {
            var created = await _catalog.CreateTagAsync(suggestion.Token);
            tagId = created.Id;
        }

        await _catalog.AssignTagAsync(SelectedAsset.Id, tagId, TagSource.Prompt);
        await LoadPreviewAsync(SelectedAsset);
        await ReloadTagPicksAsync();
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
        AssignedTags.Clear();
        Suggestions.Clear();
        if (item is null)
        {
            IsImagePreview = false;
            IsVideoPreview = false;
            PreviewPath = null;
            PreviewPrompt = null;
            PreviewNegative = null;
            PreviewModel = null;
            PreviewSeed = null;
            PreviewNotes = null;
            PreviewRating = 0;
            return;
        }

        var asset = await _catalog.GetAssetByIdAsync(item.Id);
        if (asset is null)
        {
            return;
        }

        PreviewPath = asset.IsOrphan ? null : asset.Path;
        IsVideoPreview = asset.Kind == AssetKind.Video && PreviewPath is not null;
        IsImagePreview = asset.Kind is AssetKind.Image or AssetKind.Gif && PreviewPath is not null;
        PreviewPrompt = asset.Prompt;
        PreviewNegative = asset.NegativePrompt;
        PreviewModel = asset.Model;
        PreviewSeed = asset.Seed;
        PreviewNotes = asset.Notes;
        PreviewRating = asset.Rating ?? 0;

        foreach (var tag in await _catalog.GetAssignedTagsAsync(asset.Id))
        {
            var groups = tag.ParentNames.Count > 0 ? $" ({string.Join(", ", tag.ParentNames)})" : "";
            AssignedTags.Add(new AssignedTagItem
            {
                TagId = tag.TagId,
                TagName = tag.TagName,
                Display = tag.TagName + groups,
                Source = tag.Source,
                SourceLabel = tag.Source.ToString()
            });
        }

        var existing = await _catalog.GetTagsAsync();
        Suggestions.Clear();
        foreach (var suggestion in PromptTagSuggester.Suggest(asset.Prompt, existing))
        {
            if (AssignedTags.Any(t => t.TagName.Equals(suggestion.Token, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Suggestions.Add(suggestion);
        }
    }

    private async Task ReloadTagPicksAsync()
    {
        var picks = BuildTagPicks(await _catalog.GetTagsAsync(), await _catalog.GetMembershipsAsync());
        await UiDispatch.RunAsync(() =>
        {
            AllTags.Clear();
            foreach (var pick in picks)
            {
                AllTags.Add(pick);
            }
        });
    }

    private static List<TagPickItem> BuildTagPicks(IReadOnlyList<Tag> tags, IReadOnlyList<TagMembership> memberships)
    {
        var byId = tags.ToDictionary(t => t.Id);
        var picks = new List<TagPickItem>();
        foreach (var tag in tags.OrderByDescending(t => t.Priority).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var parents = memberships
                .Where(m => m.ChildId == tag.Id && byId.ContainsKey(m.ParentId))
                .Select(m => byId[m.ParentId].Name)
                .ToList();
            var extra = parents.Count > 0 ? $" ({string.Join(", ", parents)})" : "";
            picks.Add(new TagPickItem { TagId = tag.Id, Display = tag.Name + extra });
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
        SelectedAsset = null;
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
}
