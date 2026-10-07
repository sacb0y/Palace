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
    public static CloudAccountService CloudAccounts { get; private set; } = null!;
    public static ICloudLibraryFactory CloudLibraries { get; private set; } = null!;
    public static HydrationService Hydration { get; private set; } = null!;
    public static LibraryViewModel Library { get; private set; } = null!;
    public static TagsViewModel Tags { get; private set; } = null!;
    public static RoomsViewModel Rooms { get; private set; } = null!;
    public static SettingsViewModel Settings { get; private set; } = null!;
    public static string LocalRoot { get; private set; } = "";
    public static Project CurrentProject { get; private set; } = null!;

    public static async Task InitializeAsync()
    {
        LocalRoot = ApplicationData.Current.LocalFolder.Path;
        LoadPeakOverride();
        LoadShellBackground();
        LoadMediaCache();
        var dbPath = Path.Combine(LocalRoot, "palace.db");
        var thumbs = MediaCache.ResolveThumbsRoot(LocalRoot);
        Directory.CreateDirectory(thumbs);
        Directory.CreateDirectory(MediaCache.ResolvePreviewRoot(LocalRoot));

        Db = await Task.Run(() => new PalaceDb(dbPath));
        Catalog = new CatalogService(Db);
        await RestoreCurrentProjectAsync();
        Access = new AccessService();
        Metadata = new MetadataExtractorService();
        Thumbnails = new ThumbnailService(thumbs);
        Organize = new OrganizeService(Catalog);
        CloudAccounts = new CloudAccountService(Catalog);
        CloudLibraries = new CloudLibraryFactory(Catalog, CloudAccounts);
        Hydration = new HydrationService(Catalog, Metadata, Thumbnails);
        Scan = new ScanService(Catalog, Metadata, Thumbnails, Organize, CloudLibraries);
        Watchers = new WatcherService(Catalog, Scan);
        Library = new LibraryViewModel(Catalog, Access, Scan, Organize, Thumbnails, Watchers);
        Tags = new TagsViewModel(Catalog, Organize, Access, Thumbnails);
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
        ShellBackground.SetWallpaperName(ShellBackground.ParsePath(values[ShellBackground.WallpaperNameKey]));
        ShellBackground.Apply(
            ShellBackground.ParsePath(values[ShellBackground.WallpaperPathKey]),
            ShellBackground.ParseDarkness(values[ShellBackground.DarknessKey]),
            ShellBackground.ParseBlur(values[ShellBackground.BlurKey]),
            ShellBackground.ParseEnabled(values[ShellBackground.TintEnabledKey]),
            ShellBackground.ParseTint(values[ShellBackground.TintKey]));
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
    /// files regenerate lazily.
    /// </summary>
    public static void ApplyMediaCacheRoot()
    {
        if (string.IsNullOrEmpty(LocalRoot) || Thumbnails is null)
        {
            return;
        }

        var thumbs = MediaCache.ResolveThumbsRoot(LocalRoot);
        Directory.CreateDirectory(thumbs);
        Directory.CreateDirectory(MediaCache.ResolvePreviewRoot(LocalRoot));
        Thumbnails.SetRoot(thumbs);
    }

    public static string ActiveCacheRoot =>
        string.IsNullOrEmpty(LocalRoot) ? "" : MediaCache.ResolveRoot(LocalRoot);

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
