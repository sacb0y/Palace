using System.Text.Json;
using Palace.Models;

namespace Palace.Services.Cloud;

public sealed class OneDriveLibrary : ICloudLibrary
{
    private readonly CloudAccountService _accounts;
    private readonly CloudAccount _account;

    public OneDriveLibrary(CloudAccountService accounts, CloudAccount account)
    {
        _accounts = accounts;
        _account = account;
    }

    public CloudProvider Provider => CloudProvider.OneDrive;

    public async Task<IReadOnlyList<CloudEntry>> ListChildrenAsync(string? parentItemId, CancellationToken ct = default)
    {
        var token = await _accounts.GetValidAccessTokenAsync(_account, ct).ConfigureAwait(false);
        var url = string.IsNullOrEmpty(parentItemId)
            ? "https://graph.microsoft.com/v1.0/me/drive/root/children?$select=id,name,file,folder,size,lastModifiedDateTime&$top=200"
            : "https://graph.microsoft.com/v1.0/me/drive/items/" + Uri.EscapeDataString(parentItemId)
              + "/children?$select=id,name,file,folder,size,lastModifiedDateTime&$top=200";
        var items = new List<CloudEntry>();
        while (!string.IsNullOrEmpty(url))
        {
            using var doc = await CloudOAuth.GetJsonAsync(url, token, ct).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in value.EnumerateArray())
                {
                    var id = row.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    var name = row.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    items.Add(new CloudEntry
                    {
                        Id = id,
                        Name = name,
                        DisplayPath = name,
                        IsFolder = row.TryGetProperty("folder", out _),
                        Size = row.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var size) ? size : null,
                        ModifiedUtc = row.TryGetProperty("lastModifiedDateTime", out var modEl)
                            && DateTimeOffset.TryParse(modEl.GetString(), out var mod)
                            ? mod
                            : null
                    });
                }
            }

            url = doc.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
        }

        return items;
    }

    public async Task<Stream?> OpenThumbnailAsync(string itemId, CancellationToken ct = default)
    {
        var url = await ThumbnailUrlAsync(itemId, "medium", ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        return await CloudOAuth.GetStreamAsync(url, null, ct).ConfigureAwait(false);
    }

    public Task<string?> GetLargePreviewUrlAsync(string itemId, CancellationToken ct = default) =>
        ThumbnailUrlAsync(itemId, "large", ct);

    public async Task<Stream?> OpenLargePreviewAsync(string itemId, CancellationToken ct = default)
    {
        var token = await _accounts.GetValidAccessTokenAsync(_account, ct).ConfigureAwait(false);
        var sized = await CloudOAuth.GetStreamAsync(
            "https://graph.microsoft.com/v1.0/me/drive/items/" + Uri.EscapeDataString(itemId) + "/thumbnails/0/c1600x1600/content",
            token,
            ct).ConfigureAwait(false);
        if (sized is not null)
        {
            return sized;
        }

        var url = await GetLargePreviewUrlAsync(itemId, ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(url) ? null : await CloudOAuth.GetStreamAsync(url, null, ct).ConfigureAwait(false);
    }

    public async Task<string?> GetOriginalDownloadUrlAsync(string itemId, CancellationToken ct = default)
    {
        var token = await _accounts.GetValidAccessTokenAsync(_account, ct).ConfigureAwait(false);
        using var doc = await CloudOAuth.GetJsonAsync(
            "https://graph.microsoft.com/v1.0/me/drive/items/" + Uri.EscapeDataString(itemId) + "?$select=id,@microsoft.graph.downloadUrl",
            token,
            ct).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("@microsoft.graph.downloadUrl", out var url)
            ? url.GetString()
            : null;
    }

    private async Task<string?> ThumbnailUrlAsync(string itemId, string size, CancellationToken ct)
    {
        var token = await _accounts.GetValidAccessTokenAsync(_account, ct).ConfigureAwait(false);
        using var doc = await CloudOAuth.GetJsonAsync(
            "https://graph.microsoft.com/v1.0/me/drive/items/" + Uri.EscapeDataString(itemId) + "/thumbnails",
            token,
            ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("value", out var value) || value.GetArrayLength() == 0)
        {
            return null;
        }

        var first = value[0];
        if (first.TryGetProperty(size, out var sized) && sized.TryGetProperty("url", out var url))
        {
            return url.GetString();
        }

        if (first.TryGetProperty("medium", out var medium) && medium.TryGetProperty("url", out var mediumUrl))
        {
            return mediumUrl.GetString();
        }

        return null;
    }
}
