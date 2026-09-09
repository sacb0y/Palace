using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Data;
using Palace.Models;
using Palace.Services;

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
    public ObservableCollection<PathCrumb> Breadcrumbs { get; } = [];
    public ObservableCollection<AssetItem> Assets { get; } = [];
    public ObservableCollection<AssignedTagItem> AssignedTags { get; } = [];
    public ObservableCollection<PromptSuggestion> Suggestions { get; } = [];
    public ObservableCollection<TagPickItem> AllTags { get; } = [];
    public ObservableCollection<OrganizePreviewItem> OrganizePreview { get; } = [];

    [ObservableProperty]
    public partial FolderNode? SelectedFolder { get; set; }

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
        RebuildBreadcrumbs(value);
        _ = ApplyFilterAsync();
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
        HasSelection = _selection.Count > 0;
        if (_selection.Count == 1)
        {
            SelectedAsset = _selection[0];
        }
    }

    public async Task LoadAsync()
    {
        await RefreshQuietAsync();
        StatusText = Assets.Count == 0
            ? "Add a folder to start your library."
            : $"{Assets.Count} assets";
    }

    public async Task RefreshQuietAsync()
    {
        var sources = await _catalog.GetSourceFoldersAsync();
        var assets = new List<Asset>();
        foreach (var source in sources)
        {
            assets.AddRange(await _catalog.GetAssetsAsync(sourceId: source.Id));
        }

        _allAssets = assets;
        RebuildTree(sources, assets);
        await ReloadTagPicksAsync();
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

        var existing = (await _catalog.GetSourceFoldersAsync())
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
            FileTemplate = "{Character}-{tags}.{ext}"
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
            var report = await _scan.ScanAllAsync(new Progress<string>(p =>
                StatusText = $"Scanning {Path.GetFileName(p)}"));
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

        var rooms = await _catalog.GetRoomsAsync();
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
            await RefreshQuietAsync();
            StatusText = $"Indexed {report.Added} files from {Path.GetFileName(source.Path)}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyFilterAsync()
    {
        IReadOnlyList<Asset> source = _allAssets;
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            source = await _catalog.SearchAsync(SearchQuery);
        }
        else if (SelectedFolder is not null)
        {
            var prefix = SelectedFolder.Path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            source = _allAssets.Where(a =>
            {
                var dir = Path.GetDirectoryName(a.Path) ?? "";
                if (SelectedFolder.Children.Count == 0)
                {
                    return string.Equals(dir, SelectedFolder.Path, StringComparison.OrdinalIgnoreCase)
                        || a.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }

                return string.Equals(dir, SelectedFolder.Path, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        Assets.Clear();
        foreach (var asset in source)
        {
            Assets.Add(ToItem(asset));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            StatusText = $"{Assets.Count} search results";
        }
        else if (SelectedFolder is not null)
        {
            StatusText = $"{Assets.Count} in {SelectedFolder.Name}";
        }
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
        var tags = await _catalog.GetTagsAsync();
        var memberships = await _catalog.GetMembershipsAsync();
        var byId = tags.ToDictionary(t => t.Id);
        AllTags.Clear();
        foreach (var tag in tags.OrderByDescending(t => t.Priority).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var parents = memberships
                .Where(m => m.ChildId == tag.Id && byId.ContainsKey(m.ParentId))
                .Select(m => byId[m.ParentId].Name)
                .ToList();
            var extra = parents.Count > 0 ? $" ({string.Join(", ", parents)})" : "";
            AllTags.Add(new TagPickItem { TagId = tag.Id, Display = tag.Name + extra });
        }
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
            return _allAssets.Where(a =>
                a.Path.StartsWith(SelectedFolder.Path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetDirectoryName(a.Path), SelectedFolder.Path, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return [];
    }

    private AssetItem ToItem(Asset asset)
    {
        var thumb = asset.ContentHash is null ? null : _thumbs.PathForHash(asset.ContentHash);
        return new AssetItem
        {
            Id = asset.Id,
            SourceFolderId = asset.SourceFolderId,
            FileName = asset.FileName,
            Path = asset.Path,
            ThumbPath = thumb is not null && File.Exists(thumb) ? thumb : null,
            Kind = asset.Kind,
            IsOrphan = asset.IsOrphan,
            Model = asset.Model,
            Prompt = asset.Prompt,
            OrganizeError = asset.OrganizeError
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

    private void RebuildBreadcrumbs(FolderNode? node)
    {
        Breadcrumbs.Clear();
        if (node is null)
        {
            Breadcrumbs.Add(new PathCrumb { Name = "Library", Path = "" });
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

    private void Notify(string message)
    {
        InfoMessage = message;
        ShowInfo = true;
        StatusText = message;
    }
}
