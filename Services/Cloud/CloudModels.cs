using Palace.Models;

namespace Palace.Services.Cloud;

public sealed class CloudEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayPath { get; set; } = "";
    public bool IsFolder { get; set; }
    public long? Size { get; set; }
    public DateTimeOffset? ModifiedUtc { get; set; }
}

public sealed class CloudAuthTokens
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class CloudConnectResult
{
    public CloudAccount Account { get; init; } = null!;
    public string DisplayName { get; init; } = "";
}

public interface ICloudLibrary
{
    CloudProvider Provider { get; }

    Task<IReadOnlyList<CloudEntry>> ListChildrenAsync(string? parentItemId, CancellationToken ct = default);

    Task<Stream?> OpenThumbnailAsync(string itemId, CancellationToken ct = default);

    Task<string?> GetLargePreviewUrlAsync(string itemId, CancellationToken ct = default);

    Task<Stream?> OpenLargePreviewAsync(string itemId, CancellationToken ct = default);

    Task<string?> GetOriginalDownloadUrlAsync(string itemId, CancellationToken ct = default);
}

public interface ICloudLibraryFactory
{
    Task<ICloudLibrary?> ForSourceAsync(SourceFolder source, CancellationToken ct = default);

    Task<ICloudLibrary?> ForAccountAsync(CloudAccount account, CancellationToken ct = default);
}

public sealed class CloudAuthException : Exception
{
    public CloudAuthException(string message) : base(message)
    {
    }
}
