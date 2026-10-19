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

/// <summary>Library: on-disk organize preview / apply (Undo is the shared FileOp journal).</summary>
public partial class LibraryViewModel
{
    [RelayCommand]
    private Task PreviewOrganizeAsync() =>
        ErrorReporter.RunAsync("Preview organize", Notify, async () =>
        {
            var targets = await SelectedAssetsAsync();
            if (targets.Count == 0)
            {
                Notify("Select assets or a folder to organize.");
                return;
            }

            if (RequestOrganizeChoice is null)
            {
                return;
            }

            var choice = await RequestOrganizeChoice();
            if (choice is null)
            {
                return;
            }

            var preview = await _organize.DryRunAsync(targets, choice);
            OrganizePreview.Clear();
            foreach (var item in preview)
            {
                OrganizePreview.Add(item);
            }

            ShowOrganizePanel = true;
            StatusText = $"Organize preview: {preview.Count} items.";
        });

    [RelayCommand]
    private Task ApplyOrganizeAsync() =>
        ErrorReporter.RunAsync("Apply organize", Notify, async () =>
        {
            if (OrganizePreview.Count == 0)
            {
                return;
            }

            BeginBusy("Organizing…");
            try
            {
                var result = await _organize.ApplyAsync(AppServices.CurrentProject.Id, OrganizePreview.ToList());
                // Resume is on the UI thread — safe to mutate bound preview rows here.
                OrganizeService.MarkPreviewApplied(OrganizePreview, result);
                await RefreshQuietAsync();
                await RefreshUndoStateAsync();
                if (result.Ok)
                {
                    ShowUndoToastFor(result.Message);
                }
                else
                {
                    Notify(result.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                }
            }
            finally
            {
                EndBusy();
            }
        });
}
