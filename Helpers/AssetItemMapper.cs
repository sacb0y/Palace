using Palace.Models;
using Palace.Services;

namespace Palace.Helpers;

/// <summary>
/// Maps catalog <see cref="Asset"/> rows onto mosaic <see cref="AssetItem"/>s.
/// Library <c>ToItem</c> can call <see cref="ApplyCloud"/> or replace its body with <see cref="FromAsset"/>.
/// Does not probe the original file (no <c>ImageDimensions.TryRead</c> on <see cref="Asset.Path"/>).
/// </summary>
public static class AssetItemMapper
{
    public static AssetItem FromAsset(Asset asset, ThumbnailService thumbs)
    {
        var item = new AssetItem();
        Apply(item, asset, thumbs);
        return item;
    }

    public static void Apply(AssetItem item, Asset asset, ThumbnailService thumbs)
    {
        item.Id = asset.Id;
        item.SourceFolderId = asset.SourceFolderId;
        item.FileName = asset.FileName;
        item.Path = asset.Path;
        item.ContentHash = asset.ContentHash;
        item.ThumbPath = thumbs.ExistingPathForHash(asset.ContentHash);
        item.Kind = asset.Kind;
        item.IsOrphan = asset.IsOrphan;
        item.Model = asset.Model;
        item.Prompt = asset.Prompt;
        item.OrganizeError = asset.OrganizeError;
        item.Width = asset.Width;
        item.Height = asset.Height;
        ApplyCloud(item, asset);
    }

    public static void ApplyCloud(AssetItem item, Asset asset)
    {
        item.IsOnlineOnly = asset.IsOnlineOnly;
        item.CloudItemId = asset.CloudItemId;
    }

    public static bool IsApiOnly(Asset asset) =>
        !string.IsNullOrEmpty(asset.CloudItemId) && !CloudFile.Exists(asset.Path);

    public static bool IsApiOnly(AssetItem item) =>
        !string.IsNullOrEmpty(item.CloudItemId) && !CloudFile.Exists(item.Path);
}
