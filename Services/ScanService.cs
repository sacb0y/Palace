using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services.Cloud;

namespace Palace.Services;

public sealed class ScanService
{
    private readonly CatalogService _catalog;
    private readonly MetadataExtractorService _metadata;
    private readonly ThumbnailService _thumbs;
    private readonly OrganizeService _organize;
    private readonly ICloudLibraryFactory _cloudLibraries;

    public ScanService(
        CatalogService catalog,
        MetadataExtractorService metadata,
        ThumbnailService thumbs,
        OrganizeService organize,
        ICloudLibraryFactory cloudLibraries)
    {
        _catalog = catalog;
        _metadata = metadata;
        _thumbs = thumbs;
        _organize = organize;
        _cloudLibraries = cloudLibraries;
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
        if (source.Kind != SourceKind.Local)
        {
            return await ScanCloudSourceAsync(source, progress, ct).ConfigureAwait(false);
        }

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
        if (!CloudFile.TryGetAttributes(file, out var attrs))
        {
            return false;
        }

        var info = new FileInfo(file);
        var existing = await _catalog.GetAssetByPathAsync(file);
        var isNew = existing is null;
        var onlineOnly = CloudFile.IsOnlineOnly(attrs);
        var kind = PathSafe.KindFromExt(info.Extension);
        var asset = existing ?? new Asset
        {
            Id = PalaceDb.NewId(),
            SourceFolderId = source.Id,
            DateAdded = PalaceDb.NowIso()
        };
        asset.SourceFolderId = source.Id;
        asset.Path = file;
        asset.FileName = info.Name;
        asset.Kind = kind;
        asset.IsOrphan = false;
        asset.DateModified = info.LastWriteTimeUtc.ToString("O");
        asset.FileSize = info.Length;
        asset.IsOnlineOnly = onlineOnly;
        asset.IsHdr = GalleryMedia.CatalogHdrFromHeader(
            file,
            ScanContent.MayReadOriginal(attrs),
            existing?.IsHdr == true);

        if (!ScanContent.MayReadOriginal(attrs))
        {
            asset.ContentHash = KeepOrStubHash(existing, file, info.Length, info.LastWriteTimeUtc);
            // No HashFileAsync, Extract, BitmapDecoder, ImageDimensions, or
            // GetThumbnailAsync — Dropbox/OneDrive provider thumbs can still
            // recall the original. Mosaic shows ShowCloudTile until Open.
        }
        else
        {
            var hash = await HashService.HashFileAsync(file, info.Length, ct).ConfigureAwait(false);
            var meta = _metadata.Extract(file);
            asset.ContentHash = hash;
            asset.Model = meta.Model;
            asset.Seed = meta.Seed;
            asset.Prompt = meta.Prompt;
            asset.NegativePrompt = meta.NegativePrompt;
            asset.MetadataJson = meta.RawJson;
            if (ScanContent.MayGenerateScanThumbnail(attrs))
            {
                var thumb = await _thumbs.EnsureThumbnailAsync(file, hash, kind).ConfigureAwait(false);
                if (thumb is { Width: > 0, Height: > 0 } localThumb)
                {
                    asset.Width = localThumb.Width;
                    asset.Height = localThumb.Height;
                }
                else if (ImageDimensions.TryRead(file) is { } size)
                {
                    asset.Width = size.Width;
                    asset.Height = size.Height;
                }
            }
        }

        await _catalog.UpsertAssetAsync(asset, "");

        if (isNew && autoOrganize && !onlineOnly)
        {
            await _organize.AutoOrganizeNewFileAsync(source, asset);
        }

        return isNew;
    }

