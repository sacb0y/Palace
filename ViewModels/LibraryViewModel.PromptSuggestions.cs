using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Windows.Storage;

namespace Palace.ViewModels;

/// <summary>Library From-prompt suggestion chips (audit k / pt-01a · pt-01b · pt-02).</summary>
public partial class LibraryViewModel
{
    private List<string> _dismissedPromptTokens = [];
    private HashSet<string> _promptJustAddedIds = new(StringComparer.Ordinal);

    [ObservableProperty]
    public partial bool ShowPromptSuggestions { get; set; }

    [ObservableProperty]
    public partial string PromptSuggestionsHeader { get; set; } = "From prompt";

    [ObservableProperty]
    public partial string PromptSuggestionsCountLabel { get; set; } = "";

    [ObservableProperty]
    public partial string AcceptAllPromptLabel { get; set; } = "Accept all";

    [ObservableProperty]
    public partial bool ShowAcceptAllPrompt { get; set; }

    [ObservableProperty]
    public partial string PromptIgnoredCaption { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowPromptIgnoredCaption { get; set; }

    [ObservableProperty]
    public partial bool ShowPromptAutoTaggerPlaceholder { get; set; }

    private void LoadDismissedPromptTokens()
    {
        _dismissedPromptTokens = DismissedPromptTokens.Parse(
            ApplicationData.Current.LocalSettings.Values[DismissedPromptTokens.SettingsKey] as string).ToList();
    }

    private void PersistDismissedPromptTokens()
    {
        ApplicationData.Current.LocalSettings.Values[DismissedPromptTokens.SettingsKey] =
            DismissedPromptTokens.Serialize(_dismissedPromptTokens);
    }

    private IReadOnlyDictionary<string, string> BuildAliasNameMap(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagAlias> aliases)
    {
        var nameById = tags.ToDictionary(t => t.Id, t => t.Name, StringComparer.Ordinal);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in aliases)
        {
            if (!nameById.TryGetValue(edge.AliasTagId, out var aliasName)
                || !nameById.TryGetValue(edge.IdealTagId, out var idealName))
            {
                continue;
            }

            var key = TagNames.Normalize(aliasName);
            if (key.Length == 0)
            {
                continue;
            }

            map[key] = TagNames.Normalize(idealName);
        }

        return map;
    }

    private void ApplyPromptSuggestions(
        PromptSuggestResult result,
        IReadOnlyDictionary<string, HashSet<string>> appliedTagToAssets,
        int selectionTotal,
        bool multi)
    {
        Suggestions.Clear();
        foreach (var suggestion in result.Suggestions)
        {
            if (suggestion.ExistingTagId is { Length: > 0 } tagId
                && appliedTagToAssets.TryGetValue(tagId, out var onAssets)
                && suggestion.AssetIds.All(id => onAssets.Contains(id)))
            {
                // Already on every file whose prompt carries this token.
                continue;
            }

            Suggestions.Add(suggestion);
        }

        RefreshPromptSuggestionChrome(result.IgnoredTokens, selectionTotal, multi);
    }

    private void RefreshPromptSuggestionChrome(
        IReadOnlyList<string>? ignoredTokens,
        int selectionTotal,
        bool multi)
    {
        var count = Suggestions.Count;
        ShowPromptSuggestions = count > 0;
        ShowPromptAutoTaggerPlaceholder = count > 0 || ShowGeneration;
        PromptSuggestionsHeader = multi ? "From prompts" : "From prompt";
        PromptSuggestionsCountLabel = multi && count > 0
            ? $"{count} · sorted by count"
            : count > 0 ? count.ToString() : "";

        var known = Suggestions.Count(s => s.IsKnown);
        ShowAcceptAllPrompt = known > 0;
        AcceptAllPromptLabel = known > 0 ? $"Accept all {known}" : "Accept all";

        if (ignoredTokens is { Count: > 0 })
        {
            var listed = string.Join(", ", ignoredTokens.Take(6));
            PromptIgnoredCaption = $"Ignored: {listed} · × hides one";
            ShowPromptIgnoredCaption = true;
        }
        else if (count > 0 && Suggestions.All(s => s.IsNew))
        {
            PromptIgnoredCaption = "New tags stay until you add or dismiss them.";
            ShowPromptIgnoredCaption = true;
        }
        else if (multi && count > 0)
        {
            PromptIgnoredCaption = "Each tag goes only to the files whose prompt has it.";
            ShowPromptIgnoredCaption = true;
        }
        else
        {
            PromptIgnoredCaption = "";
            ShowPromptIgnoredCaption = false;
        }
    }

    private void ClearPromptSuggestionsUi()
    {
        Suggestions.Clear();
        ShowPromptSuggestions = false;
        ShowAcceptAllPrompt = false;
        PromptSuggestionsCountLabel = "";
        PromptIgnoredCaption = "";
        ShowPromptIgnoredCaption = false;
        ShowPromptAutoTaggerPlaceholder = false;
        PromptSuggestionsHeader = "From prompt";
        AcceptAllPromptLabel = "Accept all";
    }

