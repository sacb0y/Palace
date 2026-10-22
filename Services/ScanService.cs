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
            report.Add(part);
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
        await IndexTreeAsync(source, source.Path, report, found, progress, ct).ConfigureAwait(false);
        if (report.InaccessibleFolders > 0)
        {
            // Files under a folder we could not list are not missing; do not orphan them.
            return report;
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

    /// <summary>
    /// Index catalog files under one directory (watcher Created/Renamed folder).
    /// Does not walk the whole source or orphan-check siblings — multi-delete
    /// parent LastWrite must never call <see cref="ScanSourceAsync"/>.
    /// </summary>
    public async Task<ScanReport> ScanDirectoryAsync(
        SourceFolder source,
        string directory,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var report = new ScanReport();
        if (source.Kind != SourceKind.Local || !Directory.Exists(directory))
        {
            return report;
        }

        await IndexTreeAsync(source, directory, report, found: null, progress, ct).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Index every catalog file under <paramref name="root"/>. Unsupported formats, files that
    /// fail or cannot be decoded, and folders that cannot be opened are recorded on
    /// <paramref name="report"/> for <see cref="ScanNotice"/> instead of disappearing silently.
    /// </summary>
    private async Task IndexTreeAsync(
        SourceFolder source,
        string root,
        ScanReport report,
        HashSet<string>? found,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var inaccessible = 0;
        long lastProgress = 0;
        foreach (var entry in ScanWalk.Files(root, n => inaccessible = n))
        {
            ct.ThrowIfCancellationRequested();
            var file = entry.Path;
            if (!LibrarySettings.IsIndexedExt(Path.GetExtension(file)))
            {
                if (!ScanNotice.IsIgnorable(file, entry.Attributes))
                {
                    report.NoteSkipped(file);
                }

                continue;
            }

            found?.Add(file);
            if (progress is not null && ScanFastPath.ShouldReport(ref lastProgress, Environment.TickCount64))
            {
                progress.Report(file);
            }
            try
            {
                var (outcome, decoded) = await IndexFileCoreAsync(source, file, autoOrganize: source.AutoOrganizeNewFiles, ct)
                    .ConfigureAwait(false);
                report.NoteIndexed(outcome);

                if (!decoded)
                {
                    report.NoteUndecodable(file);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                report.NoteError(file, ex);
            }
        }

        report.NoteInaccessibleFolders(inaccessible);
    }

    public async Task<bool> IndexFileAsync(SourceFolder source, string file, bool autoOrganize, CancellationToken ct = default) =>
        (await IndexFileCoreAsync(source, file, autoOrganize, ct).ConfigureAwait(false)).Outcome == ScanOutcome.Added;

    /// <summary>
    /// Outcome: added, re-indexed or left unchanged. Decoded: false when a local thumbnail could
    /// not be made from the content (remembered on the row, so unchanged files report it too).
    /// </summary>
    private async Task<(ScanOutcome Outcome, bool Decoded)> IndexFileCoreAsync(
        SourceFolder source,
        string file,
        bool autoOrganize,
        CancellationToken ct)
    {
        if (!CloudFile.TryGetAttributes(file, out var attrs))
        {
            // Gone between the walk and now: nothing indexed (counted as unchanged).
            return (ScanOutcome.Unchanged, true);
        }

        var decoded = true;

        var info = new FileInfo(file);
        var existing = await _catalog.GetAssetByPathAsync(file);
        var isNew = existing is null;
        var onlineOnly = CloudFile.IsOnlineOnly(attrs);
        var kind = PathSafe.KindFromExt(info.Extension);
        if (ScanFastPath.IsUnchanged(existing, info.Length, info.LastWriteTimeUtc, onlineOnly)
            && existing!.Kind == kind
            && (!LibrarySettings.GenerateThumbsOnScan
                || !ScanContent.MayGenerateScanThumbnail(attrs)
                || !ScanFastPath.NeedsCurrentThumb(existing)
                || _thumbs.IsCachedThumbCurrent(file, existing.ContentHash!, kind)))
        {
            // Unchanged since the last full index: skip hash / metadata / thumb / write.
            return (ScanOutcome.Unchanged, !existing!.Undecodable);
        }

        if (ScanFastPath.NeedsDateTakenOnly(existing, info.Length, info.LastWriteTimeUtc, onlineOnly)
            && existing!.Kind == kind
            && ScanContent.MayReadOriginal(attrs)
            && (!LibrarySettings.GenerateThumbsOnScan
                || !ScanContent.MayGenerateScanThumbnail(attrs)
                || !ScanFastPath.NeedsCurrentThumb(existing)
                || _thumbs.IsCachedThumbCurrent(file, existing.ContentHash!, kind)))
        {
            // Indexed by version 2, before Date taken: read only the EXIF date (no hash / thumb).
            // Same thumb rule as the fast path: an undecodable row needs no cached thumb.
            var taken = _metadata.ReadDateTaken(file);
            await _catalog.SetDateTakenAsync(existing.Id, taken, ScanFastPath.IndexVersion);
            return (ScanOutcome.Unchanged, !existing.Undecodable);
        }

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
        asset.DateModified = ScanFastPath.Stamp(info.LastWriteTimeUtc);
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
            asset.IndexVersion = ScanFastPath.IndexVersion;
            asset.Model = meta.Model;
            asset.Seed = meta.Seed;
            asset.Prompt = meta.Prompt;
            asset.NegativePrompt = meta.NegativePrompt;
            asset.MetadataJson = meta.RawJson;
            asset.DateTaken = meta.DateTaken;
            if (LibrarySettings.GenerateThumbsOnScan
                && ScanContent.MayGenerateScanThumbnail(attrs))
            {
                var thumb = await _thumbs.EnsureThumbnailAsync(
                    file, hash, kind, requireKnownStatus: true, cancellationToken: ct).ConfigureAwait(false);
                if (thumb is { Width: > 0, Height: > 0 } localThumb)
                {
                    asset.Width = localThumb.Width;
                    asset.Height = localThumb.Height;
                    decoded = !localThumb.IsPlaceholder;
                }
                else if (ImageDimensions.TryRead(file) is { } size)
                {
                    asset.Width = size.Width;
                    asset.Height = size.Height;
                    decoded = thumb is not { IsPlaceholder: true };
                }
                else
                {
                    decoded = false;
                }
            }
        }

        asset.Undecodable = !decoded;
        await _catalog.UpsertAssetAsync(asset, "");

        if (isNew && autoOrganize && !onlineOnly)
        {
            await _organize.AutoOrganizeNewFileAsync(source, asset);
        }

        return (isNew ? ScanOutcome.Added : ScanOutcome.Updated, decoded);
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
            // Account signed out / provider unavailable: the source itself can't be listed.
            report.NoteInaccessibleFolders(1);
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
            report.NoteInaccessibleFolders(1);
            return report;
        }

        if (report.InaccessibleFolders > 0)
        {
            // Items under a folder we could not list are not missing; do not orphan them.
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
            report.NoteInaccessibleFolders(1);
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

            if (!LibrarySettings.IsIndexedExt(Path.GetExtension(child.Name)))
            {
                if (!ScanNotice.IsIgnorable(child.Name, 0))
                {
                    report.NoteSkipped(child.Name);
                }

                continue;
            }

            found.Add(child.Id);
            progress?.Report(display);
            try
            {
                report.NoteIndexed(await IndexCloudItemAsync(source, library, child, display, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                report.NoteError(display, ex);
            }
        }
    }

    private async Task<ScanOutcome> IndexCloudItemAsync(
        SourceFolder source,
        ICloudLibrary library,
        CloudEntry entry,
        string displayPath,
        CancellationToken ct)
    {
        var existing = await _catalog.GetAssetByCloudItemIdAsync(source.Id, entry.Id)
            ?? await _catalog.GetAssetByPathAsync(displayPath);
        var isNew = existing is null;
        var unchanged = ScanFastPath.CloudUnchanged(existing, displayPath, entry.Size, entry.ModifiedUtc);
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
        return isNew ? ScanOutcome.Added : unchanged ? ScanOutcome.Unchanged : ScanOutcome.Updated;
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
