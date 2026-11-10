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
    public int NetworkCount { get; init; }
    public long NetworkBytes { get; init; }
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
