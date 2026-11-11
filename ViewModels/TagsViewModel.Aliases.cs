using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;

namespace Palace.ViewModels;

/// <summary>Tags ▸ Aliases list + details "Also known as" (audit j / al-01 / al-02).</summary>
public partial class TagsViewModel
{
    private List<TagAlias> _aliases = [];
    private IReadOnlyDictionary<string, int> _aliasFileCounts =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public ObservableCollection<TagAliasRow> AliasRows { get; } = [];

    public ObservableCollection<TagAliasRow> SelectedAliases { get; } = [];

    [ObservableProperty]
    public partial string AliasSearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial TagAliasSort AliasSort { get; set; } = TagAliasSort.Tag;

    [ObservableProperty]
    public partial string AliasSortLabel { get; set; } = "Tag";

    [ObservableProperty]
    public partial string AliasListCaption { get; set; } = "0 aliases · 0 tags";

    [ObservableProperty]
    public partial string AliasAddQuery { get; set; } = "";

    [ObservableProperty]
    public partial string AliasValidationHeadline { get; set; } = "";

    [ObservableProperty]
    public partial string AliasValidationDetail { get; set; } = "";

    [ObservableProperty]
    public partial bool AliasValidationIsInfo { get; set; }

    [ObservableProperty]
    public partial bool AliasValidationIsError { get; set; }

    [ObservableProperty]
    public partial bool ShowAliasMove { get; set; }

    [ObservableProperty]
    public partial bool ShowAliasMerge { get; set; }

    [ObservableProperty]
    public partial string AliasMoveLabel { get; set; } = "Move it here";

    [ObservableProperty]
    public partial string AliasMergeLabel { get; set; } = "Merge…";

    [ObservableProperty]
    public partial string AlsoKnownAsHeader { get; set; } = "Also known as";

    [ObservableProperty]
    public partial bool IsAliasesView { get; set; }

    public string AliasHelperText => TagAliasDraft.HelperText;

    public Action? RequestFocusAliasAdd { get; set; }

    private TagAliasDraft? _aliasDraft;

    partial void OnAliasSearchQueryChanged(string value) => RebuildAliasRows();

    partial void OnAliasSortChanged(TagAliasSort value)
    {
        AliasSortLabel = value == TagAliasSort.Alias ? "Alias" : "Tag";
        RebuildAliasRows();
    }

    partial void OnAliasAddQueryChanged(string value) => RefreshAliasDraft();

    [RelayCommand]
    private void SetAliasSortTag() => AliasSort = TagAliasSort.Tag;

    [RelayCommand]
    private void SetAliasSortAlias() => AliasSort = TagAliasSort.Alias;

    [RelayCommand]
    private void FocusNewAlias()
    {
        if (SelectedNode?.TagId is null)
        {
            StatusText = "Select the tag this alias should point to.";
            return;
        }

        RequestFocusAliasAdd?.Invoke();
    }

    [RelayCommand]
    private void SelectAliasRow(TagAliasRow? row)
    {
        if (row is null)
        {
            return;
        }

        var ideal = _tags.FirstOrDefault(t => t.Id == row.IdealTagId);
        if (ideal is null)
        {
            return;
        }

        SelectNode(new TagTreeNode
        {
            TagId = ideal.Id,
            Name = ideal.Name,
            EffectiveColor = ideal.Color,
            IsStarred = ideal.IsStarred
        });
    }

