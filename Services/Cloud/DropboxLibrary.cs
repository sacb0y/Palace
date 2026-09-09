using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Palace.Models;

namespace Palace.Services.Cloud;

public sealed class DropboxLibrary : ICloudLibrary
{
    private readonly CloudAccountService _accounts;
    private readonly CloudAccount _account;

    public DropboxLibrary(CloudAccountService accounts, CloudAccount account)
    {
        _accounts = accounts;
        _account = account;
    }

    public CloudProvider Provider => CloudProvider.Dropbox;

    public async Task<IReadOnlyList<CloudEntry>> ListChildrenAsync(string? parentItemId, CancellationToken ct = default)
    {
        var token = await _accounts.GetValidAccessTokenAsync(_account, ct).ConfigureAwait(false);
        var path = string.IsNullOrEmpty(parentItemId) ? "" : parentItemId;
        var items = new List<CloudEntry>();
        using var first = await CloudOAuth.PostJsonAsync(
            "https://api.dropboxapi.com/2/files/list_folder",
            token,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["path"] = path,
                ["recursive"] = false,
                ["include_non_downloadable_files"] = false
            }),
            ct).ConfigureAwait(false);
        AddEntries(first.RootElement, items);
        var cursor = first.RootElement.TryGetProperty("cursor", out var cursorEl) ? cursorEl.GetString() : null;
        var hasMore = first.RootElement.TryGetProperty("has_more", out var moreEl) && moreEl.GetBoolean();
        while (hasMore && !string.IsNullOrEmpty(cursor))
        {
            using var next = await CloudOAuth.PostJsonAsync(
                "https://api.dropboxapi.com/2/files/list_folder/continue",
                token,
                JsonSerializer.Serialize(new Dictionary<string, string> { ["cursor"] = cursor }),
                ct).ConfigureAwait(false);
            AddEntries(next.RootElement, items);
            cursor = next.RootElement.TryGetProperty("cursor", out var nextCursor) ? nextCursor.GetString() : null;
            hasMore = next.RootElement.TryGetProperty("has_more", out var nextMore) && nextMore.GetBoolean();
        }

        return items;
    }

    public Task<Stream?> OpenThumbnailAsync(string itemId, CancellationToken ct = default) =>
        OpenThumbnailSizeAsync(itemId, "w256h256", ct);

    public async Task<string?> GetLargePreviewUrlAsync(string itemId, CancellationToken ct = default)
    {
        // Dropbox does not expose a durable preview URL; callers should use OpenLargePreviewAsync.
        await Task.CompletedTask;
        return null;
    }

    public Task<Stream?> OpenLargePreviewAsync(string itemId, CancellationToken ct = default) =>
        OpenThumbnailSizeAsync(itemId, "w1024h768", ct);

    public async Task<string?> GetOriginalDownloadUrlAsync(string itemId, CancellationToken ct = default)
    {
        var token = await _accounts.GetValidAccessTokenAsync(_account, ct).ConfigureAwait(false);
        using var doc = await CloudOAuth.PostJsonAsync(
            "https://api.dropboxapi.com/2/files/get_temporary_link",
            token,
            JsonSerializer.Serialize(new Dictionary<string, string> { ["path"] = itemId }),
            ct).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("link", out var link) ? link.GetString() : null;
    }

    private async Task<Stream?> OpenThumbnailSizeAsync(string itemId, string size, CancellationToken ct)
    {
        var token = await _accounts.GetValidAccessTokenAsync(_account, ct).ConfigureAwait(false);
        var isId = itemId.StartsWith("id:", StringComparison.Ordinal);
        var resource = new Dictionary<string, string> { [".tag"] = isId ? "id" : "path" };
        resource[isId ? "id" : "path"] = itemId;
        var arg = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["resource"] = resource,
            ["format"] = "jpeg",
            ["size"] = size,
            ["mode"] = "fitone_bestfit"
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://content.dropboxapi.com/2/files/get_thumbnail_v2");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Dropbox-API-Arg", arg);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/octet-stream");
        var response = await SharedHttp.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return new HttpResponseStream(response, stream);
    }

    private static void AddEntries(JsonElement root, List<CloudEntry> items)
    {
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var row in entries.EnumerateArray())
        {
            var tag = row.TryGetProperty(".tag", out var tagEl) ? tagEl.GetString() : "";
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
                DisplayPath = row.TryGetProperty("path_display", out var pathEl) ? pathEl.GetString() ?? name : name,
                IsFolder = string.Equals(tag, "folder", StringComparison.OrdinalIgnoreCase),
                Size = row.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var size) ? size : null,
                ModifiedUtc = row.TryGetProperty("client_modified", out var modEl)
                    && DateTimeOffset.TryParse(modEl.GetString(), out var mod)
                    ? mod
                    : null
            });
        }
    }
}
