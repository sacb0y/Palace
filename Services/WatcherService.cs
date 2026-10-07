using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

public sealed class WatcherService : IDisposable
{
    private readonly CatalogService _catalog;
    private readonly ScanService _scan;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<string, CancellationTokenSource> _debounce = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private Action<string>? _onChanged;
    private CancellationTokenSource? _notifyCts;
    private int _suppressDepth;
    private long _quietUntilTick;

    /// <summary>Linger after intentional delete so late LastWrite events do not RefreshQuiet.</summary>
    public static readonly TimeSpan SuppressLinger = TimeSpan.FromMilliseconds(1500);

    public WatcherService(CatalogService catalog, ScanService scan)
    {
        _catalog = catalog;
        _scan = scan;
    }

    public void SetCallback(Action<string> onChanged) => _onChanged = onChanged;

    /// <summary>
    /// Suppress watcher catalog work and UI refresh (intentional Library delete).
    /// Disk events still fire; they are ignored until dispose (+ linger) so multi-delete
    /// does not queue a full source scan or N× RefreshQuietAsync.
    /// </summary>
    public IDisposable SuppressNotifications()
    {
        Interlocked.Increment(ref _suppressDepth);
        return new SuppressScope(this);
    }

    public bool IsSuppressed =>
        Volatile.Read(ref _suppressDepth) > 0
        || Environment.TickCount64 < Volatile.Read(ref _quietUntilTick);

    public async Task RestartAsync()
    {
        DisposeWatchers();
        foreach (var source in await _catalog.GetSourceFoldersAsync())
        {
            Start(source);
        }
    }

    public void Start(SourceFolder source)
    {
        if (source.Kind != SourceKind.Local || !Directory.Exists(source.Path))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(source.Path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Created += (_, e) => Debounce(e.FullPath, WatcherChangeKinds.Created);
            watcher.Changed += (_, e) => Debounce(e.FullPath, WatcherChangeKinds.Changed);
            watcher.Deleted += (_, e) => Debounce(e.FullPath, WatcherChangeKinds.Deleted);
            watcher.Renamed += (_, e) => Debounce(e.FullPath, WatcherChangeKinds.Renamed);
            _watchers.Add(watcher);
        }
        catch
        {
            // Folder may be inaccessible.
        }
    }

    private void Debounce(string path, WatcherChangeKinds change)
    {
        if (IsSuppressed)
        {
            return;
        }

        lock (_gate)
        {
            var key = path + "\0" + (int)change;
            if (_debounce.TryGetValue(key, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            var cts = new CancellationTokenSource();
            _debounce[key] = cts;
            _ = HandleLaterAsync(path, change, cts.Token);
        }
    }

    private async Task HandleLaterAsync(string path, WatcherChangeKinds change, CancellationToken ct)
    {
        try
        {
            await Task.Delay(400, ct).ConfigureAwait(false);
            if (IsSuppressed)
            {
                return;
            }

            if (await HandleAsync(path, change).ConfigureAwait(false))
            {
                ScheduleNotify(path);
            }
        }
        catch (OperationCanceledException)
        {
            // superseded
        }
        catch
        {
            // watcher must never crash the app
        }
    }

    /// <returns>True when catalog state changed and the UI should refresh.</returns>
    private async Task<bool> HandleAsync(string path, WatcherChangeKinds change)
    {
        var source = await _catalog.FindSourceByPathAsync(path);
        if (source is null)
        {
            return false;
        }

        if (Directory.Exists(path))
        {
            // Parent LastWrite on multi-delete must not ScanSourceAsync the whole tree.
            if (!MosaicDelete.ShouldIndexDirectory(change, directoryExists: true))
            {
                return false;
            }

            await _scan.ScanDirectoryAsync(source, path);
            return true;
        }

        var asset = await _catalog.GetAssetByPathAsync(path);
        if (!Helpers.CloudFile.Exists(path))
        {
            if (asset is not null)
            {
                await _catalog.MarkOrphanAsync(asset.Id, true);
                return true;
            }

            if (change == WatcherChangeKinds.Deleted
                && MosaicDelete.LooksLikeDeletedDirectory(path, hadMatchingAsset: false))
            {
                await _catalog.MarkOrphansUnderPrefixAsync(source.Id, path);
                return true;
            }

            return false;
        }

        if (!Helpers.PathSafe.IsCatalogExt(Path.GetExtension(path)))
        {
            return false;
        }

        await _scan.IndexFileAsync(source, path, autoOrganize: source.AutoOrganizeNewFiles);
        return true;
    }

    /// <summary>Coalesce bursty file events into one UI refresh.</summary>
    private void ScheduleNotify(string path)
    {
        if (IsSuppressed || _onChanged is null)
        {
            return;
        }

        lock (_gate)
        {
            _notifyCts?.Cancel();
            _notifyCts?.Dispose();
            var cts = new CancellationTokenSource();
            _notifyCts = cts;
            _ = NotifyLaterAsync(path, cts.Token);
        }
    }

    private async Task NotifyLaterAsync(string path, CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct).ConfigureAwait(false);
            if (IsSuppressed)
            {
                return;
            }

            _onChanged?.Invoke(path);
        }
        catch (OperationCanceledException)
        {
            // superseded
        }
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    public void Dispose()
    {
        DisposeWatchers();
        lock (_gate)
        {
            foreach (var cts in _debounce.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _debounce.Clear();
            _notifyCts?.Cancel();
            _notifyCts?.Dispose();
            _notifyCts = null;
        }
    }

        private sealed class SuppressScope : IDisposable
    {
        private WatcherService? _owner;

        public SuppressScope(WatcherService owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
            {
                return;
            }

            Interlocked.Decrement(ref owner._suppressDepth);
            // Late parent LastWrite / Deleted events often arrive after Recycle returns.
            Volatile.Write(
                ref owner._quietUntilTick,
                Environment.TickCount64 + (long)SuppressLinger.TotalMilliseconds);
        }
    }
}
