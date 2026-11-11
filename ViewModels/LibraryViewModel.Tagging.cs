using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Windows.Storage;

namespace Palace.ViewModels;

/// <summary>Library T2: Add tags box (paste pending chips / typed autocomplete), status-bar Undo, bulk remove.</summary>
public partial class LibraryViewModel
{
    private bool _suppressTagQueryHandler;
    private Dictionary<string, string> _aliasToIdeal = new(StringComparer.Ordinal);

    /// <summary>
    /// Page shows the Remove tags… dialog; returns checked tag ids or null when cancelled.
    /// </summary>
    public Func<Task<IReadOnlyList<string>?>>? RequestRemoveTagsDialog { get; set; }

    /// <summary>
    /// Page shows the &gt;10 chip-anchored Remove confirm flyout (t2-04a). True = Remove.
    /// </summary>
    public Func<AssignedTagItem, int, Task<bool>>? RequestChipRemoveConfirm { get; set; }

    /// <summary>
    /// Page shows the &gt;10 Remove from all confirm flyout anchored to the bar button.
    /// </summary>
    public Func<string, int, Task<bool>>? RequestBarRemoveConfirm { get; set; }

    [RelayCommand]
    private Task AssignFromQueryAsync() => ApplyPendingTagsAsync();

    [RelayCommand]
    private Task CreateAndAssignTagAsync() => ApplyPendingTagsAsync();

    [RelayCommand]
    private Task ApplyPendingTagsAsync() =>
        ErrorReporter.RunAsync("Add tags", Notify, async () =>
        {
            var targets = TagTargets();
            if (targets.Count == 0)
            {
                return;
            }

            // Absorb any typed remnant that has not become a pending chip yet.
            await AbsorbTypedQueryIntoPendingAsync();
            if (PendingTags.Count == 0)
            {
                return;
            }

            var pending = PendingTags.ToList();
            var createdCount = 0;
            var createdTags = new List<(string TagId, string TagName)>();
            var tags = new List<(string TagId, string TagName, TagSource Source)>();
            foreach (var chip in pending)
            {
                if (chip.TagId is { Length: > 0 } id)
                {
                    tags.Add((id, chip.DisplayName, TagSource.Manual));
                    continue;
                }

                var (createdTag, wasCreated) = await _catalog.GetOrCreateTagResultAsync(chip.TypedName);
                tags.Add((createdTag.Id, createdTag.Name, TagSource.Manual));
                if (wasCreated)
                {
                    createdCount++;
                    createdTags.Add((createdTag.Id, createdTag.Name));
                }
            }

            // Deduplicate by ideal id (aliases already resolved when pending was built).
            var unique = new Dictionary<string, (string TagId, string TagName, TagSource Source)>(StringComparer.Ordinal);
            foreach (var row in tags)
            {
                unique[row.TagId] = row;
            }

            await AssignManyToSelectionAsync(
                targets, unique.Values.ToList(), createdCount, createdTags);
            await UiDispatch.RunAsync(ClearPendingTags);
            await LoadPreviewAsync(SelectedAsset);
            await ReloadTagCatalogAsync();
        });

    [RelayCommand]
    private void ClearPendingTags()
    {
        PendingTags.Clear();
        SetTagQuerySilent("");
        SelectedPickTag = null;
        RefreshPendingChrome();
        ApplyTagSuggestionFilter("");
    }

    [RelayCommand]
    private void RemovePendingTag(PendingTagChip? chip)
    {
        if (chip is null)
        {
            return;
        }

        PendingTags.Remove(chip);
        RefreshPendingChrome();
    }

    [RelayCommand]
    private Task RemoveAssignedTagAsync(AssignedTagItem? item) =>
        ErrorReporter.RunAsync("Remove assigned tag", Notify, async () =>
        {
            var targets = TagTargets();
            if (targets.Count == 0 || item is null || !item.ShowRemove)
            {
                return;
            }

            var affected = TagRemoveConfirm.AffectedFileCount(
                item.IsPartial, item.AppliedCount, Math.Max(targets.Count, item.SelectionTotal));
            if (TagRemoveConfirm.NeedsConfirm(affected))
            {
                var ok = RequestChipRemoveConfirm is not null
                    && await RequestChipRemoveConfirm(item, affected);
                if (!ok)
                {
                    return;
                }
            }

            await RemoveTagsFromSelectionAsync(
                targets,
                [(item.TagId, item.TagName)],
                affectedFileCount: affected);
        });

