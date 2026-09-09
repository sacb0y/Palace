using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Palace.Services.Cloud;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Palace.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly AccessService _access;
    private readonly ScanService _scan;
    private readonly WatcherService _watchers;
    private readonly CloudAccountService _cloud;
    private readonly ICloudLibraryFactory _libraries;
    private bool _loading;

    public SettingsViewModel(
        CatalogService catalog,
        AccessService access,
        ScanService scan,
        WatcherService watchers,
        CloudAccountService cloud,
        ICloudLibraryFactory libraries)
    {
        _catalog = catalog;
        _access = access;
        _scan = scan;
        _watchers = watchers;
        _cloud = cloud;
        _libraries = libraries;
    }

    public ObservableCollection<SourceFolderItem> Sources { get; } = [];
    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

    public Func<ICloudLibrary, Task<CloudEntry?>>? RequestPickCloudFolder { get; set; }

    [ObservableProperty]
    public partial string SelectedTheme { get; set; } = "System";

    [ObservableProperty]
    public partial SourceFolderItem? SelectedSource { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial string OneDriveClientId { get; set; } = "";

    [ObservableProperty]
    public partial string DropboxAppKey { get; set; } = "";

    [ObservableProperty]
    public partial string OneDriveStatus { get; set; } = "Not connected";

    [ObservableProperty]
    public partial string DropboxStatus { get; set; } = "Not connected";

    [ObservableProperty]
    public partial string RedirectUri { get; set; } = "";

    [ObservableProperty]
    public partial bool IsOneDriveConnected { get; set; }

    [ObservableProperty]
    public partial bool IsDropboxConnected { get; set; }

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
                DestinationPath = source.DestinationPath,
                Kind = source.Kind.ToString(),
                CloudAccountId = source.CloudAccountId,
                CloudRootItemId = source.CloudRootItemId
            };
            item.PropertyChanged += SourceOnPropertyChanged;
            Sources.Add(item);
        }

        var stored = ApplicationData.Current.LocalSettings.Values["Theme"] as string;
        SelectedTheme = stored is "Light" or "Dark" or "System" ? stored : "System";
        ApplyTheme(SelectedTheme);
        OneDriveClientId = _cloud.OneDriveClientId;
        DropboxAppKey = _cloud.DropboxAppKey;
        RedirectUri = _cloud.RedirectUriDisplay;
        await RefreshCloudStatusAsync();
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

    partial void OnOneDriveClientIdChanged(string value)
    {
        if (!_loading)
        {
            _cloud.OneDriveClientId = value ?? "";
        }
    }

    partial void OnDropboxAppKeyChanged(string value)
    {
        if (!_loading)
        {
            _cloud.DropboxAppKey = value ?? "";
        }
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

    [RelayCommand]
    private async Task ConnectOneDriveAsync()
    {
        try
        {
            StatusText = "Connecting to OneDrive…";
            var result = await _cloud.ConnectOneDriveAsync();
            await RefreshCloudStatusAsync();
            StatusText = "Connected OneDrive as " + result.DisplayName
                + ". Pick a folder for " + AppServices.CurrentProject.Name + ".";
            await AddCloudFolderAsync(CloudProvider.OneDrive);
        }
        catch (CloudAuthException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception)
        {
            StatusText = "Could not connect OneDrive.";
        }
    }

    [RelayCommand]
    private async Task ConnectDropboxAsync()
    {
        try
        {
            StatusText = "Connecting to Dropbox…";
            var result = await _cloud.ConnectDropboxAsync();
            await RefreshCloudStatusAsync();
            StatusText = "Connected Dropbox as " + result.DisplayName
                + ". Pick a folder for " + AppServices.CurrentProject.Name + ".";
            await AddCloudFolderAsync(CloudProvider.Dropbox);
        }
        catch (CloudAuthException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception)
        {
            StatusText = "Could not connect Dropbox.";
        }
    }

    [RelayCommand]
    private async Task DisconnectOneDriveAsync()
    {
        await _cloud.DisconnectAsync(CloudProvider.OneDrive);
        await RefreshCloudStatusAsync();
        StatusText = "Disconnected OneDrive.";
    }

    [RelayCommand]
    private async Task DisconnectDropboxAsync()
    {
        await _cloud.DisconnectAsync(CloudProvider.Dropbox);
        await RefreshCloudStatusAsync();
        StatusText = "Disconnected Dropbox.";
    }

    [RelayCommand]
    private Task AddOneDriveFolderAsync() => AddCloudFolderAsync(CloudProvider.OneDrive);

    [RelayCommand]
    private Task AddDropboxFolderAsync() => AddCloudFolderAsync(CloudProvider.Dropbox);

    [RelayCommand]
    private void CopyRedirectUri()
    {
        if (string.IsNullOrWhiteSpace(RedirectUri))
        {
            StatusText = "Redirect URI is not available yet.";
            return;
        }

        var package = new DataPackage();
        package.SetText(RedirectUri);
        Clipboard.SetContent(package);
        StatusText = "Copied the OAuth redirect URI. Register it on the provider app.";
    }

    private async Task AddCloudFolderAsync(CloudProvider provider)
    {
        var account = await _cloud.GetAccountAsync(provider);
        if (account is null)
        {
            StatusText = provider == CloudProvider.OneDrive
                ? "Connect OneDrive first."
                : "Connect Dropbox first.";
            return;
        }

        var library = await _libraries.ForAccountAsync(account);
        if (library is null || RequestPickCloudFolder is null)
        {
            StatusText = "Cloud folder picker is not available.";
            return;
        }

        CloudEntry? picked;
        try
        {
            picked = await RequestPickCloudFolder(library);
        }
        catch (CloudAuthException ex)
        {
            StatusText = ex.Message;
            return;
        }

        if (picked is null)
        {
            return;
        }

        var label = account.DisplayName ?? account.AccountId;
        var display = CloudSourcePath.FolderDisplay(picked.DisplayPath);
        var path = CloudSourcePath.Build(provider, label, display);
        var rootId = CloudSourcePath.NormalizeRootItemId(picked.Id);
        var existing = await _catalog.GetSourceFoldersAsync();
        var taken = existing.FirstOrDefault(s => CloudSourcePath.PathTaken(path, [s.Path]));
        if (taken is null)
        {
            taken = existing.FirstOrDefault(s =>
                !string.IsNullOrEmpty(s.CloudAccountId)
                && CloudSourcePath.SameCloudFolder(account.Id, rootId, s.CloudAccountId, s.CloudRootItemId));
        }

        if (taken is not null)
        {
            StatusText = taken.ProjectId == AppServices.CurrentProject.Id
                ? "That cloud folder is already in this project."
                : "That cloud folder already belongs to another project.";
            return;
        }

        var folder = new SourceFolder
        {
            Id = PalaceDb.NewId(),
            Path = path,
            Kind = provider == CloudProvider.Dropbox ? SourceKind.Dropbox : SourceKind.OneDrive,
            CloudAccountId = account.Id,
            CloudRootItemId = rootId,
            ProjectId = AppServices.CurrentProject.Id
        };
        try
        {
            await _catalog.UpsertSourceFolderAsync(folder);
        }
        catch (Exception)
        {
            StatusText = "That cloud folder path is already used by another project.";
            return;
        }

        StatusText = "Scanning " + picked.Name + "…";
        await _scan.ScanSourceAsync(folder);
        await _watchers.RestartAsync();
        await LoadAsync();
        await AppServices.Library.LoadAsync();
        StatusText = "Added " + display + " to " + AppServices.CurrentProject.Name + ".";
    }

    private async Task RefreshCloudStatusAsync()
    {
        OneDriveStatus = await _cloud.StatusTextAsync(CloudProvider.OneDrive);
        DropboxStatus = await _cloud.StatusTextAsync(CloudProvider.Dropbox);
        IsOneDriveConnected = await _cloud.GetAccountAsync(CloudProvider.OneDrive) is not null;
        IsDropboxConnected = await _cloud.GetAccountAsync(CloudProvider.Dropbox) is not null;
        RedirectUri = _cloud.RedirectUriDisplay;
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
