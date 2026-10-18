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

/// <summary>Library: adding and scanning watched folders.</summary>
public partial class LibraryViewModel
{
    [RelayCommand]
    private Task AddFolderAsync() =>
        ErrorReporter.RunAsync("Add folder", Notify, async () =>
        {
            var folder = await _access.PickFolderAsync();
            if (folder is null)
            {
                return;
            }

            var existing = (await _catalog.GetSourceFoldersAsync(AppServices.CurrentProject.Id))
                .FirstOrDefault(s => string.Equals(s.Path, folder.Path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                Notify("That folder is already in the library.");
                return;
            }

            if (!MediaCache.IsAllowedSource(folder.Path, AppServices.ActiveCacheRoot, out var cacheReason))
            {
                Notify(cacheReason ?? "That folder overlaps the media cache.");
                return;
            }

            var source = new SourceFolder
            {
                Id = PalaceDb.NewId(),
                Path = folder.Path,
                AccessToken = _access.Remember(folder),
                FolderTemplate = "{Character}/{tags:2}",
                FileTemplate = "{Character}-{tags}.{ext}",
                ProjectId = AppServices.CurrentProject.Id
            };
            await _catalog.UpsertSourceFolderAsync(source);
            await ScanFolderAsync(source);
            await _watchers.RestartAsync();
        });

    [RelayCommand]
    private Task ScanAsync() =>
        ErrorReporter.RunAsync("Scan", Notify, async () =>
        {
            BeginBusy("Scanning…");
            try
            {
                var report = await _scan.ScanAllAsync(
                    new Progress<string>(p => StatusText = $"Scanning {Path.GetFileName(p)}"),
                    projectId: AppServices.CurrentProject.Id);
                await RefreshQuietAsync();
                StatusText = $"Indexed {report.Added} new, {report.Updated} updated, {report.Orphaned} missing.";
            }
            finally
            {
                EndBusy();
            }
        });

    private async Task ScanFolderAsync(SourceFolder source)
    {
        BeginBusy($"Scanning {source.Path}…");
        try
        {
            var report = await _scan.ScanSourceAsync(source);
            await RefreshQuietAsync(source.Path);
            StatusText = $"Indexed {report.Added} files from {Path.GetFileName(source.Path)}.";
        }
        finally
        {
            EndBusy();
        }
    }
}
