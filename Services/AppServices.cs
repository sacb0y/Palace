using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services.Cloud;
using Palace.ViewModels;
using Windows.Storage;

namespace Palace.Services;

public static class AppServices
{
    private const string CurrentProjectKey = "CurrentProjectId";

    public static PalaceDb Db { get; private set; } = null!;
    public static CatalogService Catalog { get; private set; } = null!;
    public static AccessService Access { get; private set; } = null!;
    public static MetadataExtractorService Metadata { get; private set; } = null!;
    public static ThumbnailService Thumbnails { get; private set; } = null!;
    public static OrganizeService Organize { get; private set; } = null!;
    public static ScanService Scan { get; private set; } = null!;
    public static WatcherService Watchers { get; private set; } = null!;
    public static FileOpService FileOps { get; private set; } = null!;
    public static CloudAccountService CloudAccounts { get; private set; } = null!;
    public static ICloudLibraryFactory CloudLibraries { get; private set; } = null!;
    public static HydrationService Hydration { get; private set; } = null!;
    public static LibraryViewModel Library { get; private set; } = null!;
    public static TagsViewModel Tags { get; private set; } = null!;
    public static RoomsViewModel Rooms { get; private set; } = null!;
    public static SettingsViewModel Settings { get; private set; } = null!;
    public static string LocalRoot { get; private set; } = "";
    public static Project CurrentProject { get; private set; } = null!;

    /// <summary>
    /// After PalaceDb folded duplicate tag names, survivors may carry implications the merged
    /// assets have not received yet; backfill them off the UI thread (also refreshes FTS).
    /// </summary>
    private static void ReportTagNameMerge()
    {
        if (Db.TagNameMergeError is { } error)
        {
            ErrorReporter.Report("Merge duplicate tag names", error);
        }

        var survivors = Db.TagNameMerge.SurvivorIds;
        if (survivors.Count == 0)
        {
            return;
        }

        _ = Task.Run(() => ErrorReporter.RunAsync("Apply implied tags after merging duplicate names", null, async () =>
        {
            foreach (var id in survivors)
            {
                await Catalog.BackfillImplicationAddedAsync(id).ConfigureAwait(false);
            }
        }));
    }

    public static async Task InitializeAsync()
    {
        LocalRoot = ApplicationData.Current.LocalFolder.Path;
        LoadPeakOverride();
        LoadShellBackground();
        LoadMediaCache();
        // Side-by-side/test packages must never open the real catalog.
        try
        {
            CatalogStore.DataDirOverride ??= CatalogStore.IsolatedDataDirFor(
                Windows.ApplicationModel.Package.Current.Id.Name, LocalRoot);
        }
        catch (InvalidOperationException)
        {
            // Unpackaged: keep PALACE_DATA_DIR / %LOCALAPPDATA%\Palace.
        }

        var settingsRoot = CatalogStore.ParsePath(
            ApplicationData.Current.LocalSettings.Values[CatalogStore.RootPathKey]);
        var dbPath = CatalogStore.ResolveDbPath(LocalRoot, settingsRoot);
        // Don't overwrite the saved root with a fallback; the next launch should retry it.
        if (CatalogStore.FallbackReason is null)
        {
            ApplicationData.Current.LocalSettings.Values[CatalogStore.RootPathKey] =
                CatalogStore.ActiveRoot ?? "";
        }
        // Custom cache drive may be unplugged — fall back to LocalFolder thumbs.
        MediaCache.EnsureRoots(LocalRoot, out var thumbs, out _);

        Db = await Task.Run(() => new PalaceDb(dbPath));
        Catalog = new CatalogService(Db);
        ReportTagNameMerge();
        await RestoreCurrentProjectAsync();
        Access = new AccessService();
        Metadata = new MetadataExtractorService();
        Thumbnails = new ThumbnailService(thumbs);
        CloudAccounts = new CloudAccountService(Catalog);
        CloudLibraries = new CloudLibraryFactory(Catalog, CloudAccounts);
        Hydration = new HydrationService(Catalog, Metadata, Thumbnails);
        // FileOps → Organize → Scan → Watchers. Suppress closes over Watchers once assigned.
        WatcherService? watchers = null;
        FileOps = new FileOpService(Catalog, () => watchers!.SuppressNotifications());
        Organize = new OrganizeService(Catalog, FileOps);
        Scan = new ScanService(Catalog, Metadata, Thumbnails, Organize, CloudLibraries);
        Watchers = new WatcherService(Catalog, Scan);
        watchers = Watchers;
        Library = new LibraryViewModel(Catalog, Access, Scan, Organize, Thumbnails, Watchers, FileOps);
        Tags = new TagsViewModel(Catalog, Organize, Access, Thumbnails, FileOps);
        Rooms = new RoomsViewModel(Catalog, Thumbnails);
        Settings = new SettingsViewModel(Catalog, Access, Scan, Watchers, CloudAccounts, CloudLibraries);
        Watchers.SetCallback(_ =>
        {
            App.DispatcherQueue.TryEnqueue(async () =>
            {
                await Library.RefreshQuietAsync();
                await Rooms.RefreshAsync();
            });
        });
    }

