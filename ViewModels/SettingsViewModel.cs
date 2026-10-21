using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Palace.Services.Cloud;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI;

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
    public IReadOnlyList<string> AccentOptions { get; } =
        [BrandAccent.FollowWindowsLabel, BrandAccent.PalacePinkLabel];
    public IReadOnlyList<string> DensityOptions { get; } = AppearanceSettings.DensityOptions;
    public string AppVersionText { get; } = AppVersion.Display;

    public string InfancyNoteText { get; } = AppVersion.InfancyNote;

    public IReadOnlyList<ShortcutHint> ShortcutHints { get; } = ShortcutCheatsheet.Entries;

    public Func<ICloudLibrary, Task<CloudEntry?>>? RequestPickCloudFolder { get; set; }

    /// <summary>
    /// Confirm dialog: title, body, primary label. Safe option is Cancel (default).
    /// Destructive commands fail closed when this is null (see <see cref="SettingsConfirm.ConfirmedAsync"/>).
    /// </summary>
    public Func<string, string, string, Task<bool>>? RequestConfirm { get; set; }

    [ObservableProperty]
    public partial string SelectedTheme { get; set; } = "System";

    [ObservableProperty]
    public partial string SelectedAccent { get; set; } = BrandAccent.FollowWindowsLabel;

    [ObservableProperty]
    public partial string SelectedDensity { get; set; } = AppearanceSettings.DefaultDensity;

    [ObservableProperty]
    public partial bool AnimationsEnabled { get; set; } = AppearanceSettings.DefaultAnimations;

    [ObservableProperty]
    public partial bool WatchForChanges { get; set; } = LibrarySettings.DefaultWatchForChanges;

    [ObservableProperty]
    public partial bool IndexImages { get; set; } = LibrarySettings.DefaultIndexImages;

    [ObservableProperty]
    public partial bool IndexVideo { get; set; } = LibrarySettings.DefaultIndexVideo;

    [ObservableProperty]
    public partial bool GenerateThumbsOnScan { get; set; } = LibrarySettings.DefaultGenerateThumbsOnScan;

    public string ImagesFormatsCaption { get; } = LibrarySettings.ImagesFormatsCaption;

    public string VideoFormatsCaption { get; } = LibrarySettings.VideoFormatsCaption;

    public string FormatsPlaceholderCaption { get; } = LibrarySettings.FormatsPlaceholderCaption;

    public IReadOnlyList<string> ZoomOptions { get; } = ViewerSettings.ZoomOptions;

    public string SdrTonemapCaption { get; } = ViewerSettings.SdrTonemapCaption;

    public string SlideshowPlaceholderCaption { get; } = ViewerSettings.SlideshowPlaceholderCaption;

    [ObservableProperty]
    public partial string SelectedDefaultZoom { get; set; } = ViewerSettings.ZoomLabel(ViewerSettings.DefaultZoom);

    [ObservableProperty]
    public partial bool ShowDetailsByDefault { get; set; } = GalleryChrome.DefaultShowDetails;

    [ObservableProperty]
    public partial bool LoopVideo { get; set; } = GalleryChrome.DefaultLoopVideo;

    [ObservableProperty]
    public partial bool TouchGesturesEnabled { get; set; } = ViewerSettings.DefaultTouchGestures;

    [ObservableProperty]
    public partial SourceFolderItem? SelectedSource { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    /// <summary>Empty watched-folders caption lives inside CardWatchedFolders, not below all cards.</summary>
    [ObservableProperty]
    public partial bool ShowWatchedFoldersEmpty { get; set; } = true;

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
    public partial ShellPresetKind SelectedPresetKind { get; set; } = ShellPresetKind.Default;

    [ObservableProperty]
    public partial ShellPresetItem? SelectedPreset { get; set; }

    /// <summary>Active Custom-group MRU id when the live wallpaper is a recent custom.</summary>
    [ObservableProperty]
    public partial string? SelectedRecentCustomId { get; set; }

    [ObservableProperty]
    public partial bool ShowCustomEmptyCaption { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowCustomBrokenWarning { get; set; }

    [ObservableProperty]
    public partial string CustomBrokenWarningMessage { get; set; } =
        "Your custom background couldn't be loaded. Switched to Default.";

    [ObservableProperty]
    public partial bool ShowRecentCustomUndoToast { get; set; }

    [ObservableProperty]
    public partial string RecentCustomUndoMessage { get; set; } = "";

    private RecentCustomBackgroundEntry? _pendingRecentDelete;
    private bool _pendingRecentDeleteWasActive;
    private double _pendingRecentDeleteDarkness;
    private double _pendingRecentDeleteBlur;
    private int _recentUndoEpoch;
    private CancellationTokenSource? _recentUndoCts;
    private int _recentUndoPaused;
    private CancellationTokenSource? _shellPersistCts;

    [ObservableProperty]
    public partial bool BlurEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool DarknessEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowCustomWallpaperActions { get; set; }

    [ObservableProperty]
    public partial double Darkness { get; set; } = ShellBackground.DefaultDarkness;

    [ObservableProperty]
    public partial string DarknessLabel { get; set; } = ShellBackground.DarknessLabel(ShellBackground.DefaultDarkness);

    [ObservableProperty]
    public partial double Blur { get; set; } = ShellBackground.DefaultBlur;

    [ObservableProperty]
    public partial string BlurLabel { get; set; } = ShellBackground.BlurLabel(ShellBackground.DefaultBlur);

    [ObservableProperty]
    public partial bool TintEnabled { get; set; }

    [ObservableProperty]
    public partial string TintHex { get; set; } = ShellBackground.DefaultTintHex;

    public ObservableCollection<ShellPresetItem> BackgroundPresets { get; } = [];

    public ObservableCollection<ShellPresetItem> ChromeBackgroundPresets { get; } = [];

    public ObservableCollection<ShellPresetItem> VividBackgroundPresets { get; } = [];

    public ObservableCollection<ShellPresetItem> CreatorBackgroundPresets { get; } = [];

    public ObservableCollection<ShellPresetItem> CustomBackgroundPresets { get; } = [];

    [ObservableProperty]
    public partial bool MediaCacheEnabled { get; set; }

    [ObservableProperty]
    public partial double MediaCacheMaxMb { get; set; } = MediaCache.DefaultMaxMb;

    [ObservableProperty]
    public partial string MediaCacheMaxLabel { get; set; } = MediaCache.MaxMbLabel(MediaCache.DefaultMaxMb);

    [ObservableProperty]
    public partial string MediaCacheUsageLabel { get; set; } = "";

    [ObservableProperty]
    public partial string MediaCachePathLabel { get; set; } = "";

    [ObservableProperty]
    public partial bool HasCustomCacheFolder { get; set; }

    [ObservableProperty]
    public partial string CatalogPathLabel { get; set; } = "";

    [ObservableProperty]
    public partial bool CatalogUsesCustomRoot { get; set; }

    [ObservableProperty]
    public partial string CatalogRestartHint { get; set; } = "";

    public IReadOnlyList<string> ThumbConcurrencyOptions { get; } = PerformanceSettings.ConcurrencyOptions;

    public IReadOnlyList<string> PreviewQualityOptions { get; } = PerformanceSettings.PreviewQualityOptions;

    public string ThumbConcurrencyCaption { get; } = PerformanceSettings.ThumbConcurrencyCaption;

    public string PreviewQualityCaption { get; } = PerformanceSettings.PreviewQualityCaption;

    public string UpdateChannelCaption { get; } = PerformanceSettings.UpdateChannelCaption;

    public string UpdateChannelLabel { get; } = PerformanceSettings.UpdateChannelLabel;

    [ObservableProperty]
    public partial string SelectedThumbConcurrency { get; set; } = PerformanceSettings.DefaultConcurrency;

    [ObservableProperty]
    public partial string SelectedPreviewQuality { get; set; } = PerformanceSettings.DefaultPreviewQuality;

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

        SelectedSource = Sources.FirstOrDefault(s => s.Id == SelectedSource?.Id) ?? Sources.FirstOrDefault();
        SyncSourceSelection();

        var stored = ApplicationData.Current.LocalSettings.Values["Theme"] as string;
        SelectedTheme = stored is "Light" or "Dark" or "System" ? stored : "System";
        ApplyTheme(SelectedTheme);
        LoadAccent();
        LoadAppearanceChrome();
        LoadLibraryChrome();
        LoadViewerChrome();
        LoadPeakOverride();
        LoadShellBackground();
        LoadMediaCache();
        LoadPerformanceChrome();
        LoadCatalogStore();
        await RefreshMediaCacheUsageAsync();
        OneDriveClientId = _cloud.OneDriveClientId;
        DropboxAppKey = _cloud.DropboxAppKey;
        RedirectUri = _cloud.RedirectUriDisplay;
        await RefreshCloudStatusAsync();
        ShowWatchedFoldersEmpty = Sources.Count == 0;
        StatusText = Sources.Count == 0 ? "" : $"{Sources.Count} watched folders.";
    }

    private void LoadCatalogStore()
    {
        CatalogPathLabel = CatalogStore.RootLabel(CatalogStore.ActiveRoot);
        CatalogUsesCustomRoot = !string.Equals(
            CatalogStore.ActiveRoot,
            CatalogStore.DefaultRoot(),
            StringComparison.OrdinalIgnoreCase);
        CatalogRestartHint = CatalogStore.FallbackReason is { } reason
            ? $"Preferred catalog folder unavailable ({reason}). Using a fallback catalog for this session; changes made now stay there and Palace will retry the preferred folder next launch."
            : CatalogStore.MigratedFromLocalState
                ? "Catalog was moved out of package LocalState so unregister will not wipe it."
                : CatalogStore.UnmergedLocalStateBackup is { } unmerged
                    ? $"Package LocalState also had a catalog; it was not merged. A copy is kept at {unmerged}."
                    : "";
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        ApplicationData.Current.LocalSettings.Values["Theme"] = value;
        ApplyTheme(value);
        // Theme chrome drives packaged light/dark thumbs — rebuild after RequestedTheme applies.
        RefreshPresetSwatches();
    }

    private void LoadAccent()
    {
        var mode = BrandAccent.ParseMode(
            ApplicationData.Current.LocalSettings.Values[BrandAccent.SettingsKey]);
        SelectedAccent = mode == BrandAccent.ModePalacePink
            ? BrandAccent.PalacePinkLabel
            : BrandAccent.FollowWindowsLabel;
    }

    partial void OnSelectedAccentChanged(string value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        var mode = value == BrandAccent.PalacePinkLabel
            ? BrandAccent.ModePalacePink
            : BrandAccent.ModeFollowWindows;
        ApplicationData.Current.LocalSettings.Values[BrandAccent.SettingsKey] = mode;
        App.ApplySavedBrandAccent();
        StatusText = BrandAccent.AppliedCaption;
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
        var clamped = ShellBackground.ClampDarkness(value);
        if (Math.Abs(clamped - value) > 0.01)
        {
            Darkness = clamped;
            return;
        }

        DarknessLabel = ShellBackground.DarknessLabel(clamped);
        if (!_load.IsLoading)
        {
            // Live Composition scalars immediately; LocalSettings ~300ms later.
            ShellBackground.ApplyLiveAmounts(clamped, Blur);
            ScheduleShellPersist();
        }
    }

    partial void OnBlurChanged(double value)
    {
        var clamped = ShellBackground.NormalizeBlur(value);
        if (Math.Abs(clamped - value) > 0.01)
        {
            Blur = clamped;
            return;
        }

        BlurLabel = ShellBackground.BlurLabel(clamped);
        if (!_load.IsLoading)
        {
            ShellBackground.ApplyLiveAmounts(Darkness, clamped);
            ScheduleShellPersist();
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

    partial void OnMediaCacheEnabledChanged(bool value)
    {
        if (!_load.IsLoading)
        {
            PersistMediaCache();
        }
    }

    partial void OnMediaCacheMaxMbChanged(double value)
    {
        MediaCacheMaxLabel = MediaCache.MaxMbLabel(value);
        if (!_load.IsLoading)
        {
            PersistMediaCache();
        }
    }

    private void LoadAppearanceChrome()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        SelectedDensity = AppearanceSettings.ParseDensity(values[AppearanceSettings.DensityKey]);
        AnimationsEnabled = AppearanceSettings.ParseAnimations(values[AppearanceSettings.AnimationsKey]);
    }

    private void LoadLibraryChrome()
    {
        AppServices.LoadLibrarySettings();
        WatchForChanges = LibrarySettings.WatchForChanges;
        IndexImages = LibrarySettings.IndexImages;
        IndexVideo = LibrarySettings.IndexVideo;
        GenerateThumbsOnScan = LibrarySettings.GenerateThumbsOnScan;
    }

    private void PersistLibraryChrome()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        values[LibrarySettings.WatchForChangesKey] = WatchForChanges;
        values[LibrarySettings.IndexImagesKey] = IndexImages;
        values[LibrarySettings.IndexVideoKey] = IndexVideo;
        values[LibrarySettings.GenerateThumbsOnScanKey] = GenerateThumbsOnScan;
        LibrarySettings.Apply(WatchForChanges, IndexImages, IndexVideo, GenerateThumbsOnScan);
    }

    private void LoadViewerChrome()
    {
        AppServices.LoadViewerSettings();
        SelectedDefaultZoom = ViewerSettings.ZoomLabel(ViewerSettings.DefaultZoomMode);
        TouchGesturesEnabled = ViewerSettings.TouchGesturesEnabled;
        var values = ApplicationData.Current.LocalSettings.Values;
        ShowDetailsByDefault = GalleryChrome.ParseShowDetails(values[GalleryChrome.ShowDetailsKey]);
        LoopVideo = GalleryChrome.ParseLoopVideo(values[GalleryChrome.LoopVideoKey]);
    }

    private void PersistViewerChrome()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        var zoom = ViewerSettings.ParseZoomLabel(SelectedDefaultZoom);
        values[ViewerSettings.DefaultZoomKey] = ViewerSettings.PersistZoom(zoom);
        values[ViewerSettings.TouchGesturesKey] = TouchGesturesEnabled;
        values[GalleryChrome.ShowDetailsKey] = ShowDetailsByDefault;
        values[GalleryChrome.LoopVideoKey] = LoopVideo;
        ViewerSettings.Apply(zoom, TouchGesturesEnabled);
    }

    private void LoadPerformanceChrome()
    {
        AppServices.LoadPerformanceSettings();
        SelectedThumbConcurrency = PerformanceSettings.ThumbConcurrency;
        SelectedPreviewQuality = PerformanceSettings.PreviewQuality;
    }

    private void PersistPerformanceChrome()
    {
        var concurrency = PerformanceSettings.NormalizeConcurrency(SelectedThumbConcurrency);
        var quality = PerformanceSettings.NormalizePreviewQuality(SelectedPreviewQuality);
        var values = ApplicationData.Current.LocalSettings.Values;
        values[PerformanceSettings.ThumbConcurrencyKey] = concurrency;
        values[PerformanceSettings.PreviewQualityKey] = quality;
        PerformanceSettings.Apply(concurrency, quality);
        SelectedThumbConcurrency = concurrency;
        SelectedPreviewQuality = quality;
    }

    partial void OnSelectedThumbConcurrencyChanged(string value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        PersistPerformanceChrome();
        StatusText = "Thumbnail decode: " + PerformanceSettings.NormalizeConcurrency(value) + ".";
    }

    partial void OnSelectedPreviewQualityChanged(string value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        PersistPerformanceChrome();
        StatusText = "Preview quality: " + PerformanceSettings.NormalizePreviewQuality(value) + ".";
    }

    partial void OnSelectedDensityChanged(string value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        ApplicationData.Current.LocalSettings.Values[AppearanceSettings.DensityKey] =
            AppearanceSettings.NormalizeDensity(value);
        StatusText = "Density: " + AppearanceSettings.NormalizeDensity(value) + ".";
    }

    partial void OnAnimationsEnabledChanged(bool value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        ApplicationData.Current.LocalSettings.Values[AppearanceSettings.AnimationsKey] = value;
        StatusText = value ? "Animations on." : "Animations off.";
    }

    partial void OnWatchForChangesChanged(bool value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        PersistLibraryChrome();
        _ = ErrorReporter.RunAsync("Watch folders", Notify, async () =>
        {
            await _watchers.RestartAsync();
            StatusText = value
                ? "Watching folders for changes."
                : "Folder watching off — scan manually when needed.";
        });
    }

    partial void OnIndexImagesChanged(bool value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        PersistLibraryChrome();
        StatusText = value ? "Images will be indexed." : "Images skipped on scan.";
    }

    partial void OnIndexVideoChanged(bool value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        PersistLibraryChrome();
        StatusText = value ? "Video will be indexed." : "Video skipped on scan.";
    }

    partial void OnGenerateThumbsOnScanChanged(bool value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        PersistLibraryChrome();
        StatusText = value
            ? "Thumbnails build during scan."
            : "Scan skips thumbnails — mosaic builds them on demand.";
    }

    partial void OnSelectedDefaultZoomChanged(string value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        var zoom = ViewerSettings.ParseZoomLabel(value);
        var label = ViewerSettings.ZoomLabel(zoom);
        if (!string.Equals(value, label, StringComparison.Ordinal))
        {
            SelectedDefaultZoom = label;
            return;
        }

        PersistViewerChrome();
        StatusText = "Default zoom: " + label + ".";
    }

    partial void OnShowDetailsByDefaultChanged(bool value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        PersistViewerChrome();
        StatusText = value ? "Details open with the overlay." : "Details stay closed until toggled.";
    }

    partial void OnLoopVideoChanged(bool value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        PersistViewerChrome();
        StatusText = value ? "Video loops." : "Video stops at the end.";
    }

    partial void OnTouchGesturesEnabledChanged(bool value)
    {
        if (_load.IsLoading)
        {
            return;
        }

        PersistViewerChrome();
        StatusText = value ? "Touch gestures on." : "Touch gestures off — mouse left-drag pan stays.";
    }

    partial void OnSelectedSourceChanged(SourceFolderItem? value) => SyncSourceSelection();

    private void SyncSourceSelection()
    {
        foreach (var source in Sources)
        {
            source.IsSelected = SelectedSource is not null
                && string.Equals(source.Id, SelectedSource.Id, StringComparison.Ordinal);
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
        EnsureBackgroundPresets();
        WallpaperPath = ShellBackground.WallpaperPath ?? "";
        HasWallpaper = ShellBackground.HasWallpaper;
        SelectedPresetKind = ShellBackground.Preset;
        WallpaperLabel = ShellBackground.WallpaperLabel;
        Darkness = ShellBackground.Darkness;
        DarknessLabel = ShellBackground.DarknessLabel(Darkness);
        Blur = ShellBackground.Blur;
        BlurLabel = ShellBackground.BlurLabel(Blur);
        TintEnabled = ShellBackground.TintEnabled;
        TintHex = ShellBackground.TintHex;
        LoadRecentCustomBackgrounds();
        SyncPresetUi();
    }

    private void LoadRecentCustomBackgrounds()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        var localFolder = ApplicationData.Current.LocalFolder.Path;
        var kept = RecentCustomBackgrounds.Parse(values[RecentCustomBackgrounds.SettingsKey]).ToList();

        // Seed MRU from the active custom wallpaper when the list is empty (upgrade path).
        if (kept.Count == 0
            && SelectedPresetKind == ShellPresetKind.Custom
            && !string.IsNullOrWhiteSpace(WallpaperPath)
            && File.Exists(WallpaperPath))
        {
            var fileName = RecentCustomBackgrounds.FileNameOnly(WallpaperPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                string? hash = null;
                try
                {
                    hash = RecentCustomBackgrounds.HashFile(WallpaperPath);
                }
                catch
                {
                    // Hash is best-effort for migration.
                }

                var seeded = RecentCustomBackgrounds.Create(
                    fileName,
                    ShellBackground.WallpaperName ?? fileName,
                    sourcePath: null,
                    Darkness,
                    Blur,
                    hash);
                var (list, _) = RecentCustomBackgrounds.Push([], seeded);
                kept = list.ToList();
                values[RecentCustomBackgrounds.ActiveIdKey] = seeded.Id;
            }
        }

        values[RecentCustomBackgrounds.SettingsKey] = RecentCustomBackgrounds.Serialize(kept);
        var activeId = values[RecentCustomBackgrounds.ActiveIdKey] as string;
        ShowCustomBrokenWarning = false;

        if (SelectedPresetKind == ShellPresetKind.Custom
            && !string.IsNullOrWhiteSpace(WallpaperPath))
        {
            var match = RecentCustomBackgrounds.FindById(kept, activeId)
                ?? RecentCustomBackgrounds.FindByCachedPath(kept, localFolder, WallpaperPath);
            SelectedRecentCustomId = match?.Id;
            if (match is not null)
            {
                values[RecentCustomBackgrounds.ActiveIdKey] = match.Id;
                if (RecentCustomBackgrounds.IsBroken(match, localFolder, File.Exists))
                {
                    // Active image missing at startup → Default + one-time Warning InfoBar.
                    SelectedRecentCustomId = null;
                    values[RecentCustomBackgrounds.ActiveIdKey] = "";
                    SelectedPresetKind = ShellPresetKind.Default;
                    WallpaperPath = "";
                    ShellBackground.SetWallpaperName(null);
                    Darkness = ShellBackground.ParseDarknessFor(
                        ShellPresetKind.Default,
                        values[ShellBackground.DarknessKeyFor(ShellPresetKind.Default)]
                            ?? values[ShellBackground.DarknessKey],
                        PresetSwatchLightChrome());
                    Blur = ShellBackground.ParseBlurFor(
                        ShellPresetKind.Default,
                        values[ShellBackground.BlurKeyFor(ShellPresetKind.Default)]
                            ?? values[ShellBackground.BlurKey],
                        PresetSwatchLightChrome());
                    PersistShellBackground(reloadWallpaper: true);
                    ShowCustomBrokenWarning = true;
                }
            }
        }
        else
        {
            SelectedRecentCustomId = null;
            values[RecentCustomBackgrounds.ActiveIdKey] = "";
        }

        RebuildCustomGroupItems(kept, localFolder);
    }

    private void RebuildCustomGroupItems(
        IReadOnlyList<RecentCustomBackgroundEntry> entries,
        string localFolder)
    {
        // Stable-identity: update / reorder existing ShellPresetItem instances by RecentId
        // so GridView containers and thumb bitmaps are not torn down on every slider tick.
        // Never Clear()+rebuild — Move/Insert/Remove only (see StableIdentityList).
        var byId = CustomBackgroundPresets
            .Where(i => i.IsRecentCustom)
            .ToDictionary(i => i.RecentId!, StringComparer.OrdinalIgnoreCase);
        var add = CustomBackgroundPresets.FirstOrDefault(i => i.IsAddCustom);
        if (add is null)
        {
            add = new ShellPresetItem
            {
                Id = ShellPresetKind.Custom,
                Name = "+ Custom",
                SelectionKey = ShellPresetSelection.KeyFor(ShellPresetKind.Custom),
                IsAddCustom = true,
                Swatch = BuildAddCustomSwatch(),
                ToolTipText = "Add a custom background image"
            };
        }
        else
        {
            add.Name = "+ Custom";
            // Keep the solid swatch instance — do not allocate a new brush each persist.
            add.ToolTipText = "Add a custom background image";
        }

        var ordered = new List<ShellPresetItem>(entries.Count + 1);
        foreach (var entry in entries)
        {
            var path = RecentCustomBackgrounds.ResolveImagePath(entry, localFolder, File.Exists);
            var broken = path is null;
            var label = RecentCustomBackgrounds.DisplayLabel(entry.DisplayName);
            if (!byId.TryGetValue(entry.Id, out var item))
            {
                item = new ShellPresetItem
                {
                    Id = ShellPresetKind.Custom,
                    SelectionKey = ShellPresetSelection.KeyForRecent(entry.Id),
                    RecentId = entry.Id
                };
            }

            var previousPath = item.RecentImagePath;
            var pathChanged = !string.Equals(previousPath, path, StringComparison.OrdinalIgnoreCase);
            item.Name = label;
            item.RecentFileName = entry.FileName;
            item.RecentImagePath = path;
            item.RecentSourcePath = entry.SourcePath;
            item.RecentContentHash = entry.ContentHash;
            item.RecentAddedUtc = entry.DateAddedUtc;
            item.RecentLastUsedUtc = entry.LastUsedUtc;
            item.RecentDarkness = entry.Darkness;
            item.RecentBlur = entry.Blur;
            item.IsBroken = broken;
            item.ToolTipText = RecentCustomBackgrounds.Tooltip(entry.DisplayName, entry.DateAddedUtc);
            // Thumbs: DecodePixelWidth once, cached on the item. Never blur/darken tiles.
            if (broken)
            {
                item.Swatch = BuildBrokenCustomSwatch();
            }
            else if (item.Swatch is null || pathChanged)
            {
                item.Swatch = BuildRecentCustomSwatch(path!);
            }

            ordered.Add(item);
        }

        ordered.Add(add);
        StableIdentityList.Sync(CustomBackgroundPresets, ordered, i => i.SelectionKey);

        var full = new List<ShellPresetItem>(
            ChromeBackgroundPresets.Count
            + VividBackgroundPresets.Count
            + CreatorBackgroundPresets.Count
            + ordered.Count);
        full.AddRange(ChromeBackgroundPresets);
        full.AddRange(VividBackgroundPresets);
        full.AddRange(CreatorBackgroundPresets);
        full.AddRange(ordered);
        StableIdentityList.Sync(BackgroundPresets, full, i => i.SelectionKey);

        ShowCustomEmptyCaption = entries.Count == 0;
    }

    private void ScheduleShellPersist(bool reloadWallpaper = false)
    {
        if (reloadWallpaper)
        {
            _shellPersistCts?.Cancel();
            _shellPersistCts = null;
            PersistShellBackground(reloadWallpaper: true);
            return;
        }

        _shellPersistCts?.Cancel();
        var cts = new CancellationTokenSource();
        _shellPersistCts = cts;
        _ = DebouncedPersistShellAsync(cts.Token);
    }

    private async Task DebouncedPersistShellAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ShellBackground.PersistDebounceMs, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        PersistShellBackground();
    }

    /// <summary>Flush a pending slider persist (Settings unload / section leave).</summary>
    public void FlushShellPersist()
    {
        if (_shellPersistCts is null)
        {
            return;
        }

        _shellPersistCts.Cancel();
        _shellPersistCts = null;
        PersistShellBackground();
    }

    private IReadOnlyList<RecentCustomBackgroundEntry> ReadRecentEntries()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        return RecentCustomBackgrounds.Parse(values[RecentCustomBackgrounds.SettingsKey]);
    }

    private void WriteRecentEntries(IReadOnlyList<RecentCustomBackgroundEntry> entries)
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        values[RecentCustomBackgrounds.SettingsKey] = RecentCustomBackgrounds.Serialize(entries);
        values[RecentCustomBackgrounds.ActiveIdKey] = SelectedRecentCustomId ?? "";
        RebuildCustomGroupItems(entries, ApplicationData.Current.LocalFolder.Path);
        SyncBackgroundSelection();
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
        var path = SelectedPresetKind == ShellPresetKind.Custom ? WallpaperPath : null;
        ShellBackground.Apply(
            SelectedPresetKind, path, Darkness, Blur, TintEnabled, TintHex, reloadWallpaper);
        SelectedPresetKind = ShellBackground.Preset;
        var values = ApplicationData.Current.LocalSettings.Values;
        values[ShellBackground.PresetKey] = ShellBackground.PresetId(ShellBackground.Preset);
        values[ShellBackground.WallpaperPathKey] = ShellBackground.WallpaperPath ?? "";
        values[ShellBackground.WallpaperNameKey] = ShellBackground.WallpaperName ?? "";
        values[ShellBackground.DarknessKey] = ShellBackground.Darkness;
        values[ShellBackground.BlurKey] = ShellBackground.Blur;
        values[ShellBackground.DarknessKeyFor(ShellBackground.Preset)] = ShellBackground.Darkness;
        values[ShellBackground.BlurKeyFor(ShellBackground.Preset)] = ShellBackground.Blur;
        values[ShellBackground.TintEnabledKey] = ShellBackground.TintEnabled;
        values[ShellBackground.TintKey] = ShellBackground.TintHex;
        if (SelectedPresetKind == ShellPresetKind.Custom
            && !string.IsNullOrWhiteSpace(SelectedRecentCustomId))
        {
            var updated = RecentCustomBackgrounds.UpdateAmounts(
                ReadRecentEntries(),
                SelectedRecentCustomId,
                ShellBackground.Darkness,
                ShellBackground.Blur);
            values[RecentCustomBackgrounds.SettingsKey] = RecentCustomBackgrounds.Serialize(updated);
            values[RecentCustomBackgrounds.ActiveIdKey] = SelectedRecentCustomId;
            RebuildCustomGroupItems(updated, ApplicationData.Current.LocalFolder.Path);
        }
        else if (SelectedPresetKind != ShellPresetKind.Custom)
        {
            SelectedRecentCustomId = null;
            values[RecentCustomBackgrounds.ActiveIdKey] = "";
        }

        HasWallpaper = ShellBackground.HasWallpaper;
        WallpaperPath = ShellBackground.WallpaperPath ?? "";
        WallpaperLabel = ShellBackground.WallpaperLabel;
        DarknessLabel = ShellBackground.DarknessLabel(ShellBackground.Darkness);
        BlurLabel = ShellBackground.BlurLabel(ShellBackground.Blur);
        TintHex = ShellBackground.TintHex;
        SyncPresetUi();
    }

    private void EnsureBackgroundPresets()
    {
        if (BackgroundPresets.Count > 0)
        {
            RefreshPresetSwatches();
            return;
        }

        foreach (var kind in ShellBackground.PickerOrder)
        {
            if (kind == ShellPresetKind.Custom)
            {
                // Custom group is rebuilt from MRU (Add image… + recents).
                continue;
            }

            var item = new ShellPresetItem
            {
                Id = kind,
                Name = ShellBackground.PresetDisplayName(kind),
                SelectionKey = ShellPresetSelection.KeyFor(kind),
                Swatch = BuildPresetSwatch(kind)
            };
            BackgroundPresets.Add(item);
            BucketFor(kind).Add(item);
        }
    }

    private ObservableCollection<ShellPresetItem> BucketFor(ShellPresetKind kind) =>
        kind switch
        {
            ShellPresetKind.Custom => CustomBackgroundPresets,
            _ when ShellBackground.PresetGroup(kind) == ShellPresetGroup.Vivid => VividBackgroundPresets,
            _ when ShellBackground.PresetGroup(kind) == ShellPresetGroup.Creator => CreatorBackgroundPresets,
            _ => ChromeBackgroundPresets
        };

    /// <summary>Rebuild picker tiles after theme flip (Light must show *-light-thumb.jpg).</summary>
    public void RefreshBackgroundPresetSwatches() => RefreshPresetSwatches();

    private void RefreshPresetSwatches()
    {
        foreach (var item in BackgroundPresets)
        {
            if (item.IsAddCustom)
            {
                item.Swatch = BuildAddCustomSwatch();
            }
            else if (item.IsRecentCustom)
            {
                item.Swatch = item.IsBroken || string.IsNullOrWhiteSpace(item.RecentImagePath)
                    ? BuildBrokenCustomSwatch()
                    : BuildRecentCustomSwatch(item.RecentImagePath!);
            }
            else
            {
                item.Swatch = BuildPresetSwatch(item.Id);
            }
        }
    }

    private Brush BuildAddCustomSwatch()
    {
        // Gooey: SubtleFillColorSecondary plate + 1 px stroke (glyph drawn in the tile template).
        return new SolidColorBrush(PresetSwatchLightChrome()
            ? Color.FromArgb(255, 0xF3, 0xF3, 0xF3)
            : Color.FromArgb(255, 0x2C, 0x2C, 0x2C));
    }

    private Brush BuildBrokenCustomSwatch() =>
        new SolidColorBrush(PresetSwatchLightChrome()
            ? Color.FromArgb(255, 0xE8, 0xE8, 0xE8)
            : Color.FromArgb(255, 0x28, 0x28, 0x28));

    private Brush BuildRecentCustomSwatch(string imagePath)
    {
        try
        {
            return new ImageBrush
            {
                ImageSource = new BitmapImage
                {
                    DecodePixelWidth = RecentCustomBackgrounds.ThumbDecodeWidth,
                    DecodePixelHeight = RecentCustomBackgrounds.ThumbDecodeHeight,
                    UriSource = new Uri(imagePath, UriKind.Absolute)
                },
                Stretch = Stretch.UniformToFill
            };
        }
        catch
        {
            return BuildBrokenCustomSwatch();
        }
    }

    private void SyncPresetUi()
    {
        BlurEnabled = ShellBackground.BlurApplies;
        DarknessEnabled = ShellBackground.DarknessApplies;
        // Add tile + remove chrome replace the old Change image / Use default row.
        ShowCustomWallpaperActions = false;
        SyncBackgroundSelection();
        RefreshPresetSwatches();
    }

    /// <summary>
    /// One selected tile across chrome / Vivid / Creator / Custom (Add + recents).
    /// Matches blur PR <c>ShellPresetSelection</c> — never per-GridView SelectedItem.
    /// </summary>
    private void SyncBackgroundSelection()
    {
        var activeKey = ActiveBackgroundSelectionKey();
        foreach (var item in BackgroundPresets)
        {
            // Add tile is never the "selected wallpaper" chrome; broken tiles are not selectable.
            item.IsSelected = !item.IsAddCustom
                && !item.IsBroken
                && ShellPresetSelection.Matches(item.SelectionKey, activeKey);
        }

        SelectedPreset = BackgroundPresets.FirstOrDefault(p => p.IsSelected);
    }

    private string ActiveBackgroundSelectionKey() =>
        !string.IsNullOrWhiteSpace(SelectedRecentCustomId)
            ? ShellPresetSelection.KeyForRecent(SelectedRecentCustomId)
            : ShellPresetSelection.KeyFor(SelectedPresetKind);

    /// <summary>
    /// Match the live shell chrome: Light theme → light packaged thumbs / gradient stops.
    /// </summary>
    private bool PresetSwatchLightChrome()
    {
        if (App.Window?.Content is Microsoft.UI.Xaml.FrameworkElement root)
        {
            return root.ActualTheme == Microsoft.UI.Xaml.ElementTheme.Light;
        }

        var systemLight = Microsoft.UI.Xaml.Application.Current?.RequestedTheme
            == Microsoft.UI.Xaml.ApplicationTheme.Light;
        return ShellBackground.IsLightChrome(SelectedTheme, systemLight);
    }

    private Brush BuildPresetSwatch(ShellPresetKind kind)
    {
        var light = PresetSwatchLightChrome();
        if (ShellBackground.IsPackagedImagePreset(kind))
        {
            return new ImageBrush
            {
                ImageSource = new BitmapImage(new Uri(ShellBackground.PackagedThumbUri(kind, light))),
                Stretch = Stretch.UniformToFill
            };
        }

        if (kind == ShellPresetKind.Custom && HasWallpaper && !string.IsNullOrWhiteSpace(WallpaperPath))
        {
            try
            {
                return new ImageBrush
                {
                    ImageSource = new BitmapImage
                    {
                        DecodePixelWidth = 224,
                        UriSource = new Uri(WallpaperPath, UriKind.Absolute)
                    },
                    Stretch = Stretch.UniformToFill
                };
            }
            catch
            {
                // Fall through to solid placeholder.
            }
        }

        if (kind is ShellPresetKind.Mica or ShellPresetKind.MicaAlt)
        {
            return new SolidColorBrush(light
                ? Color.FromArgb(255, 0xD8, 0xD8, 0xD8)
                : Color.FromArgb(255, 0x32, 0x32, 0x32));
        }

        if (kind == ShellPresetKind.Custom)
        {
            return new SolidColorBrush(light
                ? Color.FromArgb(255, 0xE8, 0xE8, 0xE8)
                : Color.FromArgb(255, 0x28, 0x28, 0x28));
        }

        var stops = ShellPresetGradients.TryStops(kind, light)
            ?? ShellPresetGradients.TryStops(ShellPresetKind.Default, light)!.Value;
        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Offset = 0, Color = ParseSwatchColor(stops.Start) },
                new GradientStop { Offset = 0.48, Color = ParseSwatchColor(stops.Mid) },
                new GradientStop { Offset = 1, Color = ParseSwatchColor(stops.End) }
            }
        };
    }

    private static Color ParseSwatchColor(string hex) =>
        ShellBackground.TryParseTint(hex, out var a, out var r, out var g, out var b)
            ? Color.FromArgb(a, r, g, b)
            : Color.FromArgb(255, 20, 20, 24);

    /// <summary>
    /// Picker tile click: Add opens the file picker; recent restores that image;
    /// broken tiles are ignored; packaged presets apply immediately.
    /// </summary>
    public Task SelectBackgroundPresetAsync(ShellPresetItem? item)
    {
        if (item is null || _load.IsLoading || item.IsBroken)
        {
            return Task.CompletedTask;
        }

        if (item.IsAddCustom)
        {
            return PickWallpaperAsync();
        }

        if (item.IsRecentCustom)
        {
            return SelectRecentCustomAsync(item);
        }

        return ApplyPresetAsync(item.Id);
    }

    /// <summary>One-click restore of a Custom-group recent (cache + saved blur/darkness).</summary>
    public Task SelectRecentCustomAsync(ShellPresetItem? item)
    {
        if (item is null || _load.IsLoading || !item.IsRecentCustom || item.IsBroken)
        {
            return Task.CompletedTask;
        }

        return ErrorReporter.RunAsync("Change background", Notify, async () =>
        {
            var localFolder = ApplicationData.Current.LocalFolder.Path;
            var entry = RecentCustomBackgrounds.FindById(ReadRecentEntries(), item.RecentId);
            if (entry is null)
            {
                WriteRecentEntries(ReadRecentEntries());
                return;
            }

            var path = RecentCustomBackgrounds.ResolveImagePath(entry, localFolder, File.Exists);
            if (path is null)
            {
                // Keep the broken placeholder; do not auto-delete — user removes explicitly.
                WriteRecentEntries(ReadRecentEntries());
                StatusText = "That custom background is no longer available.";
                return;
            }

            var previousPath = ShellBackground.WallpaperPath;
            if (!string.Equals(previousPath, path, StringComparison.OrdinalIgnoreCase))
            {
                ShellBackground.ReleaseDisplay();
            }

            WallpaperPath = path;
            SelectedPresetKind = ShellPresetKind.Custom;
            SelectedRecentCustomId = entry.Id;
            ShellBackground.SetWallpaperName(entry.DisplayName);
            Darkness = entry.Darkness;
            Blur = entry.Blur;
            WriteRecentEntries(RecentCustomBackgrounds.TouchLastUsed(ReadRecentEntries(), entry.Id));
            PersistShellBackground(reloadWallpaper: true);
            if (!string.Equals(previousPath, path, StringComparison.OrdinalIgnoreCase))
            {
                await TryDeleteStoredWallpaperAsync(previousPath, ReadRecentEntries());
            }

            StatusText = $"Background: {entry.DisplayName}.";
            await Task.CompletedTask;
        });
    }

    [RelayCommand]
    private Task RemoveRecentCustomAsync(ShellPresetItem? item)
    {
        if (item is null || _load.IsLoading || !item.IsRecentCustom)
        {
            return Task.CompletedTask;
        }

        return ErrorReporter.RunAsync("Remove recent background", Notify, async () =>
        {
            await RemoveRecentCustomCoreAsync(item.RecentId!, notify: true);
        });
    }

    /// <summary>
    /// Soft-remove from Recent. Active image → Default immediately + Undo toast.
    /// Never deletes Palace's cached copy (or the user's original).
    /// </summary>
    private async Task RemoveRecentCustomCoreAsync(string id, bool notify)
    {
        var previous = ReadRecentEntries();
        var removed = RecentCustomBackgrounds.FindById(previous, id);
        if (removed is null)
        {
            return;
        }

        var next = RecentCustomBackgrounds.Remove(previous, id);
        var wasActive = string.Equals(SelectedRecentCustomId, id, StringComparison.OrdinalIgnoreCase)
            || (SelectedPresetKind == ShellPresetKind.Custom
                && string.Equals(
                    RecentCustomBackgrounds.FileNameOnly(ShellBackground.WallpaperPath),
                    removed.FileName,
                    StringComparison.OrdinalIgnoreCase));

        _pendingRecentDelete = removed;
        _pendingRecentDeleteWasActive = wasActive;
        _pendingRecentDeleteDarkness = Darkness;
        _pendingRecentDeleteBlur = Blur;

        SelectedRecentCustomId = wasActive ? null : SelectedRecentCustomId;
        WriteRecentEntries(next);

        if (wasActive)
        {
            await ApplyPresetAsync(ShellPresetKind.Default);
            OfferRecentCustomUndoToast($"Removed {RecentCustomBackgrounds.DisplayLabel(removed.DisplayName)}.");
        }
        else
        {
            // Non-active: still offer Undo; file delete waits for the window.
            OfferRecentCustomUndoToast($"Removed {RecentCustomBackgrounds.DisplayLabel(removed.DisplayName)}.");
        }

        if (notify)
        {
            StatusText = "Removed from Custom.";
        }
    }

    private void OfferRecentCustomUndoToast(string message)
    {
        var epoch = Interlocked.Increment(ref _recentUndoEpoch);
        _recentUndoCts?.Cancel();
        RecentCustomUndoMessage = message;
        ShowRecentCustomUndoToast = true;
        _ = FinalizeRecentDeleteLaterAsync(epoch);
    }

    [RelayCommand]
    private Task UndoRecentCustomRemoveAsync() =>
        ErrorReporter.RunAsync("Undo remove background", Notify, async () =>
        {
            var pending = _pendingRecentDelete;
            if (pending is null)
            {
                ShowRecentCustomUndoToast = false;
                return;
            }

            _recentUndoCts?.Cancel();
            _pendingRecentDelete = null;
            ShowRecentCustomUndoToast = false;

            var (list, evicted) = RecentCustomBackgrounds.Push(
                ReadRecentEntries(),
                pending,
                SelectedRecentCustomId);
            WriteRecentEntries(list);
            foreach (var old in evicted)
            {
                var cache = RecentCustomBackgrounds.CachePath(
                    ApplicationData.Current.LocalFolder.Path,
                    old.FileName);
                await TryDeleteStoredWallpaperAsync(cache, list);
            }

            if (_pendingRecentDeleteWasActive)
            {
                var path = RecentCustomBackgrounds.ResolveImagePath(
                    pending,
                    ApplicationData.Current.LocalFolder.Path,
                    File.Exists);
                if (path is not null)
                {
                    ShellBackground.ReleaseDisplay();
                    WallpaperPath = path;
                    SelectedPresetKind = ShellPresetKind.Custom;
                    SelectedRecentCustomId = pending.Id;
                    ShellBackground.SetWallpaperName(pending.DisplayName);
                    Darkness = _pendingRecentDeleteDarkness;
                    Blur = _pendingRecentDeleteBlur;
                    PersistShellBackground(reloadWallpaper: true);
                }
            }

            StatusText = "Restored custom background.";
            await Task.CompletedTask;
        });

    public void PauseRecentCustomUndoTimer()
    {
        Interlocked.Increment(ref _recentUndoPaused);
        _recentUndoCts?.Cancel();
    }

    public void ResumeRecentCustomUndoTimer()
    {
        if (Interlocked.Decrement(ref _recentUndoPaused) > 0)
        {
            return;
        }

        Interlocked.Exchange(ref _recentUndoPaused, 0);
        if (ShowRecentCustomUndoToast)
        {
            _ = FinalizeRecentDeleteLaterAsync(Volatile.Read(ref _recentUndoEpoch));
        }
    }

    private async Task FinalizeRecentDeleteLaterAsync(int epoch)
    {
        _recentUndoCts?.Cancel();
        _recentUndoCts = new CancellationTokenSource();
        var token = _recentUndoCts.Token;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (epoch != Volatile.Read(ref _recentUndoEpoch) || Volatile.Read(ref _recentUndoPaused) > 0)
        {
            return;
        }

        _pendingRecentDelete = null;
        ShowRecentCustomUndoToast = false;
        // Spec: removing never deletes the file — keep the Palace cache copy.
    }

    private Task ApplyPresetAsync(ShellPresetKind kind) =>
        ErrorReporter.RunAsync("Change background", Notify, async () =>
        {
            var previousPath = ShellBackground.WallpaperPath;
            var previousRecents = ReadRecentEntries();
            var leavingCustom = SelectedPresetKind == ShellPresetKind.Custom
                && kind != ShellPresetKind.Custom;
            if (kind is ShellPresetKind.Mica or ShellPresetKind.MicaAlt
                && !Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            {
                kind = ShellPresetKind.Neutral;
                StatusText = "Mica is not supported here — using Neutral.";
            }

            SelectedPresetKind = kind;
            if (leavingCustom)
            {
                ShellBackground.ReleaseDisplay();
                WallpaperPath = "";
                ShellBackground.SetWallpaperName(null);
                SelectedRecentCustomId = null;
            }

            var values = ApplicationData.Current.LocalSettings.Values;
            var light = PresetSwatchLightChrome();
            Darkness = ShellBackground.ParseDarknessFor(
                kind,
                values[ShellBackground.DarknessKeyFor(kind)]
                    ?? (kind == ShellPresetKind.Default ? values[ShellBackground.DarknessKey] : null),
                light);
            Blur = ShellBackground.ParseBlurFor(
                kind,
                values[ShellBackground.BlurKeyFor(kind)]
                    ?? (kind == ShellPresetKind.Default ? values[ShellBackground.BlurKey] : null),
                light);
            PersistShellBackground(reloadWallpaper: leavingCustom);
            if (leavingCustom)
            {
                // Keep MRU cache files; only delete orphans not listed.
                await TryDeleteStoredWallpaperAsync(previousPath, previousRecents);
            }

            if (string.IsNullOrEmpty(StatusText) || !StatusText.StartsWith("Mica", StringComparison.Ordinal))
            {
                StatusText = $"Background: {ShellBackground.PresetDisplayName(kind)}.";
            }
        });

    private void LoadMediaCache()
    {
        AppServices.LoadMediaCache();
        MediaCacheEnabled = MediaCache.Enabled;
        MediaCacheMaxMb = MediaCache.MaxMb;
        MediaCacheMaxLabel = MediaCache.MaxMbLabel(MediaCache.MaxMb);
        HasCustomCacheFolder = !string.IsNullOrWhiteSpace(MediaCache.RootPath);
        MediaCachePathLabel = MediaCache.RootLabel(AppServices.LocalRoot);
    }

    private void PersistMediaCache()
    {
        MediaCache.Apply(
            MediaCacheEnabled,
            MediaCacheMaxMb,
            MediaCache.RootPath,
            MediaCache.AccessToken);
        var values = ApplicationData.Current.LocalSettings.Values;
        values[MediaCache.EnabledKey] = MediaCache.Enabled;
        values[MediaCache.MaxMbKey] = MediaCache.MaxMb;
        values[MediaCache.RootPathKey] = MediaCache.RootPath ?? "";
        values[MediaCache.AccessTokenKey] = MediaCache.AccessToken ?? "";
        MediaCacheMaxLabel = MediaCache.MaxMbLabel(MediaCache.MaxMb);
        HasCustomCacheFolder = !string.IsNullOrWhiteSpace(MediaCache.RootPath);
        MediaCachePathLabel = MediaCache.RootLabel(AppServices.LocalRoot);
        AppServices.ApplyMediaCacheRoot();
        _ = RefreshMediaCacheUsageAsync();
        if (MediaCache.Enabled)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    MediaCache.EnforceMaxSize(AppServices.ActiveCacheRoot);
                }
                catch
                {
                    // Best-effort size enforce after Settings change.
                }
            });
        }
    }

    private Task RefreshMediaCacheUsageAsync() =>
        ErrorReporter.RunAsync("Measure cache", Notify, async () =>
        {
            var root = AppServices.ActiveCacheRoot;
            var bytes = await Task.Run(() => MediaCache.MeasureBytes(root)).ConfigureAwait(true);
            MediaCacheUsageLabel = MediaCache.Enabled
                ? MediaCache.UsageLabel(bytes, MediaCache.MaxBytes)
                : MediaCache.FormatBytes(bytes);
            MediaCachePathLabel = MediaCache.RootLabel(AppServices.LocalRoot);
        });

    [RelayCommand]
    private Task PickCacheFolderAsync() =>
        ErrorReporter.RunAsync("Choose cache folder", Notify, async () =>
        {
            var folder = await _access.PickFolderAsync();
            if (folder is null)
            {
                return;
            }

            // All projects — watchers cover every SourceFolder path.
            var sources = (await _catalog.GetSourceFoldersAsync()).Select(s => s.Path);
            if (!MediaCache.IsAllowedRoot(folder.Path, sources, out var reason))
            {
                Notify(reason ?? "That folder cannot be used for cache.");
                return;
            }

            var previousToken = MediaCache.AccessToken;
            var token = _access.Remember(folder);
            MediaCache.Apply(MediaCacheEnabled, MediaCacheMaxMb, folder.Path, token);
            if (!string.IsNullOrEmpty(previousToken)
                && !string.Equals(previousToken, token, StringComparison.Ordinal))
            {
                _access.Forget(previousToken);
            }

            PersistMediaCache();
            await RefreshMediaCacheUsageAsync();
            StatusText = "Cache folder updated. Missing thumbs regenerate as you browse.";
        });

    [RelayCommand]
    private Task ResetCacheFolderAsync() =>
        ErrorReporter.RunAsync("Reset cache folder", Notify, async () =>
        {
            var previousToken = MediaCache.AccessToken;
            MediaCache.Apply(MediaCacheEnabled, MediaCacheMaxMb, null, null);
            if (!string.IsNullOrEmpty(previousToken))
            {
                _access.Forget(previousToken);
            }

            PersistMediaCache();
            await RefreshMediaCacheUsageAsync();
            StatusText = "Cache folder reset to the app local folder.";
        });

    [RelayCommand]
    private Task PickCatalogFolderAsync() =>
        ErrorReporter.RunAsync("Choose catalog folder", Notify, async () =>
        {
            var folder = await _access.PickFolderAsync();
            if (folder is null)
            {
                return;
            }

            var root = CatalogStore.NormalizeRoot(folder.Path);
            if (root is null)
            {
                Notify("That folder path is invalid.");
                return;
            }

            Directory.CreateDirectory(root);
            if (CatalogStore.ActiveRoot is { } active
                && !string.Equals(active, root, StringComparison.OrdinalIgnoreCase)
                && !await Task.Run(() => CatalogStore.TryCopyActiveCatalogTo(root)))
            {
                Notify("That folder already has a Palace catalog. It was not overwritten; Palace will open it after restart. Your current catalog stays where it is.");
            }

            CatalogStore.WritePointer(root);
            ApplicationData.Current.LocalSettings.Values[CatalogStore.RootPathKey] = root;
            var token = _access.Remember(folder);
            ApplicationData.Current.LocalSettings.Values[CatalogStore.AccessTokenKey] = token;
            CatalogPathLabel = CatalogStore.RootLabel(root);
            CatalogUsesCustomRoot = !string.Equals(root, CatalogStore.DefaultRoot(), StringComparison.OrdinalIgnoreCase);
            CatalogRestartHint = "Restart Palace to open the catalog in the new folder.";
            StatusText = "Catalog folder saved. Restart Palace to use it.";
        });

    [RelayCommand]
    private Task ResetCatalogFolderAsync() =>
        ErrorReporter.RunAsync("Reset catalog folder", Notify, async () =>
        {
            if (!await SettingsConfirm.ConfirmedAsync(
                    RequestConfirm,
                    SettingsConfirm.ResetCatalogTitle,
                    SettingsConfirm.ResetCatalogBody(),
                    SettingsConfirm.ResetCatalogPrimary))
            {
                return;
            }

            var root = CatalogStore.DefaultRoot();
            Directory.CreateDirectory(root);
            if (CatalogStore.ActiveRoot is { } active
                && !string.Equals(active, root, StringComparison.OrdinalIgnoreCase)
                && !await Task.Run(() => CatalogStore.TryCopyActiveCatalogTo(root)))
            {
                Notify("That folder already has a Palace catalog. It was not overwritten; Palace will open it after restart. Your current catalog stays where it is.");
            }

            CatalogStore.WritePointer(root);
            ApplicationData.Current.LocalSettings.Values[CatalogStore.RootPathKey] = root;
            ApplicationData.Current.LocalSettings.Values[CatalogStore.AccessTokenKey] = "";
            CatalogPathLabel = CatalogStore.RootLabel(root);
            CatalogUsesCustomRoot = false;
            CatalogRestartHint = "Restart Palace to open the default durable catalog folder.";
            StatusText = "Catalog folder reset to the durable default. Restart Palace to use it.";
        });

    [RelayCommand]
    private Task ExportCatalogCopyAsync() =>
        ErrorReporter.RunAsync("Export catalog copy", Notify, async () =>
        {
            var source = CatalogStore.ActiveDbPath;
            if (string.IsNullOrEmpty(source) || !File.Exists(source))
            {
                Notify("No catalog database is open.");
                return;
            }

            var folder = await _access.PickFolderAsync();
            if (folder is null)
            {
                return;
            }

            var name = $"palace-catalog-{DateTime.Now:yyyyMMdd-HHmmss}.db";
            var path = Path.Combine(folder.Path, name);
            await Task.Run(() => CatalogStore.SnapshotDatabase(source, path));
            StatusText = $"Catalog copy saved to {path}";
        });

    [RelayCommand]
    private Task ExportTagsAsync() =>
        ErrorReporter.RunAsync("Export tags", Notify, async () =>
        {
            var json = await _catalog.ExportTagsJsonAsync();
            var file = await _access.PickSaveJsonAsync(
                $"palace-tags-{DateTime.Now:yyyyMMdd}",
                "Palace tags");
            if (file is null)
            {
                return;
            }

            await FileIO.WriteTextAsync(file, json);
            StatusText = $"Exported tags to {file.Path}";
        });

    [RelayCommand]
    private Task ImportTagsAsync() =>
        ErrorReporter.RunAsync("Import tags", Notify, async () =>
        {
            var file = await _access.PickOpenJsonAsync();
            if (file is null)
            {
                return;
            }

            var json = await FileIO.ReadTextAsync(file);
            var plan = await _catalog.ImportTagsJsonAsync(json);
            // Imported implications apply to assets that already carry the source tag.
            if (plan.Implications.Count > 0)
            {
                var idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tag in await _catalog.GetTagsAsync())
                {
                    idByName.TryAdd(tag.Name, tag.Id);
                }

                foreach (var name in plan.Implications.Select(e => e.ParentName).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (idByName.TryGetValue(name, out var sourceId))
                    {
                        await _catalog.BackfillImplicationAddedAsync(sourceId);
                    }
                }
            }

            await AppServices.Tags.RefreshAsync();
            StatusText = "Imported tags. " + plan.Summary;
        });

    [RelayCommand]
    private Task ClearCacheAsync() =>
        ErrorReporter.RunAsync("Clear cache", Notify, async () =>
        {
            if (!await SettingsConfirm.ConfirmedAsync(
                    RequestConfirm,
                    SettingsConfirm.ClearCacheTitle,
                    SettingsConfirm.ClearCacheBody(),
                    SettingsConfirm.ClearCachePrimary))
            {
                return;
            }

            AppServices.Library.BeginBusy("Clearing cache…");
            try
            {
                var root = AppServices.ActiveCacheRoot;
                var deleted = await Task.Run(() => MediaCache.Clear(root)).ConfigureAwait(true);
                await RefreshMediaCacheUsageAsync();
                StatusText = deleted > 0
                    ? $"Cleared {MediaCache.FormatBytes(deleted)} from the cache."
                    : "Cache was already empty.";
            }
            finally
            {
                AppServices.Library.EndBusy();
            }
        });

    [RelayCommand]
    private void CopyAppVersion()
    {
        var data = new DataPackage();
        data.SetText(AppVersionText);
        Clipboard.SetContent(data);
        StatusText = "Version copied.";
    }

    [RelayCommand]
    private Task OpenLicensesAsync() =>
        ErrorReporter.RunAsync("Open licenses", Notify, async () =>
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(
                new Uri("ms-appx:///Assets/Backgrounds/LICENSES.md"));
            await Windows.System.Launcher.LaunchFileAsync(file);
            StatusText = "Opened licenses.";
        });

    [RelayCommand]
    private Task OpenLogsAsync() =>
        ErrorReporter.RunAsync("Open log folder", Notify, async () =>
        {
            var folder = ApplicationData.Current.LocalFolder;
            await Windows.System.Launcher.LaunchFolderAsync(folder);
            StatusText = "Opened log folder (" + ErrorLog.FileName + ").";
        });

    [RelayCommand]
    private Task ExportSettingsAsync() =>
        ErrorReporter.RunAsync("Export settings", Notify, async () =>
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            var pairs = new List<KeyValuePair<string, object>>();
            foreach (var key in SettingsPortable.ExportKeys)
            {
                if (values.TryGetValue(key, out var stored) && stored is not null)
                {
                    pairs.Add(new KeyValuePair<string, object>(key, stored));
                }
            }

            var json = SettingsPortable.Serialize(SettingsPortable.FromValues(pairs));
            var file = await _access.PickSaveJsonAsync(
                $"palace-settings-{DateTime.Now:yyyyMMdd}",
                "Palace settings");
            if (file is null)
            {
                return;
            }

            await FileIO.WriteTextAsync(file, json);
            StatusText = $"Exported settings to {file.Path}";
        });

    [RelayCommand]
    private Task ImportSettingsAsync() =>
        ErrorReporter.RunAsync("Import settings", Notify, async () =>
        {
            if (!await SettingsConfirm.ConfirmedAsync(
                    RequestConfirm,
                    SettingsConfirm.ImportSettingsTitle,
                    SettingsConfirm.ImportSettingsBody(),
                    SettingsConfirm.ImportSettingsPrimary))
            {
                return;
            }

            var file = await _access.PickOpenJsonAsync();
            if (file is null)
            {
                return;
            }

            var json = await FileIO.ReadTextAsync(file);
            var doc = SettingsPortable.Parse(json);
            var values = ApplicationData.Current.LocalSettings.Values;
            var count = SettingsPortable.ApplyTo(
                doc,
                (key, value) => values[key] = value,
                key => values.Remove(key));
            await LoadAsync();
            StatusText = count > 0
                ? $"Imported {count} settings from {file.Name}."
                : "Import file had no matching settings.";
        });

    [RelayCommand]
    private Task GlobalResetAsync() =>
        ErrorReporter.RunAsync("Reset all settings", Notify, async () =>
        {
            if (!await SettingsConfirm.ConfirmedAsync(
                    RequestConfirm,
                    SettingsConfirm.GlobalResetTitle,
                    SettingsConfirm.GlobalResetBody(),
                    SettingsConfirm.GlobalResetPrimary))
            {
                return;
            }

            var values = ApplicationData.Current.LocalSettings.Values;
            foreach (var key in SettingsPortable.ResetKeys)
            {
                values.Remove(key);
            }

            // Also clear per-preset darkness/blur leftovers so chrome returns to defaults.
            var stale = values.Keys
                .Where(k => k.StartsWith("ShellDarkness_", StringComparison.Ordinal)
                    || k.StartsWith("ShellBlur_", StringComparison.Ordinal))
                .ToList();
            foreach (var key in stale)
            {
                values.Remove(key);
            }

            await LoadAsync();
            StatusText = "Settings reset to defaults.";
        });

    [RelayCommand]
    private void ResetDarkness()
    {
        if (_load.IsLoading || !DarknessEnabled)
        {
            return;
        }

        Darkness = ShellBackground.DefaultDarknessFor(SelectedPresetKind, PresetSwatchLightChrome());
    }

    [RelayCommand]
    private void ResetBlur()
    {
        if (_load.IsLoading || !BlurEnabled)
        {
            return;
        }

        Blur = ShellBackground.DefaultBlurFor(SelectedPresetKind, PresetSwatchLightChrome());
    }

    [RelayCommand]
    private Task LocateRecentCustomAsync(ShellPresetItem? item)
    {
        if (item is null || _load.IsLoading || !item.IsRecentCustom || !item.IsBroken)
        {
            return Task.CompletedTask;
        }

        return ErrorReporter.RunAsync("Locate custom background", Notify, async () =>
        {
            var file = await _access.PickImageFileAsync();
            if (file is null)
            {
                return;
            }

            var previous = ReadRecentEntries();
            var entry = RecentCustomBackgrounds.FindById(previous, item.RecentId);
            if (entry is null)
            {
                return;
            }

            var destName = string.IsNullOrWhiteSpace(entry.FileName)
                ? RecentCustomBackgrounds.NewFileName(Path.GetExtension(file.Name))
                : entry.FileName;
            ShellBackground.ReleaseDisplay();
            var dest = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                destName,
                CreationCollisionOption.ReplaceExisting);
            await file.CopyAndReplaceAsync(dest);
            string? hash = null;
            try
            {
                hash = RecentCustomBackgrounds.HashFile(dest.Path);
            }
            catch
            {
                // Hash is best-effort.
            }

            var repaired = entry with
            {
                FileName = destName,
                DisplayName = file.Name,
                SourcePath = file.Path,
                ContentHash = hash ?? entry.ContentHash,
                LastUsedUtc = DateTimeOffset.UtcNow
            };
            var list = previous.Select(e =>
                string.Equals(e.Id, repaired.Id, StringComparison.OrdinalIgnoreCase) ? repaired : e).ToList();
            WriteRecentEntries(list);
            WallpaperPath = dest.Path;
            SelectedPresetKind = ShellPresetKind.Custom;
            SelectedRecentCustomId = repaired.Id;
            ShellBackground.SetWallpaperName(file.Name);
            Darkness = repaired.Darkness;
            Blur = repaired.Blur;
            ShowCustomBrokenWarning = false;
            PersistShellBackground(reloadWallpaper: true);
            StatusText = "Located custom background.";
        });
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
            var previousRecents = ReadRecentEntries();
            var previousRecentId = SelectedRecentCustomId;
            ShellBackground.ReleaseDisplay();
            StorageFile? dest = null;
            var createdNewFile = false;
            try
            {
                // Copy first so we can hash the stored bytes (same content → move front).
                var fileName = RecentCustomBackgrounds.NewFileName(Path.GetExtension(file.Name));
                dest = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    fileName,
                    CreationCollisionOption.ReplaceExisting);
                createdNewFile = true;
                await file.CopyAndReplaceAsync(dest);

                var hash = RecentCustomBackgrounds.HashFile(dest.Path);
                var existing = RecentCustomBackgrounds.FindByHash(previousRecents, hash);
                RecentCustomBackgroundEntry entry;
                IReadOnlyList<RecentCustomBackgroundEntry> pushed;
                IReadOnlyList<RecentCustomBackgroundEntry> evicted;
                if (existing is not null)
                {
                    // Same content: move existing to front; delete the unused new copy.
                    entry = existing with
                    {
                        DisplayName = file.Name,
                        SourcePath = file.Path,
                        Darkness = Darkness,
                        Blur = Blur,
                        ContentHash = hash,
                        DateAddedUtc = DateTimeOffset.UtcNow,
                        LastUsedUtc = DateTimeOffset.UtcNow
                    };
                    (pushed, evicted) = RecentCustomBackgrounds.Push(
                        previousRecents,
                        entry,
                        existing.Id);
                    await TryDeleteStoredWallpaperAsync(dest.Path, pushed);
                    dest = null;
                    createdNewFile = false;
                    WallpaperPath = RecentCustomBackgrounds.CachePath(
                        ApplicationData.Current.LocalFolder.Path,
                        existing.FileName);
                }
                else
                {
                    entry = RecentCustomBackgrounds.Create(
                        fileName,
                        file.Name,
                        file.Path,
                        Darkness,
                        Blur,
                        hash);
                    (pushed, evicted) = RecentCustomBackgrounds.Push(
                        previousRecents,
                        entry,
                        SelectedRecentCustomId);
                    WallpaperPath = dest.Path;
                }

                SelectedPresetKind = ShellPresetKind.Custom;
                SelectedRecentCustomId = entry.Id;
                ShellBackground.SetWallpaperName(file.Name);
                WriteRecentEntries(pushed);
                PersistShellBackground(reloadWallpaper: true);

                foreach (var old in evicted)
                {
                    var cache = RecentCustomBackgrounds.CachePath(
                        ApplicationData.Current.LocalFolder.Path,
                        old.FileName);
                    await TryDeleteStoredWallpaperAsync(cache, pushed);
                }

                await TryDeleteStoredWallpaperAsync(previous, pushed);
                StatusText = "Wallpaper updated.";
            }
            catch
            {
                SelectedRecentCustomId = previousRecentId;
                if (createdNewFile
                    && dest is not null
                    && !string.Equals(ShellBackground.WallpaperPath, dest.Path, StringComparison.OrdinalIgnoreCase))
                {
                    await TryDeleteStoredWallpaperAsync(dest.Path, previousRecents);
                }

                ShellBackground.SetWallpaperName(previousName);
                ShellBackground.RestoreDisplay();
                throw;
            }
        });

    [RelayCommand]
    private Task ClearWallpaperAsync() =>
        ApplyPresetAsync(ShellPresetKind.Default);

    private static async Task TryDeleteStoredWallpaperAsync(
        string? path,
        IEnumerable<RecentCustomBackgroundEntry>? keepListed = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var listed = keepListed?.ToList() ?? [];
        if (RecentCustomBackgrounds.ShouldKeepStoredFile(path, listed))
        {
            return;
        }

        if (!ShellBackground.IsStoredWallpaperName(path)
            && !RecentCustomBackgrounds.IsStoredRecentName(path))
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
            // Production uses the folder picker; UI tests seed SourceFolder rows
            // via CatalogService (never the GUI picker).
            await AppServices.Library.AddFolderCommand.ExecuteAsync(null);
            await LoadAsync();
        });

    [RelayCommand]
    private void SelectSource(SourceFolderItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedSource = item;
        StatusText = "Selected " + item.Path;
    }

    [RelayCommand]
    private Task OpenSourceAsync(SourceFolderItem? item) =>
        ErrorReporter.RunAsync("Open folder", Notify, () =>
        {
            if (item is null)
            {
                return Task.CompletedTask;
            }

            SelectedSource = item;
            if (item.Kind != nameof(SourceKind.Local) || !Directory.Exists(item.Path))
            {
                StatusText = item.Kind == nameof(SourceKind.Local)
                    ? "Folder is missing on disk."
                    : "Open in Explorer is for local folders.";
                return Task.CompletedTask;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{item.Path}\"",
                UseShellExecute = true
            });
            StatusText = "Opened " + item.Path;
            return Task.CompletedTask;
        });

    [RelayCommand]
    private Task RemoveSourceAsync(SourceFolderItem? item) =>
        ErrorReporter.RunAsync("Remove source", Notify, async () =>
        {
            item ??= SelectedSource;
            if (item is null)
            {
                return;
            }

            SelectedSource = item;
            var assetCount = await _catalog.CountAssetsForSourceAsync(item.Id);
            if (!await SettingsConfirm.ConfirmedAsync(
                    RequestConfirm,
                    SettingsConfirm.RemoveFolderTitle,
                    SettingsConfirm.RemoveFolderBody(item.Path, assetCount),
                    SettingsConfirm.RemoveFolderPrimary))
            {
                return;
            }

            var folder = await _catalog.GetSourceFolderAsync(item.Id);
            if (folder is not null)
            {
                _access.Forget(folder.AccessToken);
            }

            await _catalog.DeleteSourceFolderAsync(item.Id);
            await _watchers.RestartAsync();
            await LoadAsync();
            await AppServices.Library.LoadAsync();
        });

    [RelayCommand]
    private Task ScanLibraryAsync() =>
        ErrorReporter.RunAsync("Scan library", Notify, async () =>
        {
            StatusText = "Scanning…";
            await AppServices.Library.ScanCommand.ExecuteAsync(null);
            StatusText = AppServices.Library.StatusText;
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
            if (!await SettingsConfirm.ConfirmedAsync(
                    RequestConfirm,
                    SettingsConfirm.DisconnectTitle("OneDrive"),
                    SettingsConfirm.DisconnectBody("OneDrive"),
                    SettingsConfirm.DisconnectPrimary))
            {
                return;
            }

            await _cloud.DisconnectAsync(CloudProvider.OneDrive);
            await RefreshCloudStatusAsync();
            StatusText = "Disconnected OneDrive.";
        });

    [RelayCommand]
    private Task DisconnectDropboxAsync() =>
        ErrorReporter.RunAsync("Disconnect Dropbox", Notify, async () =>
        {
            if (!await SettingsConfirm.ConfirmedAsync(
                    RequestConfirm,
                    SettingsConfirm.DisconnectTitle("Dropbox"),
                    SettingsConfirm.DisconnectBody("Dropbox"),
                    SettingsConfirm.DisconnectPrimary))
            {
                return;
            }

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
        // DestinationPolicyIndex mirrors DestinationPolicy, which already saves.
        if (_load.IsLoading || sender is not SourceFolderItem item
            || e.PropertyName == nameof(SourceFolderItem.DestinationPolicyIndex))
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
        // Do not ShellBackground.Refresh() here: MainWindow already re-paints on
        // ActualThemeChanged. A second apply that recreates SystemBackdrop mid-theme
        // AVs in Microsoft.UI.Xaml.dll (Mica Dark↔Light crash-loop).
    }
}