    [RelayCommand]
    private Task AddAliasNamesAsync() =>
        ErrorReporter.RunAsync("Add alias", Notify, async () =>
        {
            if (SelectedNode?.TagId is null)
            {
                StatusText = "Select a tag first.";
                return;
            }

            var ideal = _tags.FirstOrDefault(t => t.Id == SelectedNode.TagId);
            if (ideal is null)
            {
                return;
            }

            var projectId = AppServices.CurrentProject?.Id;
            if (string.IsNullOrEmpty(projectId))
            {
                StatusText = "Open a project to edit aliases.";
                return;
            }

            var names = TagNameList.Split(AliasAddQuery);
            if (names.Count == 0)
            {
                RefreshAliasDraft();
                return;
            }

            var ready = new List<string>();
            foreach (var raw in names)
            {
                var draft = TagAliasDraft.Evaluate(raw, ideal, _tags, _aliases, _aliasFileCounts);
                if (draft.Kind is TagAliasDraftKind.AutoMatch or TagAliasDraftKind.AlreadyHere
                    or TagAliasDraftKind.ConflictOther or TagAliasDraftKind.ExistingTag)
                {
                    _aliasDraft = draft;
                    ApplyAliasDraftUi(draft);
                    // Keep the typed text so remaining comma tokens are not wiped.
                    return;
                }

                if (draft.Kind == TagAliasDraftKind.Ready)
                {
                    ready.Add(draft.Name);
                }
            }

            if (ready.Count == 0)
            {
                RefreshAliasDraft();
                return;
            }

            var result = await _fileOps.AddNewAliasesAsync(ideal.Id, ready, projectId);
            await AppServices.Library.RefreshUndoStateAsync();
            if (!result.Ok)
            {
                StatusText = result.Message;
                ShowStatusUndo = false;
                return;
            }

            AliasAddQuery = "";
            _aliasDraft = null;
            ClearAliasDraftUi();
            await ReportAliasOpAsync(result);
        });

    [RelayCommand]
    private Task MoveAliasHereAsync() =>
        ErrorReporter.RunAsync("Move alias", Notify, async () =>
        {
            var draft = _aliasDraft;
            if (draft is null
                || draft.Kind != TagAliasDraftKind.ConflictOther
                || draft.ExistingTagId is null
                || SelectedNode?.TagId is null)
            {
                return;
            }

            if (RequestConfirm is null
                || !await RequestConfirm(draft.MoveConfirmTitle, draft.MoveConfirmMessage, draft.MoveLabel))
            {
                return;
            }

            var projectId = AppServices.CurrentProject?.Id;
            if (string.IsNullOrEmpty(projectId))
            {
                StatusText = "Open a project to edit aliases.";
                return;
            }

            var result = await _fileOps.RepointAliasAsync(draft.ExistingTagId, SelectedNode.TagId, projectId);
            await AppServices.Library.RefreshUndoStateAsync();
            if (!result.Ok)
            {
                StatusText = result.Message;
                ShowStatusUndo = false;
                return;
            }

            AliasAddQuery = "";
            _aliasDraft = null;
            ClearAliasDraftUi();
            await ReportAliasOpAsync(result);
        });

    [RelayCommand]
    private void CancelAliasDraft()
    {
        AliasAddQuery = "";
        _aliasDraft = null;
        ClearAliasDraftUi();
    }

    [RelayCommand]
    private Task MergeAliasTagAsync() =>
        ErrorReporter.RunAsync("Merge into alias", Notify, async () =>
        {
            var draft = _aliasDraft;
            if (draft is null
                || draft.Kind != TagAliasDraftKind.ExistingTag
                || draft.ExistingTagId is null
                || SelectedNode?.TagId is null)
            {
                return;
            }

            if (RequestConfirm is null
                || !await RequestConfirm(draft.MergeConfirmTitle, draft.MergeConfirmMessage, "Merge"))
            {
                return;
            }

            var projectId = AppServices.CurrentProject?.Id;
            if (string.IsNullOrEmpty(projectId))
            {
                StatusText = "Open a project to edit aliases.";
                return;
            }

            var result = await _fileOps.AddAliasAsync(draft.ExistingTagId, SelectedNode.TagId, projectId);
            await AppServices.Library.RefreshUndoStateAsync();
            if (!result.Ok)
            {
                StatusText = result.Message;
                ShowStatusUndo = false;
                return;
            }

            AliasAddQuery = "";
            _aliasDraft = null;
            ClearAliasDraftUi();
            await ReportAliasOpAsync(result);
        });

    [RelayCommand]
    private Task RemoveAliasRowAsync(TagAliasRow? row) =>
        ErrorReporter.RunAsync("Remove alias", Notify, async () =>
        {
            if (row is null)
            {
                return;
            }

            // Delete is reversible via status-bar Undo; still ask when a confirm handler exists.
            if (RequestConfirm is not null
                && !await RequestConfirm(
                    "Remove alias?",
                    $"Stop treating “{row.AliasName}” as another name for “{row.IdealName}”?",
                    "Remove"))
            {
                return;
            }

            var projectId = AppServices.CurrentProject?.Id;
            if (string.IsNullOrEmpty(projectId))
            {
                StatusText = "Open a project to edit aliases.";
                return;
            }

            var result = await _fileOps.RemoveAliasAsync(row.AliasTagId, projectId);
            await AppServices.Library.RefreshUndoStateAsync();
            if (!result.Ok)
            {
                StatusText = result.Message;
                ShowStatusUndo = false;
                return;
            }

            await ReportAliasOpAsync(result);
        });

