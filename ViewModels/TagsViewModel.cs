using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Windows.Storage;

namespace Palace.ViewModels;

public partial class TagsViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly OrganizeService _organize;
    private readonly AccessService _access;
    private readonly ThumbnailService _thumbs;
    private List<Tag> _tags = [];
    private List<TagMembership> _memberships = [];
    private List<TagImplication> _implications = [];
    private int _mosaicEpoch;
    private int _busyDepth;
    private bool _suppressMosaicSort;
    private string? _reorderParentId;
    private string? _renameBaseline;
    private string? _renameBaselineTagId;
    private string _boardSelectionKey = "all";
    private const int MosaicChunkSize = 80;

    public TagsViewModel(CatalogService catalog, OrganizeService organize, AccessService access, ThumbnailService thumbs)
    {
        _catalog = catalog;
        _organize = organize;
        _access = access;
        _thumbs = thumbs;
    }

    public ObservableCollection<TagTreeNode> TagTree { get; } = [];
    public ObservableCollection<TagBoardGroup> Board { get; } = [];

    [ObservableProperty]
    public partial TagBoardGroup? SelectedBoard { get; set; }

    public string ChildrenHeader => SelectedBoard?.CountLabel ?? "Tags";
    public ObservableCollection<AssetItem> Assets { get; } = [];
    public ObservableCollection<TagMosaicSection> MosaicGroups { get; } = [];
    public ObservableCollection<TagGroupPick> ParentGroups { get; } = [];
    public ObservableCollection<TagGroupPick> AvailableGroups { get; } = [];
    public ObservableCollection<TagGroupPick> ImpliedTags { get; } = [];
    public ObservableCollection<TagGroupPick> ImpliedSuggestions { get; } = [];
    public ObservableCollection<OrganizePreviewItem> OrganizePreview { get; } = [];

    [ObservableProperty]
    public partial TagTreeNode? SelectedNode { get; set; }

    [ObservableProperty]
    public partial string NewTagName { get; set; } = "";

    [ObservableProperty]
    public partial string SelectedName { get; set; } = "";

    [ObservableProperty]
    public partial double SelectedPriority { get; set; }

    [ObservableProperty]
    public partial TagGroupPick? SelectedAvailableGroup { get; set; }

    [ObservableProperty]
    public partial string ImpliedQuery { get; set; } = "";

    [ObservableProperty]
    public partial string ImpliedTagsSummary { get; set; } = "";

    [ObservableProperty]
    public partial int AssetCount { get; set; }

    [ObservableProperty]
    public partial string AssetCountLabel { get; set; } = "0 images with this tag";

    [ObservableProperty]
    public partial bool IncludeNested { get; set; } = true;

    [ObservableProperty]
    public partial MosaicSort MosaicSort { get; set; } = MosaicSortOrder.Default;

    public IReadOnlyList<string> SortLabels => MosaicSortOrder.Labels;

    public int MosaicSortIndex
    {
        get => MosaicSortOrder.IndexOf(MosaicSort);
        set
        {
            var next = MosaicSortOrder.FromIndex(value);
            if (next == MosaicSort)
            {
                return;
            }

            MosaicSort = next;
        }
    }

    [ObservableProperty]
    public partial bool ShowOrganizePanel { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Create groups and nest tags. The same tag can live in more than one group.";

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    private void Notify(string message) => StatusText = message;

    [ObservableProperty]
    public partial string? SelectedEffectiveColor { get; set; }

    [ObservableProperty]
    public partial string SelectedColorLabel { get; set; } = "No color";

    [ObservableProperty]
    public partial bool HasCustomColor { get; set; }

    [ObservableProperty]
    public partial string BackfillStatus { get; set; } = "";

    public Func<IReadOnlyList<TagPath>, Task<OrganizeChoice?>>? RequestOrganizeChoice { get; set; }
    public Func<string, string, string, Task<bool>>? RequestConfirm { get; set; }
    public Action<GalleryViewModel>? RequestOpenGallery { get; set; }
    public Action? RequestShowLibrary { get; set; }
    public Action? RequestFocusRename { get; set; }
    public Action? MosaicReset { get; set; }
    public Action? MosaicChunkAppended { get; set; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial TagScope Scope { get; set; } = TagScope.All;

    [ObservableProperty]
    public partial bool SelectedIsStarred { get; set; }

    [ObservableProperty]
    public partial bool ShowDetails { get; set; }

    private int _backfillEpoch;

    public async Task LoadAsync() => await RefreshAsync();

    partial void OnMosaicSortChanged(MosaicSort value)
    {
        OnPropertyChanged(nameof(MosaicSortIndex));
        if (_suppressMosaicSort)
        {
            return;
        }

        if (AppServices.CurrentProject is { } project)
        {
            ApplicationData.Current.LocalSettings.Values[MosaicSortOrder.SettingsKey(project.Id)] =
                MosaicSortOrder.Persist(value);
        }

        _ = LoadMosaicAsync();
    }

    private void LoadMosaicSort()
    {
        var projectId = AppServices.CurrentProject?.Id;
        if (projectId is null)
        {
            return;
        }

        var next = MosaicSortOrder.Parse(
            ApplicationData.Current.LocalSettings.Values[MosaicSortOrder.SettingsKey(projectId)]);
        _suppressMosaicSort = true;
        MosaicSort = next;
        _suppressMosaicSort = false;
    }

    public async Task RefreshAsync()
    {
        BeginBusy("Loading…");
        try
        {
            LoadMosaicSort();
            _tags = (await _catalog.GetTagsAsync()).ToList();
            _memberships = (await _catalog.GetMembershipsAsync()).ToList();
            _implications = (await _catalog.GetImplicationsAsync()).ToList();
            await UiDispatch.RunAsync(RebuildTree);
            await LoadSelectionAsync();
            await LoadMosaicAsync();
            if (AppServices.Library is { } library)
            {
                await library.ReloadTagCatalogAsync();
                await library.ReloadAssignedTagsAsync();
            }
        }
        finally
        {
            EndBusy();
        }
    }

    public void SelectNode(TagTreeNode? node)
    {
        SelectedNode = node;
        ShowDetails = node?.TagId is not null;
        StampSelectionName(node);
        StampBoardSelection();
        RevealSelectedBoardRow();
        _ = LoadSelectionAsync();
        _ = LoadMosaicAsync();
    }

    private void StampSelectionName(TagTreeNode? node)
    {
        if (node?.TagId is null)
        {
            _renameBaseline = null;
            _renameBaselineTagId = null;
            return;
        }

        var tag = _tags.FirstOrDefault(t => t.Id == node.TagId);
        var name = tag?.Name ?? node.Name;
        SelectedName = name;
        _renameBaseline = name;
        _renameBaselineTagId = node.TagId;
        HasSelection = true;
        ShowDetails = true;
        if (tag is null)
        {
            return;
        }

        SelectedPriority = tag.Priority;
        SelectedIsStarred = tag.IsStarred;
    }

    partial void OnSearchQueryChanged(string value) => RebuildBoard();

    partial void OnScopeChanged(TagScope value) => RebuildBoard();

    [RelayCommand]
    private Task CreateUngroupedAsync() =>
        ErrorReporter.RunAsync("Create ungrouped", Notify, async () =>
        {
            if (string.IsNullOrWhiteSpace(NewTagName))
            {
                return;
            }

            var result = await _catalog.CreateTagsAsync(TagNameList.Split(NewTagName));
            NewTagName = "";
            _reorderParentId = null;
            await RefreshAsync();
            if (result.Tags.Count > 0)
            {
                SelectCreated(result.Tags[^1].Id);
            }

            StatusText = DescribeBatch(result);
        });

    [RelayCommand]
    private Task CreateChildAsync() =>
        ErrorReporter.RunAsync("Create child", Notify, async () =>
        {
            if (SelectedNode?.TagId is null || string.IsNullOrWhiteSpace(NewTagName))
            {
                return;
            }

            var parentId = SelectedNode.TagId;
            var result = await _catalog.CreateTagsAsync(TagNameList.Split(NewTagName), parentId);
            NewTagName = "";
            _reorderParentId = parentId;
            await RefreshAsync();
            if (result.Tags.Count > 0)
            {
                SelectCreated(result.Tags[^1].Id);
            }

            StatusText = DescribeBatch(result, " under the selected group");
        });

    [RelayCommand]
    private Task ToggleStarAsync() =>
        ErrorReporter.RunAsync("Toggle star", Notify, async () =>
        {
            if (SelectedNode?.TagId is null)
            {
                return;
            }

            var next = !SelectedIsStarred;
            await _catalog.SetTagStarredAsync(SelectedNode.TagId, next);
            await RefreshAsync();
            StatusText = next ? "Starred this tag." : "Removed star.";
        });

    [RelayCommand]
    private Task MoveUpAsync() =>
        ErrorReporter.RunAsync("Move up", Notify, async () =>
        {
            if (SelectedNode?.TagId is null)
            {
                return;
            }

            var ok = await _catalog.MoveTagAmongSiblingsAsync(SelectedNode.TagId, _reorderParentId, -1);
            await RefreshAsync();
            StatusText = ok ? "Moved tag up." : "Already first among siblings.";
        });

    [RelayCommand]
    private Task MoveDownAsync() =>
        ErrorReporter.RunAsync("Move down", Notify, async () =>
        {
            if (SelectedNode?.TagId is null)
            {
                return;
            }

            var ok = await _catalog.MoveTagAmongSiblingsAsync(SelectedNode.TagId, _reorderParentId, 1);
            await RefreshAsync();
            StatusText = ok ? "Moved tag down." : "Already last among siblings.";
        });

    [RelayCommand]
    private Task DeleteTagAsync() =>
        ErrorReporter.RunAsync("Delete tag", Notify, async () =>
        {
            if (SelectedNode?.TagId is null)
            {
                return;
            }

            var name = SelectedNode.Name;
            var ok = RequestConfirm is null
                || await RequestConfirm("Delete tag", $"Delete “{name}”? Assignments and group links are removed.", "Delete");
            if (!ok)
            {
                return;
            }

            await _catalog.DeleteTagAsync(SelectedNode.TagId);
            SelectedNode = null;
            await RefreshAsync();
            StatusText = $"Deleted {name}.";
        });

    [RelayCommand]
    private Task StarChipAsync(TagChipItem? chip) =>
        ErrorReporter.RunAsync("Star chip", Notify, async () =>
        {
            if (chip?.TagId is null)
            {
                return;
            }

            var next = !chip.IsStarred;
            await _catalog.SetTagStarredAsync(chip.TagId, next);
            await RefreshAsync();
            StatusText = next ? "Starred this tag." : "Removed star.";
        });

    [RelayCommand]
    private Task RemoveChipFromGroupAsync(TagChipItem? chip) =>
        ErrorReporter.RunAsync("Remove chip from group", Notify, async () =>
        {
            if (chip?.TagId is null)
            {
                return;
            }

            var rootId = chip.ParentGroupId ?? chip.ImmediateParentId;
            if (string.IsNullOrEmpty(rootId))
            {
                return;
            }

            var parents = TagSiblings.ParentsUnderRoot(_memberships, chip.TagId, rootId);
            if (parents.Count == 0 && !string.IsNullOrEmpty(chip.ImmediateParentId))
            {
                parents = [chip.ImmediateParentId];
            }

            foreach (var parentId in parents)
            {
                await _catalog.RemoveMembershipAsync(parentId, chip.TagId);
            }

            _reorderParentId = rootId;
            await RefreshAsync();
            StatusText = "Removed from group.";
        });

    [RelayCommand]
    private Task DeleteChipAsync(TagChipItem? chip) =>
        ErrorReporter.RunAsync("Delete chip", Notify, async () =>
        {
            if (chip is null)
            {
                return;
            }

            SelectChip(chip);
            await DeleteTagAsync();
        });

    [RelayCommand]
    private void FilterChip(TagChipItem? chip)
    {
        if (chip?.TagId is null)
        {
            return;
        }

        AppServices.Library.ApplySingleTagFilter(chip.TagId, chip.Name);
        RequestShowLibrary?.Invoke();
    }

    [RelayCommand]
    private void RenameChip(TagChipItem? chip)
    {
        if (chip is null)
        {
            return;
        }

        SelectChip(chip);
        RequestFocusRename?.Invoke();
    }

    [RelayCommand]
    private Task AddChipToGroupAsync(TagChipGroupMove? move) =>
        ErrorReporter.RunAsync("Add chip to group", Notify, async () =>
        {
            if (move?.Chip?.TagId is null || string.IsNullOrEmpty(move.GroupId))
            {
                return;
            }

            var ok = await _catalog.AddMembershipAsync(move.GroupId, move.Chip.TagId);
            StatusText = ok
                ? $"Also grouped under {move.GroupName}."
                : "That membership would create a cycle.";
            if (ok)
            {
                _reorderParentId = move.GroupId;
            }

            await RefreshAsync();
        });

    [RelayCommand]
    private Task MoveChipToGroupAsync(TagChipGroupMove? move) =>
        ErrorReporter.RunAsync("Move chip to group", Notify, async () =>
        {
            if (move?.Chip?.TagId is null || string.IsNullOrEmpty(move.GroupId))
            {
                return;
            }

            var rootId = move.Chip.ParentGroupId ?? move.Chip.ImmediateParentId;
            if (!string.IsNullOrEmpty(rootId) && !string.Equals(rootId, move.GroupId, StringComparison.Ordinal))
            {
                var parents = TagSiblings.ParentsUnderRoot(_memberships, move.Chip.TagId, rootId);
                if (parents.Count == 0 && !string.IsNullOrEmpty(move.Chip.ImmediateParentId))
                {
                    parents = [move.Chip.ImmediateParentId];
                }

                foreach (var parentId in parents)
                {
                    await _catalog.RemoveMembershipAsync(parentId, move.Chip.TagId);
                }
            }

            var ok = await _catalog.AddMembershipAsync(move.GroupId, move.Chip.TagId);
            StatusText = ok
                ? $"Moved to {move.GroupName}."
                : "Removed from this group, but that destination would create a cycle.";
            if (ok)
            {
                _reorderParentId = move.GroupId;
            }

            await RefreshAsync();
        });

    public IReadOnlyList<TagGroupPick> GroupDestinations(TagChipItem chip, bool add)
    {
        return TagGroups.Destinations(_tags, _memberships, chip.TagId, chip.ParentGroupId, add)
            .Select(tag => new TagGroupPick { TagId = tag.Id, Name = tag.Name })
            .ToList();
    }

    public void SelectChip(TagChipItem? chip)
    {
        if (chip?.TagId is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(chip.BoardKey))
        {
            _boardSelectionKey = chip.BoardKey;
        }

        _reorderParentId = string.IsNullOrEmpty(chip.ParentGroupId) ? null : chip.ParentGroupId;
        var node = TagTreeBuilder.Find(TagTree, chip.TagId)
            ?? new TagTreeNode { TagId = chip.TagId, Name = chip.Name, EffectiveColor = chip.EffectiveColor, IsStarred = chip.IsStarred };
        SelectNode(node);
    }

    [RelayCommand]
    private void OpenAsset(AssetItem? item)
    {
        if (item is null || Assets.Count == 0)
        {
            return;
        }

        var snapshot = Assets.ToList();
        var index = GalleryMedia.StartIndex(snapshot.Select(a => a.Id).ToList(), item.Id);
        var gallery = new GalleryViewModel(snapshot, index < 0 ? 0 : index, _catalog);
        RequestOpenGallery?.Invoke(gallery);
        if (AccessService.WouldHydrateOnOpen(item.Path))
        {
            _ = AppServices.Hydration.HydrateAfterOpenAsync(item.Id);
        }
    }

    [RelayCommand]
    private Task SaveRenameAsync() =>
        ErrorReporter.RunAsync("Save rename", Notify, async () =>
        {
            if (SelectedNode?.TagId is null || string.IsNullOrWhiteSpace(SelectedName))
            {
                return;
            }

            await _catalog.RenameTagAsync(SelectedNode.TagId, SelectedName);
            await _catalog.SetTagPriorityAsync(SelectedNode.TagId, (int)SelectedPriority);
            await RefreshAsync();
            StatusText = "Tag updated.";
        });

    public async Task ApplyColorAsync(string? hex)
    {
        if (SelectedNode?.TagId is null)
        {
            return;
        }

        await _catalog.SetTagColorAsync(SelectedNode.TagId, hex);
        await RefreshAsync();
        StatusText = "Tag color updated.";
    }

    [RelayCommand]
    private Task ClearColorAsync() =>
        ErrorReporter.RunAsync("Clear color", Notify, async () =>
        {
            if (SelectedNode?.TagId is null)
            {
                return;
            }

            await _catalog.SetTagColorAsync(SelectedNode.TagId, null);
            await RefreshAsync();
            StatusText = "Tag color cleared; it will inherit again.";
        });

    [RelayCommand]
    private Task AddToGroupAsync() =>
        ErrorReporter.RunAsync("Add to group", Notify, async () =>
        {
            if (SelectedNode?.TagId is null || SelectedAvailableGroup is null)
            {
                return;
            }

            var ok = await _catalog.AddMembershipAsync(SelectedAvailableGroup.TagId, SelectedNode.TagId);
            StatusText = ok ? $"Also grouped under {SelectedAvailableGroup.Name}." : "That membership would create a cycle.";
            if (ok)
            {
                _reorderParentId = SelectedAvailableGroup.TagId;
            }

            await RefreshAsync();
        });

    [RelayCommand]
    private Task RemoveFromGroupAsync(TagGroupPick? group) =>
        ErrorReporter.RunAsync("Remove from group", Notify, async () =>
        {
            if (SelectedNode?.TagId is null || group is null)
            {
                return;
            }

            await _catalog.RemoveMembershipAsync(group.TagId, SelectedNode.TagId);
            if (string.Equals(_reorderParentId, group.TagId, StringComparison.Ordinal))
            {
                _reorderParentId = ParentGroups.FirstOrDefault(g => g.TagId != group.TagId)?.TagId;
            }

            await RefreshAsync();
            StatusText = $"Removed from {group.Name}.";
        });

    [RelayCommand]
    private Task AddImpliedAsync() =>
        ErrorReporter.RunAsync("Add implied", Notify, async () =>
        {
            if (SelectedNode?.TagId is null || string.IsNullOrWhiteSpace(ImpliedQuery))
            {
                return;
            }

            var sourceId = SelectedNode.TagId;
            var name = ImpliedQuery.Trim();
            var existing = await _catalog.FindTagByNameAsync(name);
            var target = existing ?? await _catalog.CreateTagAsync(name);
            var ok = await _catalog.AddImplicationAsync(sourceId, target.Id);
            await UiDispatch.RunAsync(() => ImpliedQuery = "");
            await RefreshAsync();
            StatusText = ok
                ? $"Assigning this tag will also apply {target.Name}."
                : "That implicit tag would create a cycle.";
            if (ok)
            {
                QueueBackfill(() => _catalog.BackfillImplicationAddedAsync(sourceId));
            }
        });

    [RelayCommand]
    private Task RemoveImpliedAsync(TagGroupPick? item) =>
        ErrorReporter.RunAsync("Remove implied", Notify, async () =>
        {
            if (SelectedNode?.TagId is null || item is null)
            {
                return;
            }

            var impliedId = item.TagId;
            await _catalog.RemoveImplicationAsync(SelectedNode.TagId, impliedId);
            await RefreshAsync();
            StatusText = $"{item.Name} is no longer implied.";
            QueueBackfill(() => _catalog.BackfillImplicationRemovedAsync(impliedId));
        });

    [RelayCommand]
    private Task PreviewOrganizeAsync() =>
        ErrorReporter.RunAsync("Preview organize", Notify, async () =>
        {
            if (SelectedNode?.TagId is null || RequestOrganizeChoice is null)
            {
                return;
            }

            var paths = await _catalog.GetPathsToTagAsync(SelectedNode.TagId);
            if (paths.Count == 0)
            {
                StatusText = "Select a tag first.";
                return;
            }

            var choice = await RequestOrganizeChoice(paths);
            if (choice is null)
            {
                return;
            }

            var tagIds = new List<string> { SelectedNode.TagId };
            if (IncludeNested)
            {
                tagIds.AddRange(await _catalog.GetDescendantTagIdsAsync(SelectedNode.TagId));
            }

            var assets = await _catalog.GetAssetsForTagsAsync(tagIds);
            if (assets.Count == 0)
            {
                StatusText = "No images have this tag yet.";
                OrganizePreview.Clear();
                ShowOrganizePanel = false;
                return;
            }

            var preview = await _organize.DryRunAsync(assets, choice);
            OrganizePreview.Clear();
            foreach (var item in preview)
            {
                OrganizePreview.Add(item);
            }

            ShowOrganizePanel = true;
            StatusText = $"Organize preview: {preview.Count} files.";
        });

    [RelayCommand]
    private Task ApplyOrganizeAsync() =>
        ErrorReporter.RunAsync("Apply organize", Notify, async () =>
        {
            if (OrganizePreview.Count == 0)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var applied = await _organize.ApplyAsync(OrganizePreview.ToList());
                await AppServices.Library.RefreshQuietAsync();
                StatusText = $"Moved {applied} files. Last batch is undoable from Library.";
            }
            finally
            {
                IsBusy = false;
            }
        });

    partial void OnSelectedNodeChanged(TagTreeNode? value) => HasSelection = value?.TagId is not null;

    partial void OnIncludeNestedChanged(bool value)
    {
        _ = LoadSelectionAsync();
        _ = LoadMosaicAsync();
    }

    [RelayCommand]
    private void SelectGroup(TagBoardGroup? group)
    {
        if (group is null)
        {
            return;
        }

        _boardSelectionKey = BoardKey(group);
        if (group.ScopeKind is { } scope && Scope != scope)
        {
            Scope = scope;
        }
        else
        {
            ApplyBoardExpansion();
        }

        if (group.GroupId is null)
        {
            return;
        }

        _reorderParentId = null;
        var node = TagTreeBuilder.Find(TagTree, group.GroupId)
            ?? new TagTreeNode { TagId = group.GroupId, Name = group.Name, EffectiveColor = group.Color };
        SelectNode(node);
    }

    partial void OnImpliedQueryChanged(string value) => ApplyImpliedSuggestionFilter(value);

    private void SelectCreated(string tagId)
    {
        var node = TagTreeBuilder.Find(TagTree, tagId);
        if (node is not null)
        {
            SelectNode(node);
        }
    }

    private async Task LoadSelectionAsync()
    {
        HasSelection = SelectedNode?.TagId is not null;
        if (SelectedNode?.TagId is null)
        {
            await UiDispatch.RunAsync(() =>
            {
                SelectedName = "";
                SelectedPriority = 0;
                AssetCount = 0;
                AssetCountLabel = "Select a tag";
                ImpliedTagsSummary = "";
                SelectedEffectiveColor = null;
                SelectedColorLabel = "No color";
                HasCustomColor = false;
                ParentGroups.Clear();
                AvailableGroups.Clear();
                ImpliedTags.Clear();
                ImpliedSuggestions.Clear();
                SelectedIsStarred = false;
                ShowDetails = false;
                _renameBaseline = null;
                _renameBaselineTagId = null;
            });
            return;
        }

        var tag = _tags.FirstOrDefault(t => t.Id == SelectedNode.TagId);
        if (tag is null)
        {
            return;
        }

        var loadedName = tag.Name;
        if (TagSelection.ShouldApplyLoadedName(
                SelectedName, loadedName, _renameBaseline, _renameBaselineTagId, tag.Id))
        {
            SelectedName = loadedName;
            _renameBaseline = loadedName;
            _renameBaselineTagId = tag.Id;
            SelectedPriority = tag.Priority;
            SelectedIsStarred = tag.IsStarred;
        }

        var ids = new List<string> { tag.Id };
        if (IncludeNested)
        {
            ids.AddRange(await _catalog.GetDescendantTagIdsAsync(tag.Id));
        }

        var count = await _catalog.CountAssetsForTagsAsync(ids, AppServices.CurrentProject?.Id);
        var parentIds = _memberships.Where(m => m.ChildId == tag.Id).Select(m => m.ParentId).ToHashSet();
        var parents = _tags.Where(t => parentIds.Contains(t.Id)).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var available = _tags
            .Where(t => t.Id != tag.Id && !parentIds.Contains(t.Id))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var impliedIds = _implications.Where(i => i.TagId == tag.Id).Select(i => i.ImpliedTagId).ToHashSet();
        var implied = _tags.Where(t => impliedIds.Contains(t.Id)).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var colors = CatalogService.MapEffectiveColors(_tags, _memberships);
        await UiDispatch.RunAsync(() =>
        {
            if (SelectedNode?.TagId != tag.Id)
            {
                return;
            }

            if (TagSelection.ShouldApplyLoadedName(
                    SelectedName, loadedName, _renameBaseline, _renameBaselineTagId, tag.Id))
            {
                SelectedName = loadedName;
                _renameBaseline = loadedName;
                _renameBaselineTagId = tag.Id;
            }

            SelectedPriority = tag.Priority;
            SelectedEffectiveColor = colors.GetValueOrDefault(tag.Id);
            HasCustomColor = TagColor.Normalize(tag.Color) is not null;
            SelectedColorLabel = CatalogService.DescribeTagColor(tag, _tags, _memberships);
            AssetCount = count;
            AssetCountLabel = $"{count} images with this tag";
            ParentGroups.Clear();
            foreach (var parent in parents)
            {
                ParentGroups.Add(new TagGroupPick { TagId = parent.Id, Name = parent.Name });
            }

            AvailableGroups.Clear();
            foreach (var candidate in available)
            {
                AvailableGroups.Add(new TagGroupPick { TagId = candidate.Id, Name = candidate.Name });
            }

            ImpliedTags.Clear();
            foreach (var item in implied)
            {
                ImpliedTags.Add(new TagGroupPick { TagId = item.Id, Name = item.Name });
            }

            ImpliedTagsSummary = implied.Count == 0
                ? "No implicit tags"
                : string.Join(", ", implied.Select(t => t.Name));
            ApplyImpliedSuggestionFilter(ImpliedQuery);
            SelectedIsStarred = tag.IsStarred;
            ShowDetails = true;
        });
    }

    private void ApplyImpliedSuggestionFilter(string? query)
    {
        var selectedId = SelectedNode?.TagId;
        var impliedIds = new HashSet<string>(StringComparer.Ordinal);
        if (selectedId is not null)
        {
            foreach (var id in _implications.Where(i => i.TagId == selectedId).Select(i => i.ImpliedTagId))
            {
                impliedIds.Add(id);
            }
        }
        var q = (query ?? "").Trim();
        IEnumerable<Tag> source = _tags.Where(t => t.Id != selectedId && !impliedIds.Contains(t.Id));
        if (q.Length > 0)
        {
            source = source.Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var items = source.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        ImpliedSuggestions.Clear();
        foreach (var tag in items)
        {
            ImpliedSuggestions.Add(new TagGroupPick { TagId = tag.Id, Name = tag.Name });
        }
    }

    private void RebuildTree()
    {
        var keepId = SelectedNode?.TagId;
        TagTreeBuilder.Replace(TagTree, _tags, _memberships);
        ApplyTreeColors(TagTree);
        RebuildBoard();
        if (keepId is not null)
        {
            SelectedNode = TagTreeBuilder.Find(TagTree, keepId);
            HasSelection = SelectedNode?.TagId is not null;
            ShowDetails = HasSelection;
        }
    }

    private void RebuildBoard()
    {
        var colors = _tags.Count == 0 ? null : CatalogService.MapEffectiveColors(_tags, _memberships);
        var groups = TagPanelBuilder.BuildBoard(_tags, _memberships, query: SearchQuery, colors: colors);
        Board.Clear();
        foreach (var group in groups)
        {
            var item = new TagBoardGroup
            {
                GroupId = group.GroupId,
                Name = group.Name,
                Color = group.Color,
                IsUngrouped = group.IsUngrouped,
                ScopeKind = group.ScopeKind,
                AutomationIdOverride = group.AutomationIdOverride,
                IsExpanded = false
            };
            var key = BoardKey(item);
            foreach (var chip in group.Chips)
            {
                item.Chips.Add(new TagChipItem
                {
                    TagId = chip.TagId,
                    Name = chip.Name,
                    EffectiveColor = chip.EffectiveColor,
                    IsStarred = chip.IsStarred,
                    IsFilterSelected = chip.TagId == SelectedNode?.TagId,
                    ParentGroupId = group.GroupId,
                    ImmediateParentId = chip.ParentId,
                    BoardKey = key
                });
            }

            foreach (var letter in TagAlphaIndex.GroupByLetter(item.Chips, chip => chip.Name))
            {
                var section = new TagLetterSection { Letter = letter.Letter };
                foreach (var chip in letter.Items)
                {
                    section.Chips.Add(chip);
                }

                item.Letters.Add(section);
            }

            Board.Add(item);
        }

        ApplyBoardExpansion();
    }

    private void RevealSelectedBoardRow()
    {
        if (SelectedNode?.TagId is { } id)
        {
            _boardSelectionKey = TagBoardExpand.RevealKey(
                _boardSelectionKey,
                id,
                Board.Select(group => new TagBoardExpand.Row(
                    BoardKey(group),
                    group.GroupId,
                    group.Chips.Select(chip => chip.TagId).ToList())).ToList());
        }

        ApplyBoardExpansion();
    }

    private void ApplyBoardExpansion()
    {
        if (Board.Count == 0)
        {
            return;
        }

        var matched = false;
        foreach (var item in Board)
        {
            var expand = BoardKey(item) == _boardSelectionKey;
            item.IsExpanded = expand;
            matched |= expand;
        }

        if (!matched)
        {
            _boardSelectionKey = "all";
            var all = Board.FirstOrDefault(g => g.ScopeKind == TagScope.All) ?? Board[0];
            all.IsExpanded = true;
            _boardSelectionKey = BoardKey(all);
        }

        SelectedBoard = Board.FirstOrDefault(group => group.IsExpanded);
    }

    partial void OnSelectedBoardChanged(TagBoardGroup? value) => OnPropertyChanged(nameof(ChildrenHeader));

    private static string BoardKey(TagBoardGroup group) =>
        group.ScopeKind switch
        {
            TagScope.All => "all",
            TagScope.Ungrouped => "ungrouped",
            TagScope.Starred => "starred",
            _ => group.GroupId ?? "ungrouped"
        };

    private async Task LoadMosaicAsync()
    {
        var epoch = Interlocked.Increment(ref _mosaicEpoch);
        if (SelectedNode?.TagId is null)
        {
            await UiDispatch.RunAsync(() =>
            {
                if (epoch != _mosaicEpoch)
                {
                    return;
                }

                Assets.Clear();
                MosaicGroups.Clear();
                MosaicReset?.Invoke();
            });
            return;
        }

        BeginBusy("Loading…");
        try
        {
            var tagId = SelectedNode.TagId;
            var sets = TagFilter.ExpandEach([tagId], _memberships);
            if (!IncludeNested)
            {
                sets = [new HashSet<string>(StringComparer.Ordinal) { tagId }];
            }

            var projectId = AppServices.CurrentProject?.Id;
            var assets = await _catalog.GetAssetsForTagFilterAsync(
                sets,
                TagFilterMode.Any,
                projectId,
                MosaicSort);
            if (epoch != _mosaicEpoch)
            {
                return;
            }

            var sources = await _catalog.GetSourceFoldersAsync(projectId);
            var sourceCloud = sources.ToDictionary(
                source => source.Id,
                source => GalleryMedia.SourceFolderIsCloud(source.Kind, source.Path),
                StringComparer.Ordinal);
            if (epoch != _mosaicEpoch)
            {
                return;
            }

            var assigned = await _catalog.GetAssignedTagIdsByAssetAsync(assets.Select(a => a.Id));
            if (epoch != _mosaicEpoch)
            {
                return;
            }

            var tags = _tags;
            var memberships = _memberships;
            var items = await Task.Run(() => assets.Select(a =>
            {
                sourceCloud.TryGetValue(a.SourceFolderId, out var cloud);
                return AssetItemMapper.FromAsset(a, _thumbs, cloud);
            }).ToList());
            if (epoch != _mosaicEpoch)
            {
                return;
            }

            var buckets = await Task.Run(() => TagMosaicGroups.Group(
                tagId,
                items,
                item => item.Id,
                assigned,
                tags,
                memberships));
            await UiDispatch.RunAsync(() =>
            {
                if (epoch != _mosaicEpoch)
                {
                    return;
                }

                Assets.Clear();
                MosaicGroups.Clear();
                MosaicReset?.Invoke();
            });

            foreach (var bucket in buckets)
            {
                TagMosaicSection? section = null;
                for (var i = 0; i < bucket.Items.Count; i += MosaicChunkSize)
                {
                    if (epoch != _mosaicEpoch)
                    {
                        return;
                    }

                    var end = Math.Min(i + MosaicChunkSize, bucket.Items.Count);
                    await UiDispatch.RunAsync(() =>
                    {
                        if (epoch != _mosaicEpoch)
                        {
                            return;
                        }

                        section ??= new TagMosaicSection { Header = bucket.Header };
                        if (i == 0)
                        {
                            MosaicGroups.Add(section);
                        }

                        for (var n = i; n < end; n++)
                        {
                            section.Assets.Add(bucket.Items[n]);
                            Assets.Add(bucket.Items[n]);
                        }

                        section.NotifyCount();
                        MosaicChunkAppended?.Invoke();
                    });
                    await UiDispatch.YieldAsync();
                }
            }
        }
        finally
        {
            EndBusy();
        }
    }

    private void StampBoardSelection()
    {
        var id = SelectedNode?.TagId;
        foreach (var group in Board)
        {
            foreach (var chip in group.Chips)
            {
                chip.IsFilterSelected = chip.TagId == id;
            }
        }
    }

    private static string DescribeBatch(TagCreateBatchResult result, string? suffix = null)
    {
        if (result.Created == 0 && result.Existed == 0)
        {
            return "Enter one or more tag names.";
        }

        var text = $"Created {result.Created}, already existed {result.Existed}{suffix}";
        if (result.MembershipRejected > 0)
        {
            text += $", could not nest {result.MembershipRejected}";
        }

        return text + ".";
    }

    private void BeginBusy(string? status = null)
    {
        if (status is not null)
        {
            StatusText = status;
        }

        Interlocked.Increment(ref _busyDepth);
        IsBusy = true;
    }

    private void EndBusy()
    {
        if (Interlocked.Decrement(ref _busyDepth) <= 0)
        {
            Interlocked.Exchange(ref _busyDepth, 0);
            IsBusy = false;
        }
    }

    private void ApplyTreeColors(IEnumerable<TagTreeNode> nodes)
    {
        var colors = CatalogService.MapEffectiveColors(_tags, _memberships);
        var byId = _tags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        ApplyTreeColors(nodes, colors, byId);
    }

    private static void ApplyTreeColors(
        IEnumerable<TagTreeNode> nodes,
        IReadOnlyDictionary<string, string?> colors,
        IReadOnlyDictionary<string, Tag> byId)
    {
        foreach (var node in nodes)
        {
            if (node.TagId is { } id && byId.TryGetValue(id, out var tag))
            {
                node.EffectiveColor = colors.GetValueOrDefault(id);
                node.ColorIsCustom = TagColor.Normalize(tag.Color) is not null;
                node.ColorSourceLabel = node.ColorIsCustom
                    ? "Custom"
                    : node.EffectiveColor is not null ? "Inherited" : "";
            }

            ApplyTreeColors(node.Children, colors, byId);
        }
    }

    private void QueueBackfill(Func<Task<int>> work)
    {
        var epoch = Interlocked.Increment(ref _backfillEpoch);
        _ = Task.Run(() => RunBackfillAsync(work, epoch));
    }

    private async Task RunBackfillAsync(Func<Task<int>> work, int epoch)
    {
        await UiDispatch.RunAsync(() => BackfillStatus = "Updating tagged images…");
        try
        {
            var updated = await work();
            if (epoch != _backfillEpoch)
            {
                return;
            }

            await UiDispatch.RunAsync(() =>
                BackfillStatus = updated == 0
                    ? "No tagged images needed updating."
                    : $"Updated {updated} images.");
            if (AppServices.Library is { } library)
            {
                await library.ReloadAssignedTagsAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (epoch == _backfillEpoch)
            {
                await UiDispatch.RunAsync(() => BackfillStatus = $"Could not update tagged images. {ex.Message}");
            }
        }
    }
}
