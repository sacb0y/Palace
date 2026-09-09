using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>
/// After an explicit Open of a local Files On-Demand placeholder, re-hash,
/// extract metadata, and replace the stub thumbnail. Never called from scan.
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
        if (asset is null || string.IsNullOrEmpty(asset.Path) || !File.Exists(asset.Path))
        {
            return asset;
        }

        if (!await WaitUntilLocalAsync(asset.Path, ct).ConfigureAwait(false))
        {
            return asset;
        }

        var info = new FileInfo(asset.Path);
        var previousHash = asset.ContentHash;
        var hash = await HashService.HashFileAsync(asset.Path, info.Length, ct).ConfigureAwait(false);
        var meta = _metadata.Extract(asset.Path);
        asset.ContentHash = hash;
        asset.IsOnlineOnly = false;
        asset.Kind = PathSafe.KindFromExt(info.Extension);
        asset.Model = meta.Model;
        asset.Seed = meta.Seed;
        asset.Prompt = meta.Prompt;
        asset.NegativePrompt = meta.NegativePrompt;
        asset.MetadataJson = meta.RawJson;
        asset.DateModified = info.LastWriteTimeUtc.ToString("O");
        asset.FileSize = info.Length;

        var thumb = await _thumbs.EnsureThumbnailAsync(asset.Path, hash, asset.Kind).ConfigureAwait(false);
        if (thumb is { Width: > 0, Height: > 0 } sized)
        {
            asset.Width = sized.Width;
            asset.Height = sized.Height;
        }
        else if (ImageDimensions.TryRead(asset.Path) is { } size)
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

    private static async Task<bool> WaitUntilLocalAsync(string path, CancellationToken ct)
    {
        for (var i = 0; i < 40; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (!CloudFile.IsOnlineOnly(path))
            {
                return File.Exists(path);
            }

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return !CloudFile.IsOnlineOnly(path);
    }
}
