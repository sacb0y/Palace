using Palace.Helpers;
using Palace.Models;

namespace Palace.ViewModels;

/// <summary>Inputs for the Delete rejected confirm dialog (page builds the ContentDialog).</summary>
public sealed class DeleteRejectedPrompt
{
    public int ViewCount { get; init; }
    public long ViewBytes { get; init; }
    public int LibraryCount { get; init; }
    public long LibraryBytes { get; init; }
    /// <summary>Network rejects in This-view scope (for Recycle skip chrome).</summary>
    public int ViewNetworkCount { get; init; }
    public long ViewNetworkBytes { get; init; }
    /// <summary>Network rejects in Whole-library scope (for Recycle skip chrome).</summary>
    public int LibraryNetworkCount { get; init; }
    public long LibraryNetworkBytes { get; init; }
    public string ViewScopeLabel { get; init; } = "This view";
    public string NetworkFolderLabel { get; init; } = "";
    public int FolderCountView { get; init; }
    public int FolderCountLibrary { get; init; }
    public int SourceCountLibrary { get; init; }
    public IReadOnlyList<string> SampleThumbPaths { get; init; } = [];
    public DeleteRejectedScope DefaultScope { get; init; } = DeleteRejectedScope.WholeLibrary;
    public DeleteRejectedDestination DefaultDestination { get; init; } = DeleteRejectedDestination.RecycleBin;
}

/// <summary>User choices from the Delete rejected confirm dialog.</summary>
public sealed class DeleteRejectedChoice
{
    public DeleteRejectedScope Scope { get; init; }
    public DeleteRejectedDestination Destination { get; init; }
}
