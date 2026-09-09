using System.Security.Cryptography;

namespace Palace.Services;

public static class HashService
{
    public static async Task<string> HashFileAsync(string path, long fileSize, CancellationToken ct = default)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sha = SHA256.Create();
        const long large = 32L * 1024 * 1024;
        if (fileSize > large)
        {
            var buffer = new byte[8 * 1024 * 1024];
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
