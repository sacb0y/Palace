using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;

namespace Palace.ViewModels;

public partial class TagsViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly OrganizeService _organize;
    private readonly AccessService _access;
    private List<Tag> _tags = [];
    private List<TagMembership> _memberships = [];
    private List<TagImplication> _implications = [];

    public TagsViewModel(CatalogService catalog, OrganizeService organize, AccessService access)
    {
        _catalog = catalog;
        _organize = organize;
        _access = access;
    }

    public ObservableCollection<TagTreeNode> TagTree { get; } = [];
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
    public partial bool ShowOrganizePanel { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Create groups and nest tags. The same tag can live in more than one group.";

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial string? SelectedEffectiveColor { get; set; }

    [ObservableProperty]
    public partial string SelectedColorLabel { get; set; } = "No color";

    [ObservableProperty]
    public partial bool HasCustomColor { get; set; }

    [ObservableProperty]
    public partial string BackfillStatus { get; set; } = "";

    public Func<IReadOnlyList<TagPath>, Task<OrganizeChoice?>>? RequestOrganizeChoice { get; set; }

    private int _backfillEpoch;

    public async Task LoadAsync() => await RefreshAsync();

    public async Task RefreshAsync()
    {
        _tags = (await _catalog.GetTagsAsync()).ToList();
        _memberships = (await _catalog.GetMembershipsAsync()).ToList();
        _implications = (await _catalog.GetImplicationsAsync()).ToList();
        await UiDispatch.RunAsync(RebuildTree);
        await LoadSelectionAsync();
        if (AppServices.Library is { } library)
        {
            await library.ReloadTagCatalogAsync();
            await library.ReloadAssignedTagsAsync();
        }
    }

    public void SelectNode(TagTreeNode? node)
    {
        SelectedNode = node;
        _ = LoadSelectionAsync();
    }

    [RelayCommand]
    private async Task CreateUngroupedAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTagName))
        {
            return;
        }

        var existing = await _catalog.FindTagByNameAsync(NewTagName);
        var tag = existing ?? await _catalog.CreateTagAsync(NewTagName.Trim());
        NewTagName = "";
        await RefreshAsync();
        SelectCreated(tag.Id);
        StatusText = existing is null ? "Created an ungrouped tag." : "That tag already exists.";
    }

    [RelayCommand]
    private async Task CreateChildAsync()
    {
        if (SelectedNode?.TagId is null || string.IsNullOrWhiteSpace(NewTagName))
        {
            return;
        }

        var existing = await _catalog.FindTagByNameAsync(NewTagName);
        Tag tag;
        if (existing is null)
        {
            tag = await _catalog.CreateChildTagAsync(SelectedNode.TagId, NewTagName.Trim());
        }
        else
        {
            var ok = await _catalog.AddMembershipAsync(SelectedNode.TagId, existing.Id);
            if (!ok)
            {
                StatusText = "That membership would create a cycle.";
                return;
            }

            tag = existing;
        }

        NewTagName = "";
        await RefreshAsync();
        SelectCreated(tag.Id);
        StatusText = "Nested tag under the selected group.";
    }

    [RelayCommand]
    private async Task SaveRenameAsync()
    {
        if (SelectedNode?.TagId is null || string.IsNullOrWhiteSpace(SelectedName))
        {
            return;
        }

        await _catalog.RenameTagAsync(SelectedNode.TagId, SelectedName);
        await _catalog.SetTagPriorityAsync(SelectedNode.TagId, (int)SelectedPriority);
        await RefreshAsync();
        StatusText = "Tag updated.";
    }

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
    private async Task ClearColorAsync()
    {
        if (SelectedNode?.TagId is null)
        {
            return;
        }

        await _catalog.SetTagColorAsync(SelectedNode.TagId, null);
        await RefreshAsync();
        StatusText = "Tag color cleared; it will inherit again.";
    }

    [RelayCommand]
    private async Task AddToGroupAsync()
    {
        if (SelectedNode?.TagId is null || SelectedAvailableGroup is null)
        {
            return;
        }

        var ok = await _catalog.AddMembershipAsync(SelectedAvailableGroup.TagId, SelectedNode.TagId);
        StatusText = ok ? $"Also grouped under {SelectedAvailableGroup.Name}." : "That membership would create a cycle.";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveFromGroupAsync(TagGroupPick? group)
    {
        if (SelectedNode?.TagId is null || group is null)
        {
            return;
        }

        await _catalog.RemoveMembershipAsync(group.TagId, SelectedNode.TagId);
        await RefreshAsync();
        StatusText = $"Removed from {group.Name}.";
    }

    [RelayCommand]
    private async Task AddImpliedAsync()
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
    }

    [RelayCommand]
    private async Task RemoveImpliedAsync(TagGroupPick? item)
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
    }

    [RelayCommand]
    private async Task PreviewOrganizeAsync()
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
            await AppServices.Library.RefreshQuietAsync();
            StatusText = $"Moved {applied} files. Last batch is undoable from Library.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedNodeChanged(TagTreeNode? value) => HasSelection = value?.TagId is not null;

    partial void OnIncludeNestedChanged(bool value) => _ = LoadSelectionAsync();

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
            });
            return;
        }

        var tag = _tags.FirstOrDefault(t => t.Id == SelectedNode.TagId);
        if (tag is null)
        {
            return;
        }

        var ids = new List<string> { tag.Id };
        if (IncludeNested)
        {
            ids.AddRange(await _catalog.GetDescendantTagIdsAsync(tag.Id));
        }

        var count = await _catalog.CountAssetsForTagsAsync(ids);
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
            SelectedName = tag.Name;
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
        if (keepId is not null)
        {
            SelectedNode = TagTreeBuilder.Find(TagTree, keepId);
            HasSelection = SelectedNode?.TagId is not null;
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
