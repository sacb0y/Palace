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

/// <summary>Library: search, tag filters and filter chips.</summary>
public partial class LibraryViewModel
{
    [RelayCommand]
    private Task SearchAsync() =>
        ErrorReporter.RunAsync("Search", Notify, async () =>
        {
            await ApplyFilterAsync();
        });

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
    private void ToggleFilterChip(TagChipItem? chip)
    {
        if (chip is null || string.IsNullOrEmpty(chip.TagId))
        {
            return;
        }

        var node = TagTreeBuilder.Find(TagTree, chip.TagId)
            ?? new TagTreeNode
            {
                TagId = chip.TagId,
                Name = chip.Name,
                EffectiveColor = chip.EffectiveColor,
                IsStarred = chip.IsStarred
            };
        ToggleFilterTag(node);
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
            var hasFolderPath = !string.IsNullOrWhiteSpace(folderPath);

            IReadOnlyList<Asset> assets;
            if (IsTagBrowse && filterIds.Count > 0)
            {
                var memberships = _memberships.Count > 0 ? _memberships : (await _catalog.GetMembershipsAsync()).ToList();
                var sets = TagFilter.ExpandEach(filterIds, memberships);
                assets = await _catalog.GetAssetsForTagFilterAsync(sets, matchMode, projectId, MosaicSort);
            }
            else if (folderPath is not null)
            {
                assets = await _catalog.GetAssetsAsync(
                    folderPrefix: folderPath,
                    projectId: projectId,
                    sort: MosaicSort);
            }
            else
            {
                assets = await _catalog.GetAssetsAsync(projectId: projectId, sort: MosaicSort);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var hits = await _catalog.SearchAsync(search, projectId, MosaicSort);
                var set = hits.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
                assets = assets.Where(a => set.Contains(a.Id)).ToList();
            }

            if (epoch != _filterEpoch)
            {
                return;
            }

            var items = await Task.Run(() => BuildMosaicItems(
                assets,
                FolderGroups.ShouldGroup(tagBrowse, !string.IsNullOrWhiteSpace(search), hasFolderPath),
                folderName ?? "",
                folderPath ?? ""));
            if (epoch != _filterEpoch)
            {
                return;
            }

            var assetCount = items.Count(item => !item.IsFolderHeader);
            var joiner = matchMode == TagFilterMode.All
                ? " + "
                : matchMode == TagFilterMode.None ? " except " : " or ";
            var doneStatus = !string.IsNullOrWhiteSpace(search)
                ? $"{assetCount} search results"
                : tagBrowse
                    ? $"{assetCount} tagged {string.Join(joiner, filterNames)}"
                    : folderPath is not null
                        ? $"{assetCount} in {folderName}"
                        : assetCount == 0
                            ? "Add a folder to start your library."
                            : $"{assetCount} assets";

            await PopulateMosaicAsync(items, epoch, doneStatus);
        }
        finally
        {
            EndBusy();
        }
    }

    private void SyncFilterFlags()
    {
        HasTagFilters = SelectedFilterTags.Count > 0;
        ShowTagFilterChips = HasTagFilters && IsTagBrowse;
        TagFilterSummary = SelectedFilterTags.Count == 0
            ? ""
            : string.Join(TagFilterMode == TagFilterMode.All ? " + " : TagFilterMode == TagFilterMode.None ? " except " : " or ",
                SelectedFilterTags.Select(t => t.Name));
        RebuildBrowseTags();
    }

    private void RebuildBrowseTags()
    {
        var selected = SelectedFilterTags.Select(t => t.TagId).ToHashSet(StringComparer.Ordinal);
        var nodes = TagBrowseMosaic.Flatten(TagTree, node => node.TagId, node => node.Children);
        BrowseTags.Clear();
        foreach (var node in nodes)
        {
            BrowseTags.Add(new TagChipItem
            {
                TagId = node.TagId ?? "",
                Name = node.Name,
                EffectiveColor = node.EffectiveColor,
                IsStarred = node.IsStarred,
                IsFilterSelected = node.TagId is { } id && selected.Contains(id),
                AutomationPrefix = "BtnTagChip_"
            });
        }
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
}