    public static async Task LoadInitialDataAsync()
    {
        if (Library is null)
        {
            return;
        }

        Library.BeginBusy("Loading…");
        try
        {
            await Watchers.RestartAsync();
            await Library.LoadAsync();
            await Tags.LoadAsync();
            await Rooms.LoadAsync();
            await Settings.LoadAsync();
        }
        finally
        {
            Library.EndBusy();
        }
    }

    public static async Task SetCurrentProjectAsync(Project project)
    {
        CurrentProject = project;
        ApplicationData.Current.LocalSettings.Values[CurrentProjectKey] = project.Id;
        if (Library is null)
        {
            return;
        }

        Library.BeginBusy("Loading…");
        try
        {
            await Library.LoadAsync();
            await Tags.RefreshAsync();
            await Rooms.RefreshAsync();
            await Settings.LoadAsync();
        }
        finally
        {
            Library.EndBusy();
        }
    }

    private static void LoadPeakOverride()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        GalleryPeak.Apply(
            GalleryPeak.ParseEnabled(values[GalleryPeak.EnabledKey]),
            GalleryPeak.ParseNits(values[GalleryPeak.NitsKey]));
    }

    internal static void LoadShellBackground()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        bool micaSupported;
        try
        {
            micaSupported = Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported();
        }
        catch
        {
            micaSupported = false;
        }

        ShellBackground.SettingsSnapshot snap;
        try
        {
            snap = ShellBackground.ResolveFromStorage(
                values[ShellBackground.PresetKey],
                values[ShellBackground.WallpaperPathKey],
                values[ShellBackground.WallpaperNameKey],
                preset => values[ShellBackground.DarknessKeyFor(preset)],
                preset => values[ShellBackground.BlurKeyFor(preset)],
                values[ShellBackground.DarknessKey],
                values[ShellBackground.BlurKey],
                values[ShellBackground.TintEnabledKey],
                values[ShellBackground.TintKey],
                micaSupported,
                LightChrome(values["Theme"] as string));
        }
        catch
        {
            snap = ShellBackground.DefaultSettingsSnapshot();
        }

        if (snap.FellBackToDefault)
        {
            // Persist Default so a corrupt blob cannot crash-loop every launch.
            values[ShellBackground.PresetKey] = ShellBackground.PresetId(ShellPresetKind.Default);
            values[ShellBackground.WallpaperPathKey] = "";
            values[ShellBackground.WallpaperNameKey] = "";
            values[ShellBackground.DarknessKey] = ShellBackground.DefaultDarkness;
            values[ShellBackground.BlurKey] = ShellBackground.DefaultBlur;
            values[ShellBackground.TintEnabledKey] = false;
            values[ShellBackground.TintKey] = ShellBackground.DefaultTintHex;
        }

        ShellBackground.SetWallpaperName(snap.WallpaperName);
        ShellBackground.Apply(
            snap.Preset,
            snap.WallpaperPath,
            snap.Darkness,
            snap.Blur,
            snap.TintEnabled,
            snap.TintHex,
            reloadWallpaper: true);
    }

    private static bool LightChrome(string? theme)
    {
        var systemLight = false;
        try
        {
            systemLight = Microsoft.UI.Xaml.Application.Current?.RequestedTheme
                == Microsoft.UI.Xaml.ApplicationTheme.Light;
        }
        catch
        {
            // Theme is not available yet; dark defaults until Settings applies a theme.
        }

        return ShellBackground.IsLightChrome(theme, systemLight);
    }

    internal static void LoadMediaCache()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        MediaCache.Apply(
            MediaCache.ParseEnabled(values[MediaCache.EnabledKey]),
            MediaCache.ParseMaxMb(values[MediaCache.MaxMbKey]),
            MediaCache.ParsePath(values[MediaCache.RootPathKey]),
            MediaCache.ParsePath(values[MediaCache.AccessTokenKey]));
    }

    /// <summary>
    /// Re-point the live <see cref="ThumbnailService"/> at the resolved thumbs
    /// root after Settings changes. Does not move existing JPEGs — missing
    /// files regenerate lazily. Falls back to LocalFolder when the custom
    /// root cannot be created.
    /// </summary>
    public static void ApplyMediaCacheRoot()
    {
        if (string.IsNullOrEmpty(LocalRoot) || Thumbnails is null)
        {
            return;
        }

        MediaCache.EnsureRoots(LocalRoot, out var thumbs, out _);
        Thumbnails.SetRoot(thumbs);
    }

    public static string ActiveCacheRoot
    {
        get
        {
            if (!string.IsNullOrEmpty(MediaCache.EffectiveRoot))
            {
                return MediaCache.EffectiveRoot!;
            }

            return string.IsNullOrEmpty(LocalRoot) ? "" : MediaCache.ResolveRoot(LocalRoot);
        }
    }

    private static async Task RestoreCurrentProjectAsync()
    {
        var projects = await Catalog.GetProjectsAsync();
        if (projects.Count == 0)
        {
            projects = [await Catalog.CreateProjectAsync("Palace")];
        }

        var stored = ApplicationData.Current.LocalSettings.Values[CurrentProjectKey] as string;
        CurrentProject = projects.FirstOrDefault(p => p.Id == stored) ?? projects[0];
        ApplicationData.Current.LocalSettings.Values[CurrentProjectKey] = CurrentProject.Id;
    }
}