    [RelayCommand]
    private void ToggleAssignedChipSelection(AssignedTagItem? item)
    {
        if (item is null || !item.ShowRemove)
        {
            return;
        }

        item.IsChipSelected = !item.IsChipSelected;
        RefreshRemoveFromAllBar();
    }

    [RelayCommand]
    private Task RemoveSelectedAssignedTagsAsync() =>
        ErrorReporter.RunAsync("Remove from all", Notify, async () =>
        {
            var targets = TagTargets();
            var selected = AssignedTags.Where(t => t.IsChipSelected && t.ShowRemove).ToList();
            if (targets.Count == 0 || selected.Count == 0)
            {
                return;
            }

            var affected = TagRemoveConfirm.UnionFileCount(selected.Select(t => t.AssetIds));
            if (affected <= 0)
            {
                affected = selected.Sum(t =>
                    TagRemoveConfirm.AffectedFileCount(
                        t.IsPartial, t.AppliedCount, Math.Max(targets.Count, t.SelectionTotal)));
            }

            if (TagRemoveConfirm.NeedsConfirm(affected))
            {
                var names = string.Join(", ", selected.Select(t => t.TagName));
                var ok = RequestBarRemoveConfirm is not null
                    && await RequestBarRemoveConfirm(names, affected);
                if (!ok)
                {
                    return;
                }
            }

            await RemoveTagsFromSelectionAsync(
                targets,
                selected.Select(t => (t.TagId, t.TagName)).ToList(),
                affectedFileCount: affected);
        });

    [RelayCommand]
    private Task OpenRemoveTagsDialogAsync() =>
        ErrorReporter.RunAsync("Remove tags", Notify, async () =>
        {
            var targets = TagTargets();
            if (targets.Count == 0)
            {
                return;
            }

            RebuildRemovableTags(targets.Count);
            if (RemovableTags.Count == 0 || RequestRemoveTagsDialog is null)
            {
                return;
            }

            var picked = await RequestRemoveTagsDialog();
            if (picked is null || picked.Count == 0)
            {
                return;
            }

            var rows = RemovableTags
                .Where(t => picked.Contains(t.TagId, StringComparer.Ordinal))
                .Select(t => (t.TagId, t.Name))
                .ToList();
            if (rows.Count == 0)
            {
                return;
            }

            await RemoveTagsFromSelectionAsync(targets, rows, affectedFileCount: targets.Count);
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
            List<(string TagId, string TagName)>? createdTags = null;
            var createdCount = 0;
            if (suggestion.ExistingTagId is not null)
            {
                tagId = suggestion.ExistingTagId;
                tagName = suggestion.ExistingTagName ?? suggestion.Token;
            }
            else
            {
                var (created, wasCreated) = await _catalog.GetOrCreateTagResultAsync(suggestion.Token);
                tagId = created.Id;
                tagName = created.Name;
                if (wasCreated)
                {
                    createdCount = 1;
                    createdTags = [(tagId, tagName)];
                }
            }

            await AssignManyToSelectionAsync(
                targets, [(tagId, tagName, TagSource.Prompt)], createdCount, createdTags);
            await LoadPreviewAsync(SelectedAsset);
            await ReloadTagCatalogAsync();
        });

    [RelayCommand]
    private void FocusAssignTag()
    {
        RefreshPendingChrome();
        // T2/T8: one Add tags box — focus it (legacy FlyAssignTags removed).
        RequestFocusAssignTag?.Invoke();
    }

    public Task ReloadAssignedTagsAsync() => LoadPreviewAsync(SelectedAsset);

    public async Task ReloadTagCatalogAsync()
    {
        var tags = await _catalog.GetTagsAsync();
        var memberships = await _catalog.GetMembershipsAsync();
        var aliases = await _catalog.GetAliasesAsync();
        var implications = await _catalog.GetImplicationsAsync();
        var useCounts = await _catalog.GetAssetTagCountsAsync();
        var pickItems = BuildTagPicks(tags, memberships, useCounts);
        var selectedTagId = SelectedTag?.TagId;
        var filterIds = SelectedFilterTags.Select(t => t.TagId).ToList();
        await UiDispatch.RunAsync(() =>
        {
            _tags = tags.ToList();
            _memberships = memberships.ToList();
            _implications = implications.ToList();
            _aliasToIdeal = aliases.ToDictionary(
                a => a.AliasTagId, a => a.IdealTagId, StringComparer.Ordinal);
            TagTreeBuilder.Replace(TagTree, tags, memberships);
            ApplyLibraryTreeColors();
            RestoreFilterSelection(filterIds);
            if (IsTagBrowse)
            {
                SelectedTag = selectedTagId is null ? null : TagTreeBuilder.Find(TagTree, selectedTagId);
            }

            ReplaceTagPicks(pickItems);
            RebuildBrowseTags();
            RefreshPendingChrome();
            RefreshAssignedTagsHeader();
            RefreshRemoveFromAllBar();
        });
    }

