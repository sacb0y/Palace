using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;

namespace Palace.ViewModels;

/// <summary>
/// Library: Delete rejected — toolbar / More / All rejected pin / Review end card /
/// confirm dialog / status Undo (Recycle only) / partial-failure Show + Retry.
/// </summary>
public partial class LibraryViewModel
{
    private List<DeleteRejectedFailure> _lastDeleteRejectedFailures = [];
    private List<Asset> _lastDeleteRejectedRetryTargets = [];
    private DeleteRejectedDestination _lastDeleteRejectedDestination = DeleteRejectedDestination.RecycleBin;
    private string? _allRejectedSourceRoot;
    private bool _statusDetailsIsFailures;

    /// <summary>
    /// Page shows <see cref="Pages.DeleteRejectedDialog"/>; null fails closed.
    /// </summary>
    public Func<DeleteRejectedPrompt, Task<DeleteRejectedChoice?>>? RequestDeleteRejected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDeleteRejectedToolbar))]
    [NotifyPropertyChangedFor(nameof(DeleteRejectedToolbarLabel))]
    [NotifyPropertyChangedFor(nameof(AllRejectedCountText))]
    [NotifyPropertyChangedFor(nameof(ShowRejectedActionBar))]
    [NotifyPropertyChangedFor(nameof(RejectedActionSummary))]
    [NotifyPropertyChangedFor(nameof(DeleteAllRejectedLabel))]
    [NotifyPropertyChangedFor(nameof(ShowRejectedEmpty))]
    public partial int RejectedCountInView { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AllRejectedCountText))]
    [NotifyPropertyChangedFor(nameof(ShowRejectedActionBar))]
    [NotifyPropertyChangedFor(nameof(RejectedActionSummary))]
    [NotifyPropertyChangedFor(nameof(DeleteAllRejectedLabel))]
    [NotifyPropertyChangedFor(nameof(ShowRejectedEmpty))]
    public partial int AllRejectedCount { get; set; }

    [ObservableProperty]
    public partial long AllRejectedBytes { get; set; }

    [ObservableProperty]
    public partial int AllRejectedFolderCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRejectedActionBar))]
    [NotifyPropertyChangedFor(nameof(ShowRejectedEmpty))]
    [NotifyPropertyChangedFor(nameof(AllRejectedSelected))]
    public partial bool IsAllRejectedView { get; set; }

    public bool ShowDeleteRejectedToolbar => RejectedCountInView > 0 && !IsTagBrowse;

    public string DeleteRejectedToolbarLabel => DeleteRejected.ToolbarLabel(RejectedCountInView);

    public string AllRejectedCountText => AllRejectedCount.ToString();

    public bool AllRejectedSelected => IsAllRejectedView;

    public bool ShowRejectedActionBar => IsAllRejectedView && AllRejectedCount > 0 && !IsTagBrowse;

    public bool ShowRejectedEmpty => IsAllRejectedView && AllRejectedCount == 0 && !IsTagBrowse;

    public string RejectedActionSummary =>
        AllRejectedCount <= 0
            ? ""
            : $"{AllRejectedCount} rejected files · {DeleteRejected.FormatSize(AllRejectedBytes)} · {AllRejectedFolderCount} folders";

    public string DeleteAllRejectedLabel =>
        AllRejectedCount > 0 ? $"Delete all {AllRejectedCount} rejected…" : "Delete rejected…";

    public string StatusDetailsLinkLabel => _statusDetailsIsFailures ? "Show" : "Details";

    public bool ShowRetryDeleteRejected => _lastDeleteRejectedRetryTargets.Count > 0;

    public string ReviewEndTitle => _review?.EndTitle ?? "";

    public string ReviewEndStats => _review?.EndStats ?? "";

    public string ReviewDeleteRejectedLabel => _review?.DeleteRejectedLabel ?? "";

    public bool ShowReviewDeleteRejected => _review?.CanDeleteRejected == true;

    public bool ShowReviewEndCard =>
        IsReviewMode && IsGalleryOverlayOpen && _review is { IsComplete: true };

    /// <summary>
    /// Source root that All rejected count / mosaic / crumb / This-view share.
    /// Captured when entering All rejected so clearing <see cref="SelectedFolder"/> does not inflate to whole-project.
    /// </summary>
    internal string? AllRejectedSourceRoot => ResolveAllRejectedSourceRoot();

    /// <summary>Refresh rejected counts for the toolbar pin (current folder) and All rejected.</summary>
    public async Task RefreshRejectedCountsAsync()
    {
        if (AppServices.CurrentProject is null)
        {
            RejectedCountInView = 0;
            AllRejectedCount = 0;
            AllRejectedBytes = 0;
            AllRejectedFolderCount = 0;
            return;
        }

        var projectId = AppServices.CurrentProject.Id;
        var all = await _catalog.GetAssetsAsync(projectId: projectId);
        var rejected = DeleteRejected.OnlyRejected(all);

        // Pin + All rejected share one current-source root (not whole project when mosaic is source-scoped).
        var sourceRoot = ResolveAllRejectedSourceRoot();
        var allScope = string.IsNullOrWhiteSpace(sourceRoot)
            ? rejected
            : DeleteRejected.InScope(rejected, sourceRoot);
        AllRejectedCount = allScope.Count;
        AllRejectedBytes = DeleteRejected.TotalBytes(allScope);
        AllRejectedFolderCount = DeleteRejected.DistinctRejectedFolders(allScope);

        // Toolbar N follows the selected folder (or All rejected view / whole source).
        if (IsAllRejectedView)
        {
            RejectedCountInView = AllRejectedCount;
        }
        else if (SelectedFolder is not null)
        {
            RejectedCountInView = DeleteRejected.InScope(rejected, SelectedFolder.Path).Count;
        }
        else
        {
            RejectedCountInView = allScope.Count;
        }
    }

    [RelayCommand]
    private void OpenAllRejected()
    {
        if (IsTagBrowse)
        {
            IsTagBrowse = false;
        }

        // Remember the source before clearing selection so count/mosaic/This-view stay aligned.
        _allRejectedSourceRoot = SelectedFolder is not null
            ? FindRootPath(SelectedFolder)
            : ResolveAllRejectedSourceRoot();

        IsAllRejectedView = true;
        if (SelectedFolder is not null)
        {
            SelectedFolder = null;
        }
        else
        {
            StampFolderSelection(null);
            RebuildBreadcrumbs();
            _ = ApplyFilterAsync();
        }

        _ = RefreshRejectedCountsAsync();
    }

    [RelayCommand]
    private Task DeleteRejectedFromToolbarAsync() =>
        OpenDeleteRejectedAsync(DeleteRejectedScope.WholeLibrary, preferViewPrefix: CurrentRejectedViewPrefix());

    [RelayCommand]
    private Task DeleteAllRejectedAsync() =>
        // Action bar "Delete all N" matches the All-rejected mosaic — default This view.
        OpenDeleteRejectedAsync(DeleteRejectedScope.ThisView, preferViewPrefix: CurrentRejectedViewPrefix());

    [RelayCommand]
    private Task DeleteRejectedFromReviewAsync()
    {
        // Session-scoped: this view = Review folder (SelectedFolder / mosaic root).
        return OpenDeleteRejectedAsync(
            DeleteRejectedScope.ThisView,
            preferViewPrefix: SelectedFolder?.Path ?? CurrentRejectedViewPrefix());
    }

    [RelayCommand]
    private Task RetryDeleteRejectedFailuresAsync()
    {
        if (_lastDeleteRejectedRetryTargets.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Keep the destination from the failed run (do not force Recycle after Permanent partial).
        return RunDeleteRejectedAsync(
            _lastDeleteRejectedRetryTargets,
            DeleteRejected.RetryDestination(_lastDeleteRejectedDestination));
    }

    private string? CurrentRejectedViewPrefix()
    {
        if (IsAllRejectedView)
        {
            return ResolveAllRejectedSourceRoot();
        }

        return SelectedFolder?.Path;
    }

    private string? ResolveAllRejectedSourceRoot()
    {
        if (!string.IsNullOrWhiteSpace(_allRejectedSourceRoot))
        {
            return _allRejectedSourceRoot;
        }

        if (SelectedFolder is not null)
        {
            return FindRootPath(SelectedFolder);
        }

        return FolderTree.FirstOrDefault()?.Path;
    }

    private string? FindRootPath(FolderNode? node)
    {
        if (node is null)
        {
            return FolderTree.FirstOrDefault()?.Path;
        }

        if (node.IsRoot)
        {
            return node.Path;
        }

        foreach (var root in FolderTree)
        {
            if (FolderNames.IsSameOrUnder(node.Path, root.Path))
            {
                return root.Path;
            }
        }

        return node.Path;
    }

    private async Task OpenDeleteRejectedAsync(DeleteRejectedScope defaultScope, string? preferViewPrefix)
    {
        if (AppServices.CurrentProject is null || RequestDeleteRejected is null)
        {
            return;
        }

        var projectId = AppServices.CurrentProject.Id;
        var all = await _catalog.GetAssetsAsync(projectId: projectId);
        var rejected = DeleteRejected.OnlyRejected(all);
        if (rejected.Count == 0)
        {
            Notify("Nothing rejected.");
            return;
        }

        var viewPrefix = preferViewPrefix;
        var viewAssets = string.IsNullOrWhiteSpace(viewPrefix)
            ? rejected
            : DeleteRejected.InScope(rejected, viewPrefix);
        if (viewAssets.Count == 0)
        {
            viewAssets = rejected;
            viewPrefix = null;
        }

        var library = rejected;
        DeleteRejected.PartitionNetwork(viewAssets, out var viewNetwork);
        DeleteRejected.PartitionNetwork(library, out var libraryNetwork);

        var viewSourceName = viewPrefix is null
            ? "library"
            : Path.GetFileName(viewPrefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(viewSourceName))
        {
            viewSourceName = "this view";
        }

        var networkLabel = libraryNetwork.Count == 0
            ? ""
            : Path.GetFileName(
                  Path.GetPathRoot(libraryNetwork[0].Path)?.TrimEnd('\\', '/')
                  ?? libraryNetwork[0].Path)
              is { Length: > 0 } leaf
                ? leaf
                : "network share";

        // Prefer a source folder name when the network path sits under a watched root.
        foreach (var root in FolderTree)
        {
            if (libraryNetwork.Any(a => FolderNames.IsSameOrUnder(a.Path, root.Path))
                || viewNetwork.Any(a => FolderNames.IsSameOrUnder(a.Path, root.Path)))
            {
                networkLabel = root.Name;
                break;
            }
        }

        var prompt = new DeleteRejectedPrompt
        {
            ViewCount = viewAssets.Count,
            ViewBytes = DeleteRejected.TotalBytes(viewAssets),
            LibraryCount = library.Count,
            LibraryBytes = DeleteRejected.TotalBytes(library),
            ViewNetworkCount = viewNetwork.Count,
            ViewNetworkBytes = DeleteRejected.TotalBytes(viewNetwork),
            LibraryNetworkCount = libraryNetwork.Count,
            LibraryNetworkBytes = DeleteRejected.TotalBytes(libraryNetwork),
            ViewScopeLabel = $"This view · All rejected in {viewSourceName}",
            NetworkFolderLabel = networkLabel,
            FolderCountView = DeleteRejected.DistinctRejectedFolders(viewAssets),
            FolderCountLibrary = DeleteRejected.DistinctRejectedFolders(library),
            SourceCountLibrary = library.Select(a => a.SourceFolderId).Distinct(StringComparer.Ordinal).Count(),
            SampleThumbPaths = viewAssets.Take(6).Select(a => a.Path).ToList(),
            DefaultScope = defaultScope,
            DefaultDestination = DeleteRejectedDestination.RecycleBin
        };

        var choice = await RequestDeleteRejected(prompt);
        if (choice is null)
        {
            return;
        }

        var targets = choice.Scope == DeleteRejectedScope.WholeLibrary ? library : viewAssets;
        await RunDeleteRejectedAsync(targets, choice.Destination);
    }

    private async Task RunDeleteRejectedAsync(
        IReadOnlyList<Asset> targets,
        DeleteRejectedDestination destination) =>
        await ErrorReporter.RunAsync("Delete rejected", Notify, async () =>
        {
            if (AppServices.CurrentProject is null || targets.Count == 0)
            {
                return;
            }

            _lastDeleteRejectedDestination = destination;

            BeginBusy(destination == DeleteRejectedDestination.Permanent
                ? "Deleting rejected…"
                : "Moving rejected to Recycle Bin…");
            DeleteRejectedResult result;
            try
            {
                using (_watchers.SuppressNotifications())
                {
                    result = await _fileOps.DeleteRejectedAsync(
                        targets,
                        AppServices.CurrentProject.Id,
                        destination);
                }
            }
            finally
            {
                EndBusy();
            }

            _lastDeleteRejectedFailures = result.Failures.ToList();
            _lastDeleteRejectedRetryTargets = targets
                .Where(a => result.Failures.Any(f => f.AssetId == a.Id))
                .ToList();
            OnPropertyChanged(nameof(ShowRetryDeleteRejected));

            if (result.RemovedAssetIds.Count > 0)
            {
                await RemoveDeletedFromUiAsync(result.RemovedAssetIds);
            }

            await RebuildFolderTreeAsync(SelectedFolder?.Path);
            await RefreshRejectedCountsAsync();
            if (IsAllRejectedView)
            {
                await ApplyFilterAsync();
            }

            // Arm toolbar + status Undo from the journal (same as Move / Tag assign).
            await RefreshUndoStateAsync();

            StatusText = result.Message;
            ShowStatusUndo = result.CanUndo && CanUndoFileOp;
            if (result.Failed > 0)
            {
                _statusDetailsIsFailures = true;
                StatusDetails = string.Join(
                    Environment.NewLine,
                    result.Failures.Select(f => $"{f.FileName}: {f.Reason}"));
                OnPropertyChanged(nameof(StatusDetailsLinkLabel));
                Notify(result.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            }
            else
            {
                _statusDetailsIsFailures = false;
                StatusDetails = destination == DeleteRejectedDestination.Permanent
                    ? "Can’t be undone"
                    : null;
                OnPropertyChanged(nameof(StatusDetailsLinkLabel));
                if (result.Ok)
                {
                    Notify(result.Message);
                }
                else
                {
                    Notify(result.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                }
            }
        });
}
