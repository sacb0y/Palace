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
    private readonly LoadGate _load = new();

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
    public string AppVersionText { get; } = AppVersion.Display;

    public IReadOnlyList<ShortcutHint> ShortcutHints { get; } = ShortcutCheatsheet.Entries;

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

    [ObservableProperty]
    public partial bool PeakOverrideEnabled { get; set; }

    [ObservableProperty]
    public partial double PeakOverrideNits { get; set; } = GalleryPresent.SdrReferenceNits;

    [ObservableProperty]
    public partial string PeakOverrideLabel { get; set; } = GalleryPresent.PeakNitsLabel(GalleryPresent.SdrReferenceNits);

    [ObservableProperty]
    public partial string WallpaperPath { get; set; } = "";

    [ObservableProperty]
    public partial string WallpaperLabel { get; set; } = ShellBackground.DefaultWallpaperLabel;

    [ObservableProperty]
    public partial bool HasWallpaper { get; set; }

    [ObservableProperty]
    public partial double Darkness { get; set; } = ShellBackground.DefaultDarkness;

    [ObservableProperty]
    public partial string DarknessLabel { get; set; } = ShellBackground.AmountLabel(ShellBackground.DefaultDarkness);

    [ObservableProperty]
    public partial double Blur { get; set; } = ShellBackground.DefaultBlur;

    [ObservableProperty]
    public partial string BlurLabel { get; set; } = ShellBackground.AmountLabel(ShellBackground.DefaultBlur);

    [ObservableProperty]
    public partial bool TintEnabled { get; set; }

    [ObservableProperty]
    public partial string TintHex { get; set; } = ShellBackground.DefaultTintHex;

    public async Task LoadAsync()
    {
        using var _ = _load.Begin();
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
        LoadPeakOverride();
        LoadShellBackground();
        OneDriveClientId = _cloud.OneDriveClientId;
        DropboxAppKey = _cloud.DropboxAppKey;
        RedirectUri = _cloud.RedirectUriDisplay;
        await RefreshCloudStatusAsync();
        StatusText = Sources.Count == 0 ? "No watched folders yet." : $"{Sources.Count} watched folders.";
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        ApplicationData.Current.LocalSettings.Values["Theme"] = value;
        ApplyTheme(value);
    }

    partial void OnPeakOverrideEnabledChanged(bool value)
    {
        if (!_load.IsLoading)
        {
            PersistPeakOverride();
        }
    }

    partial void OnPeakOverrideNitsChanged(double value)
    {
        PeakOverrideLabel = GalleryPresent.PeakNitsLabel((float)value);
        if (!_load.IsLoading)
        {
            PersistPeakOverride();
        }
    }

    partial void OnDarknessChanged(double value)
    {
        DarknessLabel = ShellBackground.AmountLabel(value);
        if (!_load.IsLoading)
        {
            PersistShellBackground();
        }
    }

    partial void OnBlurChanged(double value)
    {
        BlurLabel = ShellBackground.AmountLabel(value);
        if (!_load.IsLoading)
        {
            PersistShellBackground();
        }
    }

    partial void OnTintEnabledChanged(bool value)
    {
        if (!_load.IsLoading)
        {
            PersistShellBackground();
        }
    }

    partial void OnTintHexChanged(string value)
    {
        if (!_load.IsLoading)
        {
            PersistShellBackground();
        }
    }

    private void LoadPeakOverride()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        GalleryPeak.Apply(
            GalleryPeak.ParseEnabled(values[GalleryPeak.EnabledKey]),
            GalleryPeak.ParseNits(values[GalleryPeak.NitsKey]));
        PeakOverrideEnabled = GalleryPeak.Enabled;
        PeakOverrideNits = GalleryPeak.Nits;
        PeakOverrideLabel = GalleryPresent.PeakNitsLabel(GalleryPeak.Nits);
    }

    private void LoadShellBackground()
    {
        AppServices.LoadShellBackground();
        WallpaperPath = ShellBackground.WallpaperPath ?? "";
        HasWallpaper = ShellBackground.HasWallpaper;
        WallpaperLabel = ShellBackground.WallpaperLabel;
        Darkness = ShellBackground.Darkness;
        DarknessLabel = ShellBackground.AmountLabel(Darkness);
        Blur = ShellBackground.Blur;
        BlurLabel = ShellBackground.AmountLabel(Blur);
        TintEnabled = ShellBackground.TintEnabled;
        TintHex = ShellBackground.TintHex;
    }

    private void PersistPeakOverride()
    {
        GalleryPeak.Apply(PeakOverrideEnabled, (float)PeakOverrideNits);
        var values = ApplicationData.Current.LocalSettings.Values;
        values[GalleryPeak.EnabledKey] = PeakOverrideEnabled;
        values[GalleryPeak.NitsKey] = PeakOverrideNits;
        PeakOverrideLabel = GalleryPresent.PeakNitsLabel(GalleryPeak.Nits);
    }

    private void PersistShellBackground(bool reloadWallpaper = false)
    {
        ShellBackground.Apply(WallpaperPath, Darkness, Blur, TintEnabled, TintHex, reloadWallpaper);
        var values = ApplicationData.Current.LocalSettings.Values;
        values[ShellBackground.WallpaperPathKey] = ShellBackground.WallpaperPath ?? "";
        values[ShellBackground.WallpaperNameKey] = ShellBackground.WallpaperName ?? "";
        values[ShellBackground.DarknessKey] = ShellBackground.Darkness;
        values[ShellBackground.BlurKey] = ShellBackground.Blur;
        values[ShellBackground.TintEnabledKey] = ShellBackground.TintEnabled;
        values[ShellBackground.TintKey] = ShellBackground.TintHex;
        HasWallpaper = ShellBackground.HasWallpaper;
        WallpaperLabel = ShellBackground.WallpaperLabel;
        DarknessLabel = ShellBackground.AmountLabel(ShellBackground.Darkness);
        BlurLabel = ShellBackground.AmountLabel(ShellBackground.Blur);
        TintHex = ShellBackground.TintHex;
    }

    [RelayCommand]
    private Task PickWallpaperAsync() =>
        ErrorReporter.RunAsync("Choose wallpaper", Notify, async () =>
        {
            var file = await _access.PickImageFileAsync();
            if (file is null)
            {
                return;
            }

            var previous = ShellBackground.WallpaperPath;
            var previousName = ShellBackground.WallpaperName;
            ShellBackground.ReleaseDisplay();
            StorageFile? dest = null;
            try
            {
                dest = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    ShellBackground.NewWallpaperFileName(Path.GetExtension(file.Name)),
                    CreationCollisionOption.ReplaceExisting);
                await file.CopyAndReplaceAsync(dest);
                WallpaperPath = dest.Path;
                ShellBackground.SetWallpaperName(file.Name);
                PersistShellBackground(reloadWallpaper: true);
                await TryDeleteStoredWallpaperAsync(previous);
                StatusText = "Wallpaper updated.";
            }
            catch
            {
                if (dest is not null && !string.Equals(ShellBackground.WallpaperPath, dest.Path, StringComparison.OrdinalIgnoreCase))
                {
                    await TryDeleteStoredWallpaperAsync(dest.Path);
                }

                ShellBackground.SetWallpaperName(previousName);
                ShellBackground.RestoreDisplay();
                throw;
            }
        });

    [RelayCommand]
    private Task ClearWallpaperAsync() =>
        ErrorReporter.RunAsync("Clear wallpaper", Notify, async () =>
        {
            var path = ShellBackground.WallpaperPath;
            var name = ShellBackground.WallpaperName;
            ShellBackground.ReleaseDisplay();
            try
            {
                WallpaperPath = "";
                ShellBackground.SetWallpaperName(null);
                PersistShellBackground(reloadWallpaper: true);
                await TryDeleteStoredWallpaperAsync(path);
                StatusText = "Using the default gradient.";
            }
            catch
            {
                WallpaperPath = path ?? "";
                ShellBackground.SetWallpaperName(name);
                ShellBackground.RestoreDisplay();
                throw;
            }
        });

    private static async Task TryDeleteStoredWallpaperAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !ShellBackground.IsStoredWallpaperName(path))
        {
            return;
        }

        try
        {
            var stored = await StorageFile.GetFileFromPathAsync(path);
            await stored.DeleteAsync();
        }
        catch
        {
            // Local copy may already be gone or still flushing.
        }
    }

    partial void OnOneDriveClientIdChanged(string value)
    {
        if (!_load.IsLoading)
        {
            _cloud.OneDriveClientId = value ?? "";
        }
    }

    partial void OnDropboxAppKeyChanged(string value)
    {
        if (!_load.IsLoading)
        {
            _cloud.DropboxAppKey = value ?? "";
        }
    }

    [RelayCommand]
    private Task AddSourceAsync() =>
        ErrorReporter.RunAsync("Add source", Notify, async () =>
        {
            await AppServices.Library.AddFolderCommand.ExecuteAsync(null);
            await LoadAsync();
        });

    [RelayCommand]
    private Task RemoveSourceAsync() =>
        ErrorReporter.RunAsync("Remove source", Notify, async () =>
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
        });

    [RelayCommand]
    private Task PickDestinationAsync() =>
        ErrorReporter.RunAsync("Pick destination", Notify, async () =>
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
        });

    [RelayCommand]
    private Task ConnectOneDriveAsync() =>
        ErrorReporter.RunAsync("Connect OneDrive", Notify, async () =>
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
        });

    [RelayCommand]
    private Task ConnectDropboxAsync() =>
        ErrorReporter.RunAsync("Connect Dropbox", Notify, async () =>
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
        });

    [RelayCommand]
    private Task DisconnectOneDriveAsync() =>
        ErrorReporter.RunAsync("Disconnect OneDrive", Notify, async () =>
        {
            await _cloud.DisconnectAsync(CloudProvider.OneDrive);
            await RefreshCloudStatusAsync();
            StatusText = "Disconnected OneDrive.";
        });

    [RelayCommand]
    private Task DisconnectDropboxAsync() =>
        ErrorReporter.RunAsync("Disconnect Dropbox", Notify, async () =>
        {
            await _cloud.DisconnectAsync(CloudProvider.Dropbox);
            await RefreshCloudStatusAsync();
            StatusText = "Disconnected Dropbox.";
        });

    [RelayCommand]
    private Task AddOneDriveFolderAsync() =>
        ErrorReporter.RunAsync("Add OneDrive folder", Notify, () => AddCloudFolderAsync(CloudProvider.OneDrive));

    [RelayCommand]
    private Task AddDropboxFolderAsync() =>
        ErrorReporter.RunAsync("Add Dropbox folder", Notify, () => AddCloudFolderAsync(CloudProvider.Dropbox));

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
        if (_load.IsLoading || sender is not SourceFolderItem item)
        {
            return;
        }

        await ErrorReporter.RunAsync("Save source settings", Notify, () => PersistSourceAsync(item));
    }

    private void Notify(string message) => StatusText = message;

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
        ShellBackground.Refresh();
    }
}