    partial void OnTagQueryChanged(string value)
    {
        if (_suppressTagQueryHandler)
        {
            return;
        }

        _ = ErrorReporter.RunAsync("Add tags input", Notify, () => HandleTagQueryChangedAsync(value));
    }

    private async Task HandleTagQueryChangedAsync(string value)
    {
        if (TagAddInput.ContainsSeparator(value))
        {
            var (committed, remainder) = TagAddInput.CommitOnSeparator(null, value);
            // Clear the box immediately so the AutoSuggestBox can sync before async resolves.
            SetTagQuerySilent(remainder);
            ShowTagSuggestions = false;
            TagSuggestions.Clear();
            foreach (var name in committed)
            {
                await AddPendingNameAsync(name);
            }

            TagSuggestionsSummary = PendingTags.Count > 0
                ? "Pending chips — Enter applies"
                : "Type to find a tag";
            RefreshPendingChrome();
            return;
        }

        ApplyTagSuggestionFilter(value);
        ShowTagSuggestions = TagAddInput.OpensSuggestions(value) && TagSuggestions.Count > 0;
        RefreshPendingChrome();
    }

    private async Task AbsorbTypedQueryIntoPendingAsync()
    {
        var typed = (TagQuery ?? "").Trim();
        if (typed.Length == 0)
        {
            return;
        }

        if (SelectedPickTag is not null
            && (SelectedPickTag.Name.Equals(typed, StringComparison.OrdinalIgnoreCase)
                || SelectedPickTag.Display.Equals(typed, StringComparison.OrdinalIgnoreCase)))
        {
            await AddPendingNameAsync(SelectedPickTag.Name);
        }
        else
        {
            await AddPendingNameAsync(typed);
        }

        SetTagQuerySilent("");
        SelectedPickTag = null;
        RefreshPendingChrome();
    }

