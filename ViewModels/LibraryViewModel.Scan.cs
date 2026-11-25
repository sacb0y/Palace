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
            var ct = BeginBusyCancelable("Scanning…");
            try
            {
                var report = await _scan.ScanAllAsync(
                    new Progress<ScanProgress>(p =>
                    {
                        var name = string.IsNullOrEmpty(p.FileName) ? p.Phase : p.FileName;
                        // Done/Total stay optional until IndexTree can pre-count cheaply.
                        StatusText = $"Scanning {name}";
                    }),
                    ct,
                    projectId: AppServices.CurrentProject.Id);
                await RefreshQuietAsync();
                StatusText = ScanNotice.Summary(report);
                ShowScanNotice(report);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Scan cancelled.";
            }
            finally
            {
                EndBusyCancelable();
            }
        });

    private async Task ScanFolderAsync(SourceFolder source)
    {
        var ct = BeginBusyCancelable($"Scanning {source.Path}…");
        try
        {
            var report = await _scan.ScanSourceAsync(
                source,
                new Progress<ScanProgress>(p =>
                {
                    var name = string.IsNullOrEmpty(p.FileName) ? p.Phase : p.FileName;
                    StatusText = $"Scanning {name}";
                }),
                ct);
            await RefreshQuietAsync(source.Path);
            StatusText = ScanNotice.Summary(report, Path.GetFileName(source.Path));
            ShowScanNotice(report);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        finally
        {
            EndBusyCancelable();
        }
    }

    /// <summary>Keeps the footer to one line; full notice lives behind Details + InfoBar.</summary>
    private void ShowScanNotice(ScanReport report)
    {
        if (ScanNotice.Describe(report) is { } notice)
        {
            StatusDetails = notice;
            Notify(notice, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
            // StatusText stays the Indexed summary only (one line).
        }
        else
        {
            StatusDetails = null;
        }
    }

    [RelayCommand]
    private void ShowStatusDetails()
    {
        if (string.IsNullOrWhiteSpace(StatusDetails))
        {
            return;
        }

        Notify(StatusDetails, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
    }
}
