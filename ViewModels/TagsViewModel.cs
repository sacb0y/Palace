using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Data;
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
    private readonly FileOpService _fileOps;
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

    public TagsViewModel(
        CatalogService catalog,
        OrganizeService organize,
        AccessService access,
        ThumbnailService thumbs,
        FileOpService fileOps)
    {
        _catalog = catalog;
        _organize = organize;
        _access = access;
        _thumbs = thumbs;
        _fileOps = fileOps;
        foreach (var hex in TagInk.Presets)
        {
            ColorPresets.Add(new TagColorPreset { Hex = hex });
        }
    }

    public ObservableCollection<TagTreeNode> TagTree { get; } = [];
    public ObservableCollection<TagBoardGroup> Board { get; } = [];

    [ObservableProperty]
    public partial TagBoardGroup? SelectedBoard { get; set; }

    public string ChildrenHeader =>
        SelectedBoard?.ScopeKind == TagScope.Aliases
            ? (string.IsNullOrWhiteSpace(AliasListCaption) ? "Aliases" : $"Aliases ({AliasListCaption})")
            : SelectedBoard?.CountLabel ?? "Tags";
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

    /// <summary>Create-target group for "New tags in [Group ▾]" (null = Ungrouped).</summary>
    [ObservableProperty]
    public partial TagGroupPick? CreateTargetGroup { get; set; }

    public ObservableCollection<TagGroupPick> CreateTargetGroups { get; } = [];

    public ObservableCollection<TagChipItem> RecentlyCreated { get; } = [];

    public ObservableCollection<TagColorPreset> ColorPresets { get; } = [];

    [ObservableProperty]
    public partial bool ShowStatusUndo { get; set; }

    [ObservableProperty]
    public partial string SelectedDescription { get; set; } = "";

    [ObservableProperty]
    public partial string BoardSortLabel { get; set; } = "Name";

    [ObservableProperty]
    public partial string SelectedName { get; set; } = "";

    private readonly HashSet<string> _recentlyCreatedIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingEmptyGroupIds = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _recentTagIds = [];
    private bool _clearRecentlyCreatedOnNextRebuild;

    [ObservableProperty]
    public partial double SelectedPriority { get; set; }

    private int _priorityLoadDepth;

    /// <summary>True while the number box is being filled from the catalog, not from a user edit.</summary>
    public bool IsLoadingPriority => _priorityLoadDepth > 0;

    [ObservableProperty]
    public partial TagGroupPick? SelectedAvailableGroup { get; set; }

    [ObservableProperty]
    public partial string ImpliedQuery { get; set; } = "";

    [ObservableProperty]
    public partial string ImpliedTagsSummary { get; set; } = "";

    [ObservableProperty]
    public partial int AssetCount { get; set; }

    [ObservableProperty]
    public partial string AssetCountLabel { get; set; } = "0 items with this tag";

    public bool CanSaveRename =>
        SelectedNode?.TagId is not null
        && !string.IsNullOrWhiteSpace(SelectedName)
        && !string.Equals(SelectedName.Trim(), _renameBaseline?.Trim(), StringComparison.Ordinal);

    public string RenameError { get; private set; } = "";

    [ObservableProperty]
    public partial bool IncludeNested { get; set; } = true;

    [ObservableProperty]
    public partial MosaicSort MosaicSort { get; set; } = MosaicSortOrder.Default;

    /// <summary>Sort field choices (the direction is <see cref="ToggleSortDirectionCommand"/>).</summary>
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

    public string SortDirectionLabel => MosaicSortOrder.DirectionLabel(MosaicSort);
    public string SortDescendingLabel =>
        MosaicSortOrder.DirectionLabel(MosaicSortOrder.Compose(MosaicSortOrder.FieldOf(MosaicSort), descending: true));
    public string SortAscendingLabel =>
        MosaicSortOrder.DirectionLabel(MosaicSortOrder.Compose(MosaicSortOrder.FieldOf(MosaicSort), descending: false));
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
    public partial bool ShowOrganizePanel { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Create groups and nest tags. The same tag can live in more than one group.";

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial bool IsSelectMode { get; set; }

    [ObservableProperty]
    public partial string SelectionSummary { get; set; } = "";

    private readonly List<AssetItem> _mosaicSelection = [];

    private void Notify(string message) => StatusText = message;

    partial void OnIsSelectModeChanged(bool value) => RefreshMosaicSelectionSummary();

    private void RefreshMosaicSelectionSummary() =>
        SelectionSummary = SelectMode.Summary(IsSelectMode, _mosaicSelection.Count);

    public void SetMosaicSelection(IEnumerable<AssetItem> items)
    {
        _mosaicSelection.Clear();
        _mosaicSelection.AddRange(items.Where(item => !item.IsFolderHeader && !string.IsNullOrEmpty(item.Id)));
        var ids = _mosaicSelection.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var asset in Assets)
        {
            asset.IsSelected = ids.Contains(asset.Id);
        }

        RefreshMosaicSelectionSummary();
    }

    public void ToggleMosaicAsset(AssetItem item)
    {
        if (item.IsFolderHeader || string.IsNullOrEmpty(item.Id))
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
        _mosaicSelection.Clear();
        _mosaicSelection.AddRange(Assets.Where(asset => asset.IsSelected));
        RefreshMosaicSelectionSummary();
    }

    public void ApplyMosaicRange(int from, int to)
    {
        var flags = Assets.Select(asset => asset.IsFolderHeader).ToList();
        var indexes = RangeSelect.ContiguousIndexes(flags, from, to);
        SetMosaicSelection(indexes.Select(i => Assets[i]));
    }

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
    [ObservableProperty]
    public partial GalleryViewModel? OverlayGallery { get; set; }

    [ObservableProperty]
    public partial bool IsGalleryOverlayOpen { get; set; }

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

    public async Task LoadAsync() => await RefreshAsync();

    partial void OnMosaicSortChanged(MosaicSort value)
    {
        OnPropertyChanged(nameof(MosaicSortIndex));
        OnPropertyChanged(nameof(IsSortDescending));
        OnPropertyChanged(nameof(SortDirectionLabel));
        OnPropertyChanged(nameof(SortDescendingLabel));
        OnPropertyChanged(nameof(SortAscendingLabel));
        OnPropertyChanged(nameof(SortButtonLabel));
        if (_suppressMosaicSort)
        {
            return;
        }

        if (AppServices.CurrentProject is { } project)
        {
            ApplicationData.Current.LocalSettings.Values[MosaicSortOrder.SettingsKey(project.Id, MosaicSortView.Tags)] =
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

        var settings = ApplicationData.Current.LocalSettings.Values;
        var next = MosaicSortOrder.Load(key => settings.TryGetValue(key, out var v) ? v : null, projectId, MosaicSortView.Tags);
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
            _aliases = (await _catalog.GetAliasesAsync()).ToList();
            await LoadAliasFileCountsAsync();
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
        _renameBaseline = name;
        _renameBaselineTagId = node.TagId;
        SelectedName = name;
        OnPropertyChanged(nameof(CanSaveRename));
        HasSelection = true;
        ShowDetails = true;
        if (tag is null)
        {
            return;
        }

        SetSelectedPriorityFromCatalog(tag.Priority);
        SelectedIsStarred = tag.IsStarred;
    }

    partial void OnSearchQueryChanged(string value) => RebuildBoard();

    partial void OnScopeChanged(TagScope value) => RebuildBoard();

    [RelayCommand]
    private Task CreateNewTagsAsync() =>
        ErrorReporter.RunAsync("Create tags", Notify, async () =>
        {
            if (string.IsNullOrWhiteSpace(NewTagName))
            {
                return;
            }

            var names = TagNameList.Split(NewTagName);
            if (names.Count == 0)
            {
                return;
            }

            var parentId = string.IsNullOrEmpty(CreateTargetGroup?.TagId)
                ? null
                : CreateTargetGroup!.TagId;
            var projectId = AppServices.CurrentProject?.Id;
            TagCreateBatchResult result;
            if (!string.IsNullOrEmpty(projectId))
            {
                result = await _fileOps.CreateTagsAsync(names, parentId, projectId);
                await AppServices.Library.RefreshUndoStateAsync();
            }
            else
            {
                result = await _catalog.CreateTagsAsync(names, parentId);
                result = new TagCreateBatchResult
                {
                    Created = result.Created,
                    Existed = result.Existed,
                    MembershipRejected = result.MembershipRejected,
                    Tags = result.Tags,
                    CreatedTagIds = result.CreatedTagIds,
                    GroupName = result.GroupName ?? CreateTargetGroup?.Name,
                    Ok = true,
                    Message = result.Created > 0
                        ? TagStatusBar.CreatedInGroup(
                            result.Created,
                            result.GroupName ?? CreateTargetGroup?.Name,
                            result.Tags.Where(t => result.CreatedTagIds.Contains(t.Id))
                                .Select(t => t.Name)
                                .DefaultIfEmpty()
                                .ToList())
                        : TagCreateCopy.Describe(result, parentId is null ? null : " in the group")
                };
            }

            NewTagName = "";
            _reorderParentId = parentId;
            if (parentId is not null)
            {
                _pendingEmptyGroupIds.Remove(parentId);
            }

            StampRecentlyCreated(result.CreatedTagIds);
            await RefreshAsync();
            if (result.Tags.Count > 0)
            {
                SelectCreated(result.Tags[^1].Id);
            }

            StatusText = string.IsNullOrWhiteSpace(result.Message)
                ? TagCreateCopy.Describe(result, parentId is null ? null : " in the group")
                : result.Message;
            ShowStatusUndo = result.Ok && result.BatchId is not null && AppServices.Library.CanUndoFileOp;
            AppServices.Library.ShowUndoToast = false;
        });

    /// <summary>Legacy AutomationId path — same as <see cref="CreateNewTagsAsync"/>.</summary>
    [RelayCommand]
    private Task CreateUngroupedAsync() => CreateNewTagsAsync();

    /// <summary>Legacy AutomationId path — sets create target from selection then creates.</summary>
    [RelayCommand]
    private Task CreateChildAsync() =>
        ErrorReporter.RunAsync("Create child", Notify, async () =>
        {
            if (SelectedNode?.TagId is not null)
            {
                CreateTargetGroup = new TagGroupPick
                {
                    TagId = SelectedNode.TagId,
                    Name = SelectedNode.Name
                };
            }

            await CreateNewTagsAsync();
        });

    [RelayCommand]
    private void ExpandAllBoardGroups()
    {
        foreach (var group in Board.Where(g => g.ScopeKind is null && !g.IsUngrouped))
        {
            group.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void CollapseAllBoardGroups()
    {
        foreach (var group in Board.Where(g => g.ScopeKind is null && !g.IsUngrouped))
        {
            group.IsExpanded = false;
        }
    }

    [RelayCommand]
    private Task CreateGroupAsync() =>
        ErrorReporter.RunAsync("Create group", Notify, async () =>
        {
            var name = "New group";
            var n = 2;
            while (_tags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"New group ({n++})";
            }

            var tag = await _catalog.CreateTagAsync(name);
            _pendingEmptyGroupIds.Add(tag.Id);
            await RefreshAsync();
            CreateTargetGroup = new TagGroupPick { TagId = tag.Id, Name = tag.Name };
            SelectCreated(tag.Id);
            StatusText = $"Created group “{tag.Name}”. Add tags below.";
            ShowStatusUndo = false;
        });

    [RelayCommand]
    private Task ApplyPresetColorAsync(string? hex) =>
        ErrorReporter.RunAsync("Apply preset color", Notify, async () =>
        {
            if (SelectedNode?.TagId is null || string.IsNullOrWhiteSpace(hex))
            {
                return;
            }

            await ApplyColorAsync(TagColor.Normalize(hex) ?? hex);
        });

    private void StampRecentlyCreated(IReadOnlyList<string> createdIds)
    {
        _clearRecentlyCreatedOnNextRebuild = false;
        _recentlyCreatedIds.Clear();
        foreach (var id in createdIds)
        {
            if (!string.IsNullOrEmpty(id))
            {
                _recentlyCreatedIds.Add(id);
            }
        }
    }

    private void LoadRecentTagIds()
    {
        try
        {
            _recentTagIds = RecentTags.Parse(
                ApplicationData.Current.LocalSettings.Values[RecentTags.SettingsKey] as string);
        }
        catch
        {
            _recentTagIds = [];
        }
    }

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
            var tagId = SelectedNode.TagId;
            var itemCount = AssetCount;
            var groupLinks = await _catalog.CountTagGroupLinksAsync(tagId);
            var impact = itemCount > 0 || groupLinks > 0
                ? $" It will be removed from {AssetNouns.Count(itemCount)} and {groupLinks} group/rule link(s)."
                : "";
            var ok = RequestConfirm is null
                || await RequestConfirm(
                    "Delete tag",
                    $"Delete “{name}”?{impact} Implied-tag rules that use it are removed too.",
                    "Delete tag");
            if (!ok)
            {
                return;
            }

            var projectId = AppServices.CurrentProject?.Id;
            if (string.IsNullOrEmpty(projectId))
            {
                await _catalog.DeleteTagAsync(tagId);
                SelectedNode = null;
                await RefreshAsync();
                StatusText = $"Deleted {name}.";
                return;
            }

            var result = await _fileOps.DeleteTagAsync(tagId, projectId);
            SelectedNode = null;
            await RefreshAsync();
            if (result.Ok)
            {
                AppServices.Library.OfferUndoToast(result.Message);
                await AppServices.Library.RefreshUndoStateAsync();
            }

            StatusText = result.Message;
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
        if (item is null || item.IsFolderHeader || Assets.Count == 0)
        {
            return;
        }

        var snapshot = Assets.Where(asset => !asset.IsFolderHeader).ToList();
        if (!GalleryMedia.CanOpen(snapshot.Count, true))
        {
            Notify("Select an asset first.");
            return;
        }

        var index = GalleryMedia.StartIndex(snapshot.Select(a => a.Id).ToList(), item.Id);
        OverlayGallery = new GalleryViewModel(snapshot, index < 0 ? 0 : index, _catalog);
        IsGalleryOverlayOpen = true;
        if (AccessService.WouldHydrateOnOpen(item.Path))
        {
            _ = AppServices.Hydration.HydrateAfterOpenAsync(item.Id);
        }
    }

    [RelayCommand]
    private void CloseOverlay()
    {
        IsGalleryOverlayOpen = false;
        OverlayGallery?.StopGifPlayback();
        OverlayGallery = null;
    }

    [RelayCommand]
    private Task SaveRenameAsync() =>
        ErrorReporter.RunAsync("Save rename", Notify, async () =>
        {
            if (SelectedNode?.TagId is null || string.IsNullOrWhiteSpace(SelectedName))
            {
                return;
            }

            var name = TagNames.Normalize(SelectedName);
            if (!await _catalog.RenameTagAsync(SelectedNode.TagId, name))
            {
                RenameError = TagNames.DuplicateMessage(name);
                OnPropertyChanged(nameof(RenameError));
                StatusText = RenameError + " Choose a different name.";
                if (_renameBaseline is not null)
                {
                    SelectedName = _renameBaseline;
                }

                OnPropertyChanged(nameof(CanSaveRename));
                return;
            }

            RenameError = "";
            OnPropertyChanged(nameof(RenameError));
            _renameBaseline = name;
            SelectedName = name;
            OnPropertyChanged(nameof(CanSaveRename));
            await RefreshAsync();
            StatusText = "Tag renamed.";
        });

    /// <summary>Persist the number box's <c>NewValue</c>, not the still-stale <see cref="SelectedPriority"/>.</summary>
    public void CommitPriorityFromBox(double newValue)
    {
        var commit = TagPriorityEdit.FromBox(newValue, IsLoadingPriority);
        if (!commit.Save || SelectedNode?.TagId is null)
        {
            return;
        }

        if (Math.Abs(SelectedPriority - commit.Value) > 0.001)
        {
            SetSelectedPriorityFromCatalog(commit.Value);
        }

        if (SavePriorityCommand.CanExecute(null))
        {
            SavePriorityCommand.Execute(null);
        }
    }

    private void SetSelectedPriorityFromCatalog(double value)
    {
        _priorityLoadDepth++;
        try
        {
            SelectedPriority = value;
        }
        finally
        {
            _priorityLoadDepth--;
        }
    }

    [RelayCommand]
    private Task SavePriorityAsync() =>
        ErrorReporter.RunAsync("Save priority", Notify, async () =>
        {
            if (SelectedNode?.TagId is null)
            {
                return;
            }

            await _catalog.SetTagPriorityAsync(SelectedNode.TagId, (int)SelectedPriority);
            await RefreshAsync();
            StatusText = "Priority updated.";
        });

    public void RevertRename()
    {
        if (_renameBaseline is not null)
        {
            SelectedName = _renameBaseline;
            RenameError = "";
            OnPropertyChanged(nameof(RenameError));
            OnPropertyChanged(nameof(CanSaveRename));
        }
    }

    partial void OnSelectedNameChanged(string value) => OnPropertyChanged(nameof(CanSaveRename));

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
    private Task SaveDescriptionAsync() =>
        ErrorReporter.RunAsync("Save description", Notify, async () =>
        {
            if (SelectedNode?.TagId is null)
            {
                return;
            }

            await _catalog.SetTagDescriptionAsync(SelectedNode.TagId, SelectedDescription);
            var tag = _tags.FirstOrDefault(t => t.Id == SelectedNode.TagId);
            if (tag is not null)
            {
                tag.Description = string.IsNullOrWhiteSpace(SelectedDescription)
                    ? null
                    : SelectedDescription.Trim();
            }

            StatusText = "Description saved.";
        });

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
            var target = await _catalog.GetOrCreateTagAsync(name);
            var ok = await _catalog.AddImplicationAsync(sourceId, target.Id);
            await UiDispatch.RunAsync(() => ImpliedQuery = "");
            await RefreshAsync();
            StatusText = ok
                ? $"Assigning this tag will also apply {target.Name}."
                : "That implicit tag would create a cycle.";
            // AddImplicationAsync recalculates AssetTag + FTS in the same catalog write (T3 / N1).
            if (ok && AppServices.Library is { } library)
            {
                await library.ReloadAssignedTagsAsync();
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

            await _catalog.RemoveImplicationAsync(SelectedNode.TagId, item.TagId);
            await RefreshAsync();
            StatusText = $"{item.Name} is no longer implied.";
            // RemoveImplicationAsync recalculates AssetTag + FTS in the same catalog write (T3 / N1).
            if (AppServices.Library is { } library)
            {
                await library.ReloadAssignedTagsAsync();
            }
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
                StatusText = "No files have this tag yet.";
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
                var result = await _organize.ApplyAsync(AppServices.CurrentProject.Id, OrganizePreview.ToList());
                OrganizeService.MarkPreviewApplied(OrganizePreview, result);
                await AppServices.Library.RefreshQuietAsync();
                await AppServices.Library.RefreshUndoStateAsync();
                StatusText = result.Ok
                    ? $"{result.Message} Undo from Library (Ctrl+Z)."
                    : result.Message;
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

        if (group.ScopeKind == TagScope.GroupsHeader)
        {
            _ = CreateGroupCommand.ExecuteAsync(null);
            return;
        }

        _boardSelectionKey = BoardKey(group);
        if (group.ScopeKind is TagScope.All or TagScope.Ungrouped or TagScope.Starred
            or TagScope.Recent or TagScope.Aliases)
        {
            if (Scope != group.ScopeKind.Value)
            {
                Scope = group.ScopeKind.Value;
            }
            else
            {
                ApplyBoardExpansion();
            }

            if (group.ScopeKind == TagScope.Ungrouped)
            {
                CreateTargetGroup = null;
            }

            if (group.ScopeKind == TagScope.Aliases)
            {
                RebuildAliasRows();
            }

            return;
        }

        ApplyBoardExpansion();
        if (group.GroupId is null)
        {
            return;
        }

        _reorderParentId = null;
        CreateTargetGroup = new TagGroupPick { TagId = group.GroupId, Name = group.Name };
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
            return;
        }

        foreach (var group in Board)
        {
            var chip = group.Chips.FirstOrDefault(c => c.TagId == tagId);
            if (chip is not null)
            {
                SelectChip(chip);
                return;
            }
        }

        var tag = _tags.FirstOrDefault(t => t.Id == tagId);
        if (tag is not null)
        {
            SelectNode(new TagTreeNode
            {
                TagId = tag.Id,
                Name = tag.Name,
                EffectiveColor = tag.Color,
                IsStarred = tag.IsStarred
            });
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
                SetSelectedPriorityFromCatalog(0);
                AssetCount = 0;
                AssetCountLabel = "Select a tag";
                ImpliedTagsSummary = "";
                SelectedEffectiveColor = null;
                SelectedColorLabel = "No color";
                HasCustomColor = false;
                SelectedDescription = "";
                RecentlyCreated.Clear();
                ParentGroups.Clear();
                AvailableGroups.Clear();
                ImpliedTags.Clear();
                ImpliedSuggestions.Clear();
                SelectedAliases.Clear();
                AlsoKnownAsHeader = "Also known as";
                AliasAddQuery = "";
                ClearAliasDraftUi();
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
            _renameBaseline = loadedName;
            _renameBaselineTagId = tag.Id;
            SelectedName = loadedName;
            OnPropertyChanged(nameof(CanSaveRename));
            SetSelectedPriorityFromCatalog(tag.Priority);
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
                _renameBaseline = loadedName;
                _renameBaselineTagId = tag.Id;
                SelectedName = loadedName;
                OnPropertyChanged(nameof(CanSaveRename));
            }

            SetSelectedPriorityFromCatalog(tag.Priority);
            SelectedEffectiveColor = colors.GetValueOrDefault(tag.Id);
            HasCustomColor = TagColor.Normalize(tag.Color) is not null;
            SelectedColorLabel = CatalogService.DescribeTagColor(tag, _tags, _memberships);
            SelectedDescription = tag.Description ?? "";
            AssetCount = count;
            AssetCountLabel = AssetNouns.WithThisTag(count);
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
            RebuildSelectedAliases();
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
        if (_clearRecentlyCreatedOnNextRebuild)
        {
            _recentlyCreatedIds.Clear();
            _clearRecentlyCreatedOnNextRebuild = false;
        }
        else if (_recentlyCreatedIds.Count > 0)
        {
            // Keep outline for this rebuild; clear on the next one (t2-03b fade).
            _clearRecentlyCreatedOnNextRebuild = true;
        }

        LoadRecentTagIds();
        var colors = _tags.Count == 0 ? null : CatalogService.MapEffectiveColors(_tags, _memberships);
        var groups = TagPanelBuilder.BuildBoard(
            _tags,
            _memberships,
            query: SearchQuery,
            colors: colors,
            recentIds: _recentTagIds,
            extraRootGroupIds: _pendingEmptyGroupIds.ToList(),
            aliasCount: _aliases.Count);
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
                TotalChipCount = group.TotalCount,
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
                    IsRecentlyCreated = _recentlyCreatedIds.Contains(chip.TagId),
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

        RebuildCreateTargets();
        RebuildRecentlyCreatedList();
        RebuildAliasRows();
        ApplyBoardExpansion();
    }

    private void RebuildCreateTargets()
    {
        var keepId = CreateTargetGroup?.TagId;
        CreateTargetGroups.Clear();
        CreateTargetGroups.Add(new TagGroupPick { TagId = "", Name = "Ungrouped" });
        foreach (var group in Board.Where(g => g.ScopeKind is null && !string.IsNullOrEmpty(g.GroupId)))
        {
            CreateTargetGroups.Add(new TagGroupPick { TagId = group.GroupId!, Name = group.Name });
        }

        CreateTargetGroup = string.IsNullOrEmpty(keepId)
            ? null
            : CreateTargetGroups.FirstOrDefault(g => g.TagId == keepId);
        OnPropertyChanged(nameof(NewTagsInHeader));
        OnPropertyChanged(nameof(CreateTargetIsUngrouped));
    }

    private void RebuildRecentlyCreatedList()
    {
        RecentlyCreated.Clear();
        foreach (var id in _recentlyCreatedIds)
        {
            var tag = _tags.FirstOrDefault(t => t.Id == id);
            if (tag is null)
            {
                continue;
            }

            var colors = CatalogService.MapEffectiveColors(_tags, _memberships);
            RecentlyCreated.Add(new TagChipItem
            {
                TagId = tag.Id,
                Name = tag.Name,
                EffectiveColor = colors.GetValueOrDefault(tag.Id) ?? tag.Color,
                IsStarred = tag.IsStarred,
                IsRecentlyCreated = true,
                CountBadge = "0"
            });
        }

        OnPropertyChanged(nameof(HasRecentlyCreated));
    }

    public bool HasRecentlyCreated => RecentlyCreated.Count > 0;

    public string NewTagsInHeader =>
        TagBoardShell.NewTagsHeader(CreateTargetGroup?.Name, CreateTargetIsUngrouped);

    public bool CreateTargetIsUngrouped =>
        CreateTargetGroup is null || string.IsNullOrEmpty(CreateTargetGroup.TagId);

    partial void OnCreateTargetGroupChanged(TagGroupPick? value)
    {
        OnPropertyChanged(nameof(NewTagsInHeader));
        OnPropertyChanged(nameof(CreateTargetIsUngrouped));
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

    partial void OnSelectedBoardChanged(TagBoardGroup? value)
    {
        IsAliasesView = value?.ScopeKind == TagScope.Aliases;
        OnPropertyChanged(nameof(ChildrenHeader));
        if (IsAliasesView)
        {
            RebuildAliasRows();
        }
    }

    private static string BoardKey(TagBoardGroup group) =>
        group.ScopeKind switch
        {
            TagScope.All => "all",
            TagScope.Ungrouped => "ungrouped",
            TagScope.Starred => "starred",
            TagScope.Recent => "recent",
            TagScope.Aliases => "aliases",
            TagScope.GroupsHeader => "groups-header",
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
                _mosaicSelection.Clear();
                RefreshMosaicSelectionSummary();
                MosaicReset?.Invoke();
            });
            return;
        }

        BeginBusy("Loading…");
        try
        {
            var tagId = SelectedNode.TagId;
            var aliasMap = (await _catalog.GetAliasesAsync())
                .ToDictionary(a => a.AliasTagId, a => a.IdealTagId, StringComparer.Ordinal);
            // Nested: ideal + membership descendants. Flat: still alias-expand (ideal + siblings).
            var filterMemberships = IncludeNested ? _memberships : (IReadOnlyList<TagMembership>)[];
            var sets = TagFilter.ExpandEach([tagId], filterMemberships, aliasMap);

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
                _mosaicSelection.Clear();
                RefreshMosaicSelectionSummary();
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

    private string? _statusBeforeBusy;

    private void BeginBusy(string? status = null)
    {
        if (Interlocked.Increment(ref _busyDepth) == 1)
        {
            _statusBeforeBusy = StatusText;
        }

        // Keep "Loading…" on PrgTags only — do not stick it in the status line.
        if (status is not null
            && !status.Equals("Loading…", StringComparison.Ordinal)
            && !status.Equals("Loading...", StringComparison.Ordinal))
        {
            StatusText = status;
        }

        IsBusy = true;
    }

    private void EndBusy()
    {
        if (Interlocked.Decrement(ref _busyDepth) <= 0)
        {
            Interlocked.Exchange(ref _busyDepth, 0);
            IsBusy = false;
            if (StatusText is "Loading…" or "Loading...")
            {
                StatusText = _statusBeforeBusy ?? "";
            }

            _statusBeforeBusy = null;
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

}
