using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public TagsViewModel(CatalogService catalog, OrganizeService organize, AccessService access)
    {
        _catalog = catalog;
        _organize = organize;
        _access = access;
    }

    public ObservableCollection<TagTreeNode> TagTree { get; } = [];
    public ObservableCollection<TagGroupPick> ParentGroups { get; } = [];
    public ObservableCollection<TagGroupPick> AvailableGroups { get; } = [];
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

    public Func<IReadOnlyList<TagPath>, Task<OrganizeChoice?>>? RequestOrganizeChoice { get; set; }

    public async Task LoadAsync() => await RefreshAsync();

    public async Task RefreshAsync()
    {
        _tags = (await _catalog.GetTagsAsync()).ToList();
        _memberships = (await _catalog.GetMembershipsAsync()).ToList();
        RebuildTree();
        await LoadSelectionAsync();
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
        if (existing is null)
        {
            await _catalog.CreateTagAsync(NewTagName.Trim());
        }

        NewTagName = "";
        await RefreshAsync();
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
        if (existing is null)
        {
            await _catalog.CreateChildTagAsync(SelectedNode.TagId, NewTagName.Trim());
        }
        else
        {
            var ok = await _catalog.AddMembershipAsync(SelectedNode.TagId, existing.Id);
            if (!ok)
            {
                StatusText = "That membership would create a cycle.";
                return;
            }
        }

        NewTagName = "";
        await RefreshAsync();
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

    private async Task LoadSelectionAsync()
    {
        ParentGroups.Clear();
        AvailableGroups.Clear();
        HasSelection = SelectedNode?.TagId is not null;
        if (SelectedNode?.TagId is null)
        {
            SelectedName = "";
            SelectedPriority = 0;
            AssetCount = 0;
            AssetCountLabel = "Select a tag";
            return;
        }

        var tag = _tags.FirstOrDefault(t => t.Id == SelectedNode.TagId);
        if (tag is null)
        {
            return;
        }

        SelectedName = tag.Name;
        SelectedPriority = tag.Priority;
        var ids = new List<string> { tag.Id };
        if (IncludeNested)
        {
            ids.AddRange(await _catalog.GetDescendantTagIdsAsync(tag.Id));
        }

        AssetCount = await _catalog.CountAssetsForTagsAsync(ids);
        AssetCountLabel = $"{AssetCount} images with this tag";

        var parentIds = _memberships.Where(m => m.ChildId == tag.Id).Select(m => m.ParentId).ToHashSet();
        foreach (var parent in _tags.Where(t => parentIds.Contains(t.Id)).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            ParentGroups.Add(new TagGroupPick { TagId = parent.Id, Name = parent.Name });
        }

        foreach (var candidate in _tags.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (candidate.Id == tag.Id || parentIds.Contains(candidate.Id))
            {
                continue;
            }

            AvailableGroups.Add(new TagGroupPick { TagId = candidate.Id, Name = candidate.Name });
        }
    }

    private void RebuildTree()
    {
        var keepId = SelectedNode?.TagId;
        var byId = _tags.ToDictionary(t => t.Id);
        var children = new Dictionary<string, List<Tag>>(StringComparer.Ordinal);
        var childIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in _memberships)
        {
            if (!byId.TryGetValue(edge.ChildId, out var child))
            {
                continue;
            }

            childIds.Add(edge.ChildId);
            if (!children.TryGetValue(edge.ParentId, out var list))
            {
                list = [];
                children[edge.ParentId] = list;
            }

            list.Add(child);
        }

        TagTree.Clear();
        foreach (var root in _tags.Where(t => !childIds.Contains(t.Id) && children.ContainsKey(t.Id))
                     .OrderByDescending(t => t.Priority)
                     .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            TagTree.Add(BuildNode(root, children, []));
        }

        var ungrouped = _tags.Where(t => !childIds.Contains(t.Id) && !children.ContainsKey(t.Id))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ungrouped.Count > 0)
        {
            var bucket = new TagTreeNode { Name = "Ungrouped", IsUngroupedBucket = true };
            foreach (var tag in ungrouped)
            {
                bucket.Children.Add(new TagTreeNode { TagId = tag.Id, Name = tag.Name });
            }

            TagTree.Add(bucket);
        }

        if (keepId is not null)
        {
            SelectedNode = FindNode(TagTree, keepId);
            HasSelection = SelectedNode?.TagId is not null;
        }
    }

    private static TagTreeNode BuildNode(Tag tag, Dictionary<string, List<Tag>> children, HashSet<string> trail)
    {
        var node = new TagTreeNode { TagId = tag.Id, Name = tag.Name };
        if (!trail.Add(tag.Id))
        {
            return node;
        }

        if (children.TryGetValue(tag.Id, out var kids))
        {
            foreach (var kid in kids.OrderByDescending(t => t.Priority).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                node.Children.Add(BuildNode(kid, children, [.. trail]));
            }
        }

        return node;
    }

    private static TagTreeNode? FindNode(IEnumerable<TagTreeNode> nodes, string tagId)
    {
        foreach (var node in nodes)
        {
            if (node.TagId == tagId)
            {
                return node;
            }

            var child = FindNode(node.Children, tagId);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }
}
