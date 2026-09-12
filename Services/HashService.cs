using System.Security.Cryptography;
using System.Text;

namespace Palace.Services;

public static class HashService
{
    public const string CloudStubPrefix = "cloud:";

    public static bool IsCloudStub(string? hash) =>
        hash is not null && hash.StartsWith(CloudStubPrefix, StringComparison.Ordinal);

    public static string CloudStubHash(string path, long size, DateTimeOffset modifiedUtc)
    {
        var payload = string.Concat(path, "\n", size.ToString(), "\n", modifiedUtc.UtcTicks.ToString());
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return CloudStubPrefix + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public const long PrefixHashThresholdBytes = 32L * 1024 * 1024;
    public const int PrefixHashBytes = 8 * 1024 * 1024;

    public static bool UsesPrefixHash(long fileSize) => fileSize > PrefixHashThresholdBytes;

    /// <summary>
    /// Prefix-hash of a large file does not read the tail, so On-Demand recall
    /// flags can remain. Explicit Open must drain the stream to finish recall.
    /// Scan must not call this.
    /// </summary>
    public static bool NeedsFullRecall(bool isOnlineOnly, long fileSize) =>
        isOnlineOnly && UsesPrefixHash(fileSize);

    /// <summary>Keep Prompt/Model when Extract was skipped or the file is still a placeholder.</summary>
    public static bool ApplyExtractedMetadata(bool stillOnlineOnly) => !stillOnlineOnly;

    /// <summary>
    /// Reads the original to EOF so Files On-Demand recall can finish.
    /// Only for explicit Open hydration — never mosaic/scan/thumbs.
    /// </summary>
    public static async Task RecallFullyAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[PrefixHashBytes];
        while (await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false) > 0)
        {
        }
    }

    public static async Task<string> HashFileAsync(string path, long fileSize, CancellationToken ct = default)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sha = SHA256.Create();
        if (UsesPrefixHash(fileSize))
        {
            var buffer = new byte[PrefixHashBytes];
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            sha.TransformBlock(buffer, 0, read, null, 0);
            var len = BitConverter.GetBytes(fileSize);
            sha.TransformFinalBlock(len, 0, len.Length);
            return Convert.ToHexString(sha.Hash ?? []).ToLowerInvariant();
        }

        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