    [RelayCommand]
    private Task AcceptSuggestionAsync(PromptSuggestion? suggestion) =>
        ErrorReporter.RunAsync("Accept prompt suggestion", Notify, async () =>
        {
            if (suggestion is null)
            {
                return;
            }

            await AcceptPromptSuggestionsAsync([suggestion], acceptKnownOnly: false);
        });

    [RelayCommand]
    private Task AcceptAllPromptSuggestionsAsync() =>
        ErrorReporter.RunAsync("Accept all from prompt", Notify, async () =>
        {
            var known = Suggestions.Where(s => s.IsKnown).ToList();
            if (known.Count == 0)
            {
                return;
            }

            await AcceptPromptSuggestionsAsync(known, acceptKnownOnly: true);
        });

    [RelayCommand]
    private void DismissPromptSuggestion(PromptSuggestion? suggestion)
    {
        if (suggestion is null)
        {
            return;
        }

        _dismissedPromptTokens = DismissedPromptTokens.Remember(
            _dismissedPromptTokens, suggestion.Token).ToList();
        PersistDismissedPromptTokens();
        Suggestions.Remove(suggestion);
        RefreshPromptSuggestionChrome(
            null,
            suggestion.SelectionTotal,
            suggestion.SelectionTotal > 1);
        if (Suggestions.Count == 0)
        {
            ClearPromptSuggestionsUi();
        }
    }

    [RelayCommand]
    private void DismissAllPromptSuggestions()
    {
        if (Suggestions.Count == 0)
        {
            return;
        }

        _dismissedPromptTokens = DismissedPromptTokens.RememberMany(
            _dismissedPromptTokens,
            Suggestions.Select(s => s.Token)).ToList();
        PersistDismissedPromptTokens();
        ClearPromptSuggestionsUi();
    }

    private async Task AcceptPromptSuggestionsAsync(
        IReadOnlyList<PromptSuggestion> suggestions,
        bool acceptKnownOnly)
    {
        if (suggestions.Count == 0)
        {
            return;
        }

        var rows = new List<(string AssetId, string TagId, string TagName, TagSource Source)>();
        var createdTags = new List<(string TagId, string TagName)>();
        var createdCount = 0;
        var acceptedNames = new List<string>();
        var justAdded = new HashSet<string>(StringComparer.Ordinal);
        var fileIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var suggestion in suggestions)
        {
            if (acceptKnownOnly && !suggestion.IsKnown)
            {
                continue;
            }

            string tagId;
            string tagName;
            if (suggestion.ExistingTagId is { Length: > 0 } existingId)
            {
                tagId = existingId;
                tagName = suggestion.ExistingTagName ?? suggestion.Token;
            }
            else
            {
                var (created, wasCreated) = await _catalog.GetOrCreateTagResultAsync(suggestion.Token);
                tagId = created.Id;
                tagName = created.Name;
                if (wasCreated)
                {
                    createdCount++;
                    createdTags.Add((tagId, tagName));
                }
            }

            // Fail closed: never fall back to the whole selection (sparse multi contract).
            if (suggestion.AssetIds.Count == 0)
            {
                continue;
            }

            foreach (var assetId in suggestion.AssetIds)
            {
                rows.Add((assetId, tagId, tagName, TagSource.Prompt));
                fileIds.Add(assetId);
            }

            acceptedNames.Add(tagName);
            justAdded.Add(tagId);
            Suggestions.Remove(suggestion);
        }

        if (rows.Count == 0)
        {
            return;
        }

        var result = await _fileOps.AssignTagRowsAsync(
            rows,
            AppServices.CurrentProject.Id,
            createdCount > 0 ? createdTags : null);
        await RefreshUndoStateAsync();

        if (result.Ok)
        {
            _promptJustAddedIds = justAdded;
            var uniqueNames = acceptedNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var status = uniqueNames.Count > 1 && fileIds.Count <= 1
                ? TagStatusBar.AddedFromPrompt(uniqueNames.Count)
                : uniqueNames.Count == 1
                    ? TagStatusBar.AddedFromPrompts(uniqueNames[0], fileIds.Count)
                    : TagStatusBar.AddedFromPrompts(null, fileIds.Count, uniqueNames.Count);
            await UiDispatch.RunAsync(() =>
            {
                StatusText = status;
                ShowStatusUndo = result.BatchId is not null && CanUndoFileOp;
                ShowUndoToast = false;
                AssignAnnouncement = status;
                if (Suggestions.Count == 0)
                {
                    ClearPromptSuggestionsUi();
                }
                else
                {
                    RefreshPromptSuggestionChrome(
                        null,
                        suggestions[0].SelectionTotal,
                        suggestions[0].SelectionTotal > 1);
                }
            });
        }
        else
        {
            StatusText = result.Message;
            ShowStatusUndo = false;
        }

        await LoadPreviewAsync(SelectedAsset);
        await ReloadTagCatalogAsync();
    }
}