    private async Task AddPendingNameAsync(string rawName)
    {
        var name = rawName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        // Deduplicate pending by typed name / ideal id (case-insensitive).
        if (PendingTags.Any(p =>
                p.TypedName.Equals(name, StringComparison.OrdinalIgnoreCase)
                || p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var match = FindTagPick(name) ?? await ResolveTagByNameAsync(name);
        if (match is null)
        {
            PendingTags.Add(new PendingTagChip
            {
                TypedName = name,
                DisplayName = name,
                IsNew = true,
                EffectiveColor = null
            });
            return;
        }

        var ideal = await _catalog.GetIdealTagAsync(match.TagId);
        var idealId = ideal?.Id ?? match.TagId;
        var idealName = ideal?.Name ?? match.Name;
        if (PendingTags.Any(p => p.TagId == idealId))
        {
            return;
        }

        var isAlias = !string.Equals(match.TagId, idealId, StringComparison.Ordinal)
            || !match.Name.Equals(idealName, StringComparison.OrdinalIgnoreCase);
        var colors = _tags.Count == 0
            ? null
            : CatalogService.MapEffectiveColors(_tags, _memberships);
        PendingTags.Add(new PendingTagChip
        {
            TypedName = name,
            DisplayName = idealName,
            TagId = idealId,
            AliasName = isAlias ? match.Name : null,
            IsNew = false,
            EffectiveColor = colors?.GetValueOrDefault(idealId) ?? match.EffectiveColor
        });
    }

    private async Task<TagPickItem?> ResolveTagByNameAsync(string name)
    {
        var tag = await _catalog.FindTagByNameAsync(name);
        if (tag is null)
        {
            return null;
        }

        var colors = _tags.Count == 0
            ? null
            : CatalogService.MapEffectiveColors(_tags, _memberships);
        return new TagPickItem
        {
            TagId = tag.Id,
            Name = tag.Name,
            Display = tag.Name,
            EffectiveColor = colors?.GetValueOrDefault(tag.Id)
        };
    }

    private void SetTagQuerySilent(string value)
    {
        _suppressTagQueryHandler = true;
        try
        {
            TagQuery = value;
        }
        finally
        {
            _suppressTagQueryHandler = false;
        }
    }

    private void RefreshPendingChrome()
    {
        HasPendingTags = PendingTags.Count > 0;
        var newCount = PendingTags.Count(p => p.IsNew);
        var aliasCount = PendingTags.Count(p => p.IsAlias);
        ShowPendingSummary = HasPendingTags;
        PendingSummary = HasPendingTags
            ? TagAddInput.PasteSummary(PendingTags.Count, newCount, aliasCount)
            : "";
        var targets = TagTargets().Count;
        ApplyPendingLabel = TagAddInput.ApplyButtonLabel(PendingTags.Count, targets);
        ApplyPendingHint = TagAddInput.ApplyHint;
        CanApplyPendingTags = targets > 0 && PendingTags.Count > 0;
        RefreshAssignedTagsHeader();
    }

    private void RefreshAssignedTagsHeader()
    {
        var n = TagTargets().Count;
        AssignedTagsHeader = "Tags";
        AssignedTagsHeaderSuffix = n == 1
            ? TagAddInput.SingleImageHeaderSuffix(AssignedTags.Count, ImpliedAssignedTags.Count)
            : TagAddInput.AssignedTagsHeaderSuffix(n);
        // t8-03: single-image uses ⋯ (Remove tags… inside); multi keeps the header link.
        ShowTagsMoreMenu = n == 1;
        ShowRemoveTags = AssignedTags.Count > 0;
        ShowRemoveTagsLink = n > 1 && ShowRemoveTags;
    }

    private void RefreshRemoveFromAllBar()
    {
        var selected = AssignedTags.Where(t => t.IsChipSelected && t.ShowRemove).ToList();
        ShowRemoveFromAllBar = selected.Count > 0;
        if (!ShowRemoveFromAllBar)
        {
            RemoveFromAllSummary = "";
            return;
        }

        var files = TagRemoveConfirm.UnionFileCount(selected.Select(t => t.AssetIds));
        if (files <= 0)
        {
            files = selected.Sum(t =>
                TagRemoveConfirm.AffectedFileCount(t.IsPartial, t.AppliedCount, t.SelectionTotal));
        }

        RemoveFromAllSummary = TagRemoveConfirm.SelectedBarLabel(selected.Count, files);
    }

    private void RebuildRemovableTags(int selectionTotal)
    {
        RemovableTags.Clear();
        // t2-04b: Sort: most used (then A–Z) — not alphabetical-only.
        var ordered = TagRemoveFlyout.OrderMostUsed(
            AssignedTags.Where(t => t.ShowRemove),
            t => t.AppliedCount > 0 ? t.AppliedCount : selectionTotal,
            t => t.TagName);
        foreach (var tag in ordered)
        {
            RemovableTags.Add(new RemovableTagPick
            {
                TagId = tag.TagId,
                Name = tag.TagName,
                EffectiveColor = tag.EffectiveColor,
                AppliedCount = tag.AppliedCount > 0 ? tag.AppliedCount : selectionTotal,
                SelectionTotal = selectionTotal,
                IsChecked = false
            });
        }
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
        if (!TagAddInput.OpensSuggestions(query))
        {
            TagSuggestions.Clear();
            ShowTagSuggestions = false;
            TagSuggestionsSummary = PendingTags.Count > 0
                ? "Pending chips — Enter applies"
                : "Type to find a tag";
            return;
        }

        var q = (query ?? "").Trim();
        var byId = _tagPicks.ToDictionary(t => t.TagId, StringComparer.Ordinal);
        var assignedIds = AssignedTags.Select(t => t.TagId)
            .Concat(PendingTags.Where(p => p.TagId is not null).Select(p => p.TagId!));
        var ranked = TagSuggest.Rank(
            _tagPicks.Select(t => new TagSuggest.Candidate(t.TagId, t.Name, t.Display)),
            q,
            excludeTagIds: assignedIds);

        // Clone rows so match/alias chrome never mutates the shared AllTags picks.
        var items = new List<TagPickItem>();
        foreach (var c in ranked)
        {
            if (!byId.TryGetValue(c.TagId, out var source))
            {
                continue;
            }

            var idealId = TagAliasResolve.Ideal(source.TagId, _aliasToIdeal);
            var isAlias = !string.Equals(idealId, source.TagId, StringComparison.Ordinal)
                && byId.TryGetValue(idealId, out _);
            TagPickItem? ideal = isAlias ? byId.GetValueOrDefault(idealId) : null;
            var span = TagSuggest.SplitMatch(source.Name, q);
            var faceColor = ideal?.EffectiveColor ?? source.EffectiveColor;
            var group = ideal is not null && !string.IsNullOrEmpty(ideal.GroupName)
                ? ideal.GroupName
                : source.GroupName;
            var useCount = ideal?.UseCount ?? source.UseCount;
            var display = isAlias && ideal is not null
                ? TagAddInput.AliasFace(source.Name, ideal.Name)
                : source.Name;
            items.Add(new TagPickItem
            {
                TagId = source.TagId,
                Name = source.Name,
                Display = display,
                SuggestText = source.Name,
                EffectiveColor = faceColor,
                GroupName = group,
                UseCount = useCount,
                MatchBefore = span.Before,
                MatchText = span.Match,
                MatchAfter = span.After,
                IsAliasHit = isAlias,
                AliasCanonicalName = ideal?.Name ?? "",
            });
        }

        // Create last in source; owned LstTagSuggestions paints this order (ASB popup reverses when above).
        var createLabel = TagAddInput.CreateSuggestionLabel(q);
        var ordered = TagSuggest.WithCreateLast(
            items,
            new TagPickItem
            {
                TagId = "",
                Name = q,
                Display = createLabel,
                SuggestText = TagSuggest.CreateSuggestText(createLabel),
                MatchText = createLabel,
                IsCreateRow = true,
                EffectiveColor = TagAddInput.SlateHex,
            },
            t => t.IsCreateRow);

        TagSuggestions.Clear();
        foreach (var item in ordered)
        {
            TagSuggestions.Add(item);
        }

        ShowTagSuggestions = true;
        var matchNames = ordered.Where(t => !t.IsCreateRow).Select(t => t.Name).ToList();
        TagSuggestionsSummary = matchNames.Count == 0
            ? createLabel
            : string.Join(", ", matchNames);
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

    private static List<TagPickItem> BuildTagPicks(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships,
        IReadOnlyDictionary<string, int>? useCounts = null)
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
            var groupName = parents.Count > 0 ? string.Join(", ", parents) : "";
            var extra = parents.Count > 0 ? $" ({groupName})" : "";
            picks.Add(new TagPickItem
            {
                TagId = tag.Id,
                Name = tag.Name,
                Display = tag.Name + extra,
                SuggestText = tag.Name,
                GroupName = groupName,
                UseCount = useCounts?.GetValueOrDefault(tag.Id) ?? 0,
                EffectiveColor = colors.GetValueOrDefault(tag.Id)
            });
        }

        return picks;
    }

    private async Task AssignManyToSelectionAsync(
        IReadOnlyList<AssetItem> targets,
        IReadOnlyList<(string TagId, string TagName, TagSource Source)> tags,
        int createdCount,
        IReadOnlyList<(string TagId, string TagName)>? createdTags = null)
    {
        var result = await _fileOps.AssignTagsAsync(
            targets.Select(t => t.Id).ToList(),
            tags,
            AppServices.CurrentProject.Id,
            createdTags);
        foreach (var (tagId, _, source) in tags)
        {
            if (source == TagSource.Manual)
            {
                RememberRecent(tagId);
            }
        }

        await RefreshUndoStateAsync();
        if (result.Ok)
        {
            var names = tags.Select(t => t.TagName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var status = TagStatusBar.Tagged(targets.Count, names, createdCount);
            await UiDispatch.RunAsync(() =>
            {
                StatusText = status;
                ShowStatusUndo = result.BatchId is not null && CanUndoFileOp;
                ShowUndoToast = false;
                AssignAnnouncement = status;
            });
        }
        else
        {
            StatusText = result.Message;
            ShowStatusUndo = false;
        }
    }

    private async Task RemoveTagsFromSelectionAsync(
        IReadOnlyList<AssetItem> targets,
        IReadOnlyList<(string TagId, string TagName)> tags,
        int affectedFileCount)
    {
        var result = await _fileOps.RemoveTagsAsync(
            targets.Select(t => t.Id).ToList(),
            tags,
            AppServices.CurrentProject.Id);
        await RefreshUndoStateAsync();
        if (result.Ok)
        {
            var names = tags.Select(t => t.TagName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var status = TagStatusBar.Removed(names, affectedFileCount > 0 ? affectedFileCount : targets.Count);
            await UiDispatch.RunAsync(() =>
            {
                StatusText = status;
                ShowStatusUndo = result.BatchId is not null && CanUndoFileOp;
                ShowUndoToast = false;
                AssignAnnouncement = status;
            });
        }
        else
        {
            StatusText = result.Message;
            ShowStatusUndo = false;
        }

        await LoadPreviewAsync(SelectedAsset);
        await RefreshUndoStateAsync();
        RefreshRemoveFromAllBar();
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

    /// <summary>T2 leftover hook — Assign panel collections removed in T8; refresh pending only.</summary>
    public void RebuildAssignPanelPublic() => RefreshPendingChrome();
}