    private async Task ReportAliasOpAsync(FileOpResult result)
    {
        StatusText = TagStatusBar.WithCheck(result.Message);
        ShowStatusUndo = result.Ok && result.BatchId is not null && AppServices.Library.CanUndoFileOp;
        AppServices.Library.ShowUndoToast = false;
        await RefreshAsync();
    }

    private void RefreshAliasDraft()
    {
        if (SelectedNode?.TagId is null)
        {
            _aliasDraft = null;
            ClearAliasDraftUi();
            return;
        }

        var ideal = _tags.FirstOrDefault(t => t.Id == SelectedNode.TagId);
        if (ideal is null)
        {
            _aliasDraft = null;
            ClearAliasDraftUi();
            return;
        }

        var first = TagNameList.Split(AliasAddQuery).FirstOrDefault() ?? AliasAddQuery;
        var draft = TagAliasDraft.Evaluate(first, ideal, _tags, _aliases, _aliasFileCounts);
        _aliasDraft = draft;
        ApplyAliasDraftUi(draft);
    }

    private void ApplyAliasDraftUi(TagAliasDraft draft)
    {
        AliasValidationHeadline = draft.Headline;
        AliasValidationDetail = draft.Detail;
        AliasValidationIsInfo = draft.Severity == TagAliasDraftSeverity.Info;
        AliasValidationIsError = draft.Severity == TagAliasDraftSeverity.Error;
        ShowAliasMove = draft.ShowsMove;
        ShowAliasMerge = draft.ShowsMerge;
        AliasMoveLabel = draft.MoveLabel;
        AliasMergeLabel = draft.MergeLabel;
    }

    private void ClearAliasDraftUi()
    {
        AliasValidationHeadline = "";
        AliasValidationDetail = "";
        AliasValidationIsInfo = false;
        AliasValidationIsError = false;
        ShowAliasMove = false;
        ShowAliasMerge = false;
        AliasMoveLabel = "Move it here";
        AliasMergeLabel = "Merge…";
    }

    private void RebuildAliasRows()
    {
        var colors = _tags.Count == 0 ? null : CatalogService.MapEffectiveColors(_tags, _memberships);
        var filtering = !string.IsNullOrWhiteSpace(AliasSearchQuery);
        var rows = TagAliasList.Build(
            _tags,
            _aliases,
            fileCounts: _aliasFileCounts,
            colors: colors,
            query: AliasSearchQuery,
            sort: AliasSort);
        var idealCount = _aliases
            .Select(a => a.IdealTagId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        AliasRows.Clear();
        foreach (var row in rows)
        {
            AliasRows.Add(row);
        }

        AliasListCaption = TagAliasList.Caption(rows.Count, _aliases.Count, idealCount, filtering);
        OnPropertyChanged(nameof(ChildrenHeader));
        RebuildSelectedAliases();
    }

    private void RebuildSelectedAliases()
    {
        SelectedAliases.Clear();
        var idealId = SelectedNode?.TagId;
        if (idealId is null)
        {
            AlsoKnownAsHeader = "Also known as";
            return;
        }

        var colors = _tags.Count == 0 ? null : CatalogService.MapEffectiveColors(_tags, _memberships);
        var rows = TagAliasList.Build(
                _tags,
                _aliases,
                fileCounts: _aliasFileCounts,
                colors: colors,
                sort: TagAliasSort.Alias)
            .Where(r => string.Equals(r.IdealTagId, idealId, StringComparison.Ordinal))
            .ToList();
        foreach (var row in rows)
        {
            SelectedAliases.Add(row);
        }

        AlsoKnownAsHeader = rows.Count == 0
            ? "Also known as"
            : $"Also known as ({rows.Count})";
        RefreshAliasDraft();
    }

    private async Task LoadAliasFileCountsAsync()
    {
        _aliasFileCounts = await _catalog.GetAssetTagCountsAsync();
    }
}
