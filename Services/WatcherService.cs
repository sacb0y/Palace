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

    public WatcherService(CatalogService catalog, ScanService scan)
    {
        _catalog = catalog;
        _scan = scan;
    }

    public void SetCallback(Action<string> onChanged) => _onChanged = onChanged;

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
            watcher.Created += (_, e) => Debounce(e.FullPath);
            watcher.Changed += (_, e) => Debounce(e.FullPath);
            watcher.Deleted += (_, e) => Debounce(e.FullPath);
            watcher.Renamed += (_, e) => Debounce(e.FullPath);
            _watchers.Add(watcher);
        }
        catch
        {
            // Folder may be inaccessible.
        }
    }

    private void Debounce(string path)
    {
        lock (_gate)
        {
            if (_debounce.TryGetValue(path, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            var cts = new CancellationTokenSource();
            _debounce[path] = cts;
            _ = HandleLaterAsync(path, cts.Token);
        }
    }

    private async Task HandleLaterAsync(string path, CancellationToken ct)
    {
        try
        {
            await Task.Delay(400, ct).ConfigureAwait(false);
            await HandleAsync(path).ConfigureAwait(false);
            _onChanged?.Invoke(path);
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

    private async Task HandleAsync(string path)
    {
        var source = await _catalog.FindSourceByPathAsync(path);
        if (source is null)
        {
            return;
        }

        if (Directory.Exists(path))
        {
            await _scan.ScanSourceAsync(source);
            return;
        }

        var asset = await _catalog.GetAssetByPathAsync(path);
        if (!Helpers.CloudFile.Exists(path))
        {
            if (asset is not null)
            {
                await _catalog.MarkOrphanAsync(asset.Id, true);
            }

            return;
        }

        if (!Helpers.PathSafe.IsCatalogExt(Path.GetExtension(path)))
        {
            return;
        }

        await _scan.IndexFileAsync(source, path, autoOrganize: source.AutoOrganizeNewFiles);
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
        }
    }
}
