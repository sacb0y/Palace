using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Models;
using Palace.Services;
using Windows.Storage;

namespace Palace.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly AccessService _access;
    private readonly ScanService _scan;
    private readonly WatcherService _watchers;
    private bool _loading;

    public SettingsViewModel(CatalogService catalog, AccessService access, ScanService scan, WatcherService watchers)
    {
        _catalog = catalog;
        _access = access;
        _scan = scan;
        _watchers = watchers;
    }

    public ObservableCollection<SourceFolderItem> Sources { get; } = [];
    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

    [ObservableProperty]
    public partial string SelectedTheme { get; set; } = "System";

    [ObservableProperty]
    public partial SourceFolderItem? SelectedSource { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    public async Task LoadAsync()
    {
        _loading = true;
        Sources.Clear();
        foreach (var source in await _catalog.GetSourceFoldersAsync(AppServices.CurrentProject.Id))
        {
            var item = new SourceFolderItem
            {
                Id = source.Id,
                Path = source.Path,
                AutoOrganizeNewFiles = source.AutoOrganizeNewFiles,
                FolderTemplate = source.FolderTemplate ?? "{Character}/{tags:2}",
                FileTemplate = source.FileTemplate ?? "{Character}-{tags}.{ext}",
                DestinationPolicy = source.DestinationPolicy.ToString(),
                DestinationPath = source.DestinationPath
            };
            item.PropertyChanged += SourceOnPropertyChanged;
            Sources.Add(item);
        }

        var stored = ApplicationData.Current.LocalSettings.Values["Theme"] as string;
        SelectedTheme = stored is "Light" or "Dark" or "System" ? stored : "System";
        ApplyTheme(SelectedTheme);
        _loading = false;
        StatusText = Sources.Count == 0 ? "No watched folders yet." : $"{Sources.Count} watched folders.";
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        ApplicationData.Current.LocalSettings.Values["Theme"] = value;
        ApplyTheme(value);
    }

    [RelayCommand]
    private async Task AddSourceAsync()
    {
        await AppServices.Library.AddFolderCommand.ExecuteAsync(null);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RemoveSourceAsync()
    {
        if (SelectedSource is null)
        {
            return;
        }

        var folder = await _catalog.GetSourceFolderAsync(SelectedSource.Id);
        if (folder is not null)
        {
            _access.Forget(folder.AccessToken);
        }

        await _catalog.DeleteSourceFolderAsync(SelectedSource.Id);
        await _watchers.RestartAsync();
        await LoadAsync();
        await AppServices.Library.LoadAsync();
    }

    [RelayCommand]
    private async Task PickDestinationAsync()
    {
        if (SelectedSource is null)
        {
            return;
        }

        var folder = await _access.PickFolderAsync();
        if (folder is null)
        {
            return;
        }

        SelectedSource.DestinationPath = folder.Path;
        SelectedSource.DestinationPolicy = nameof(DestinationPolicy.Destination);
        await PersistSourceAsync(SelectedSource);
    }

    private async void SourceOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loading || sender is not SourceFolderItem item)
        {
            return;
        }

        await PersistSourceAsync(item);
    }

    private async Task PersistSourceAsync(SourceFolderItem item)
    {
        var current = await _catalog.GetSourceFolderAsync(item.Id);
        if (current is null)
        {
            return;
        }

        current.AutoOrganizeNewFiles = item.AutoOrganizeNewFiles;
        current.FolderTemplate = item.FolderTemplate;
        current.FileTemplate = item.FileTemplate;
        current.DestinationPolicy = Enum.TryParse<DestinationPolicy>(item.DestinationPolicy, out var policy)
            ? policy
            : DestinationPolicy.InSource;
        current.DestinationPath = item.DestinationPath;
        await _catalog.UpsertSourceFolderAsync(current);
        StatusText = $"Saved settings for {Path.GetFileName(item.Path)}.";
    }

    private static void ApplyTheme(string theme)
    {
        if (App.Window?.Content is not Microsoft.UI.Xaml.FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = theme switch
        {
            "Light" => Microsoft.UI.Xaml.ElementTheme.Light,
            "Dark" => Microsoft.UI.Xaml.ElementTheme.Dark,
            _ => Microsoft.UI.Xaml.ElementTheme.Default
        };
    }
}
