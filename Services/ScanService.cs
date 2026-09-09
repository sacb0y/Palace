using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

public sealed class ScanService
{
    private readonly CatalogService _catalog;
    private readonly MetadataExtractorService _metadata;
    private readonly ThumbnailService _thumbs;
    private readonly OrganizeService _organize;

    public ScanService(
        CatalogService catalog,
        MetadataExtractorService metadata,
        ThumbnailService thumbs,
        OrganizeService organize)
    {
        _catalog = catalog;
        _metadata = metadata;
        _thumbs = thumbs;
        _organize = organize;
    }

    public async Task<ScanReport> ScanAllAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        string? projectId = null)
    {
        var report = new ScanReport();
        foreach (var source in await _catalog.GetSourceFoldersAsync(projectId))
        {
            ct.ThrowIfCancellationRequested();
            var part = await ScanSourceAsync(source, progress, ct).ConfigureAwait(false);
            report.Added += part.Added;
            report.Updated += part.Updated;
            report.Orphaned += part.Orphaned;
            report.Errors += part.Errors;
        }

        return report;
    }

    public async Task<ScanReport> ScanSourceAsync(SourceFolder source, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var report = new ScanReport();
        if (!Directory.Exists(source.Path))
        {
            foreach (var existing in await _catalog.GetAssetsAsync(sourceId: source.Id))
            {
                await _catalog.MarkOrphanAsync(existing.Id, true);
                report.Orphaned++;
            }

            return report;
        }

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(source.Path, "*.*", SearchOption.AllDirectories)
                .Where(p => PathSafe.IsCatalogExt(Path.GetExtension(p)));
        }
        catch
        {
            report.Errors++;
            return report;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            found.Add(file);
            progress?.Report(file);
            try
            {
                var created = await IndexFileAsync(source, file, autoOrganize: source.AutoOrganizeNewFiles, ct)
                    .ConfigureAwait(false);
                if (created)
                {
                    report.Added++;
                }
                else
                {
                    report.Updated++;
                }
            }
            catch
            {
                report.Errors++;
            }
        }

        foreach (var known in await _catalog.GetIndexedPathsAsync(source.Id))
        {
            if (!found.Contains(known))
            {
                var asset = await _catalog.GetAssetByPathAsync(known);
                if (asset is not null)
                {
                    await _catalog.MarkOrphanAsync(asset.Id, true);
                    report.Orphaned++;
                }
            }
        }

        return report;
    }

    public async Task<bool> IndexFileAsync(SourceFolder source, string file, bool autoOrganize, CancellationToken ct = default)
    {
        var info = new FileInfo(file);
        if (!info.Exists)
        {
            return false;
        }

        var existing = await _catalog.GetAssetByPathAsync(file);
        var isNew = existing is null;
        var hash = await HashService.HashFileAsync(file, info.Length, ct).ConfigureAwait(false);
        var kind = PathSafe.KindFromExt(info.Extension);
        var meta = _metadata.Extract(file);
        var asset = existing ?? new Asset
        {
            Id = PalaceDb.NewId(),
            SourceFolderId = source.Id,
            DateAdded = PalaceDb.NowIso()
        };
        asset.SourceFolderId = source.Id;
        asset.Path = file;
        asset.FileName = info.Name;
        asset.ContentHash = hash;
        asset.Kind = kind;
        asset.IsOrphan = false;
        asset.Model = meta.Model;
        asset.Seed = meta.Seed;
        asset.Prompt = meta.Prompt;
        asset.NegativePrompt = meta.NegativePrompt;
        asset.MetadataJson = meta.RawJson;
        asset.DateModified = info.LastWriteTimeUtc.ToString("O");
        asset.FileSize = info.Length;

        var thumb = await _thumbs.EnsureThumbnailAsync(file, hash, kind);
        if (thumb is { Width: > 0, Height: > 0 } thumbSize)
        {
            asset.Width = thumbSize.Width;
            asset.Height = thumbSize.Height;
        }
        else if (ImageDimensions.TryRead(file) is { } size)
        {
            asset.Width = size.Width;
            asset.Height = size.Height;
        }

        await _catalog.UpsertAssetAsync(asset, "");

        if (isNew && autoOrganize)
        {
            await _organize.AutoOrganizeNewFileAsync(source, asset);
        }

        return isNew;
    }
}
