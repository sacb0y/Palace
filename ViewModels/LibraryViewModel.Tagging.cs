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

/// <summary>Library: assigning tags, tag picks / suggestions and the assign panel.</summary>
public partial class LibraryViewModel
{
    [RelayCommand]
    private Task AssignFromQueryAsync() =>
        ErrorReporter.RunAsync("Assign from query", Notify, async () =>
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
                    var created = await _catalog.GetOrCreateTagAsync(query);
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
        });

    [RelayCommand]
    private Task CreateAndAssignTagAsync() =>
        ErrorReporter.RunAsync("Create and assign tag", Notify, async () =>
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
                var tag = await _catalog.GetOrCreateTagAsync(name);
                await AssignToSelectionAsync(targets, tag.Id, tag.Name, TagSource.Manual);
            }

            await UiDispatch.RunAsync(() => NewTagName = "");
            await LoadPreviewAsync(SelectedAsset);
            await ReloadTagCatalogAsync();
        });

    [RelayCommand]
    private Task RemoveAssignedTagAsync(AssignedTagItem? item) =>
        ErrorReporter.RunAsync("Remove assigned tag", Notify, async () =>
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
        });

    [RelayCommand]
    private Task AcceptSuggestionAsync(PromptSuggestion? suggestion) =>
        ErrorReporter.RunAsync("Accept suggestion", Notify, async () =>
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
                var created = await _catalog.GetOrCreateTagAsync(suggestion.Token);
                tagId = created.Id;
                tagName = created.Name;
            }

            await AssignToSelectionAsync(targets, tagId, tagName, TagSource.Prompt);
            await LoadPreviewAsync(SelectedAsset);
            await ReloadTagCatalogAsync();
        });

    [RelayCommand]
    private void FocusAssignTag()
    {
        RequestFocusAssignTag?.Invoke();
        RequestOpenAssignPanel?.Invoke();
        RebuildAssignPanel();
    }

    [RelayCommand]
    private Task ToggleAssignChipAsync(TagChipItem? chip) =>
        ErrorReporter.RunAsync("Toggle assign chip", Notify, async () =>
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
        });

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
}