    private async Task<ScanReport> ScanCloudSourceAsync(
        SourceFolder source,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var report = new ScanReport();
        var library = await _cloudLibraries.ForSourceAsync(source, ct).ConfigureAwait(false);
        if (library is null)
        {
            report.Errors++;
            return report;
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            await WalkCloudAsync(source, library, source.CloudRootItemId, source.Path, found, report, progress, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            report.Errors++;
            return report;
        }

        foreach (var known in await _catalog.GetIndexedCloudItemIdsAsync(source.Id))
        {
            if (!found.Contains(known))
            {
                var asset = await _catalog.GetAssetByCloudItemIdAsync(source.Id, known);
                if (asset is not null)
                {
                    await _catalog.MarkOrphanAsync(asset.Id, true);
                    report.Orphaned++;
                }
            }
        }

        return report;
    }

    private async Task WalkCloudAsync(
        SourceFolder source,
        ICloudLibrary library,
        string? parentId,
        string displayPrefix,
        HashSet<string> found,
        ScanReport report,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        IReadOnlyList<CloudEntry> children;
        try
        {
            children = await library.ListChildrenAsync(parentId, ct).ConfigureAwait(false);
        }
        catch
        {
            report.Errors++;
            return;
        }

        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            var display = Path.Combine(displayPrefix, child.Name);
            if (child.IsFolder)
            {
                await WalkCloudAsync(source, library, child.Id, display, found, report, progress, ct)
                    .ConfigureAwait(false);
                continue;
            }

            if (!PathSafe.IsCatalogExt(Path.GetExtension(child.Name)))
            {
                continue;
            }

            found.Add(child.Id);
            progress?.Report(display);
            try
            {
                var created = await IndexCloudItemAsync(source, library, child, display, ct).ConfigureAwait(false);
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
    }

    private async Task<bool> IndexCloudItemAsync(
        SourceFolder source,
        ICloudLibrary library,
        CloudEntry entry,
        string displayPath,
        CancellationToken ct)
    {
        var existing = await _catalog.GetAssetByCloudItemIdAsync(source.Id, entry.Id)
            ?? await _catalog.GetAssetByPathAsync(displayPath);
        var isNew = existing is null;
        var modified = entry.ModifiedUtc ?? DateTimeOffset.UtcNow;
        var size = entry.Size ?? 0;
        var asset = existing ?? new Asset
        {
            Id = PalaceDb.NewId(),
            SourceFolderId = source.Id,
            DateAdded = PalaceDb.NowIso()
        };
        asset.SourceFolderId = source.Id;
        asset.Path = displayPath;
        asset.FileName = entry.Name;
        asset.Kind = PathSafe.KindFromExt(Path.GetExtension(entry.Name));
        asset.IsOrphan = false;
        asset.IsOnlineOnly = true;
        asset.CloudItemId = entry.Id;
        asset.DateModified = modified.ToString("O");
        asset.FileSize = entry.Size;
        asset.ContentHash = KeepOrStubHash(existing, displayPath, size, modified);
        // Display path only — never download / Open the original to decide HDR.
        // Keep a prior probe (hydrate / previous local scan) so API rescan
        // does not clear the mosaic HDR badge.
        asset.IsHdr = GalleryMedia.CatalogHdrFromHeader(
            displayPath,
            mayReadOriginalHeader: false,
            existing?.IsHdr == true);

        if (_thumbs.ExistingPathForHash(asset.ContentHash) is null)
        {
            try
            {
                await using var stream = await library.OpenThumbnailAsync(entry.Id, ct).ConfigureAwait(false);
                if (stream is not null)
                {
                    var thumb = await _thumbs.CacheJpegAsync(asset.ContentHash, stream).ConfigureAwait(false);
                    if (thumb is { Width: > 0, Height: > 0 } cloudThumb && asset.Width is not > 0)
                    {
                        asset.Width = cloudThumb.Width;
                        asset.Height = cloudThumb.Height;
                    }
                }
            }
            catch
            {
                // Provider thumb is optional; mosaic shows a cloud tile.
            }
        }

        await _catalog.UpsertAssetAsync(asset, "");
        return isNew;
    }

    private static string KeepOrStubHash(Asset? existing, string path, long size, DateTimeOffset modifiedUtc)
    {
        if (existing?.ContentHash is { Length: > 0 } hash && !HashService.IsCloudStub(hash))
        {
            return hash;
        }

        if (HashService.IsCloudStub(existing?.ContentHash))
        {
            return existing!.ContentHash!;
        }

        return HashService.CloudStubHash(path, size, modifiedUtc);
    }
}
