using Palace.Data;
using Palace.ViewModels;
using Windows.Storage;

namespace Palace.Services;

public static class AppServices
{
    public static PalaceDb Db { get; private set; } = null!;
    public static CatalogService Catalog { get; private set; } = null!;
    public static AccessService Access { get; private set; } = null!;
    public static MetadataExtractorService Metadata { get; private set; } = null!;
    public static ThumbnailService Thumbnails { get; private set; } = null!;
    public static OrganizeService Organize { get; private set; } = null!;
    public static ScanService Scan { get; private set; } = null!;
    public static WatcherService Watchers { get; private set; } = null!;
    public static LibraryViewModel Library { get; private set; } = null!;
    public static TagsViewModel Tags { get; private set; } = null!;
    public static RoomsViewModel Rooms { get; private set; } = null!;
    public static SettingsViewModel Settings { get; private set; } = null!;
    public static string LocalRoot { get; private set; } = "";

    public static async Task InitializeAsync()
    {
        LocalRoot = ApplicationData.Current.LocalFolder.Path;
        var dbPath = Path.Combine(LocalRoot, "palace.db");
        var thumbs = Path.Combine(LocalRoot, "thumbs");
        Directory.CreateDirectory(thumbs);

        Db = new PalaceDb(dbPath);
        Catalog = new CatalogService(Db);
        Access = new AccessService();
        Metadata = new MetadataExtractorService();
        Thumbnails = new ThumbnailService(thumbs);
        Organize = new OrganizeService(Catalog);
        Scan = new ScanService(Catalog, Metadata, Thumbnails, Organize);
        Watchers = new WatcherService(Catalog, Scan);
        Library = new LibraryViewModel(Catalog, Access, Scan, Organize, Thumbnails, Watchers);
        Tags = new TagsViewModel(Catalog, Organize, Access);
        Rooms = new RoomsViewModel(Catalog, Thumbnails);
        Settings = new SettingsViewModel(Catalog, Access, Scan, Watchers);
        Watchers.SetCallback(_ =>
        {
            App.DispatcherQueue.TryEnqueue(async () =>
            {
                await Library.RefreshQuietAsync();
                await Rooms.RefreshAsync();
            });
        });
        await Watchers.RestartAsync();
        await Library.LoadAsync();
        await Tags.LoadAsync();
        await Rooms.LoadAsync();
        await Settings.LoadAsync();
    }
}
