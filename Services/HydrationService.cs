using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>
/// After an explicit Open of a local Files On-Demand placeholder, re-hash,
/// extract metadata, and replace the stub thumbnail. Never called from scan.
/// Large files are prefix-hashed; this type fully reads the original first
/// so recall flags can clear before Extract and catalog update.
/// </summary>
public sealed class HydrationService
{
    private readonly CatalogService _catalog;
    private readonly MetadataExtractorService _metadata;
    private readonly ThumbnailService _thumbs;

    public HydrationService(
        CatalogService catalog,
        MetadataExtractorService metadata,
        ThumbnailService thumbs)
    {
        _catalog = catalog;
        _metadata = metadata;
        _thumbs = thumbs;
    }

    public async Task<Asset?> HydrateAfterOpenAsync(string assetId, CancellationToken ct = default)
    {
        var asset = await _catalog.GetAssetByIdAsync(assetId).ConfigureAwait(false);
        if (asset is null || string.IsNullOrEmpty(asset.Path) || !CloudFile.Exists(asset.Path))
        {
            return asset;
        }

        var info = new FileInfo(asset.Path);
        if (HashService.NeedsFullRecall(CloudFile.IsOnlineOnly(asset.Path), info.Length))
        {
            await HashService.RecallFullyAsync(asset.Path, ct).ConfigureAwait(false);
            info.Refresh();
        }

        var previousHash = asset.ContentHash;
        var hash = await HashService.HashFileAsync(asset.Path, info.Length, ct).ConfigureAwait(false);
        var stillOnline = CloudFile.IsOnlineOnly(asset.Path);
        asset.ContentHash = hash;
        asset.IsOnlineOnly = stillOnline;
        asset.Kind = PathSafe.KindFromExt(info.Extension);
        asset.DateModified = info.LastWriteTimeUtc.ToString("O");
        asset.FileSize = info.Length;

        if (HashService.ApplyExtractedMetadata(stillOnline))
        {
            var meta = _metadata.Extract(asset.Path);
            asset.Model = meta.Model;
            asset.Seed = meta.Seed;
            asset.Prompt = meta.Prompt;
            asset.NegativePrompt = meta.NegativePrompt;
            asset.MetadataJson = meta.RawJson;
        }

        var thumb = await _thumbs.EnsureThumbnailAsync(asset.Path, hash, asset.Kind).ConfigureAwait(false);
        if (thumb is { Width: > 0, Height: > 0 } sized)
        {
            asset.Width = sized.Width;
            asset.Height = sized.Height;
        }
        else if (!stillOnline && ImageDimensions.TryRead(asset.Path) is { } size)
        {
            asset.Width = size.Width;
            asset.Height = size.Height;
        }

        await _catalog.UpsertAssetAsync(asset, "").ConfigureAwait(false);
        if (!string.Equals(previousHash, hash, StringComparison.OrdinalIgnoreCase))
        {
            _thumbs.TryDelete(previousHash);
        }

        return asset;
    }
}
