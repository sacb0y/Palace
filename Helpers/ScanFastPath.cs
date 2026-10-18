using Palace.Models;
using Palace.Services;

namespace Palace.Helpers;

/// <summary>
/// Rescan fast path: a local file whose size and write time match its catalog row (and that was
/// indexed by the current <see cref="IndexVersion"/>) is not re-hashed, re-parsed or re-written.
/// Re-hashing every unchanged file was the bulk of a rescan (the whole library is read back).
/// Bump <see cref="IndexVersion"/> whenever scan-time extraction changes (metadata fields, HDR
/// probe, kind mapping) so every file is indexed once more.
/// </summary>
public static class ScanFastPath
{
    public const int IndexVersion = 1;

    /// <summary>Same format the scan stores in <see cref="Asset.DateModified"/>.</summary>
    public static string Stamp(DateTime lastWriteUtc) =>
        DateTime.SpecifyKind(lastWriteUtc, DateTimeKind.Utc).ToString("O");

    public static bool IsUnchanged(Asset? existing, long length, DateTime lastWriteUtc, bool onlineOnly) =>
        existing is not null
        && !onlineOnly
        && !existing.IsOnlineOnly
        && !existing.IsOrphan
        && existing.IndexVersion == IndexVersion
        && existing.FileSize == length
        && string.Equals(existing.DateModified, Stamp(lastWriteUtc), StringComparison.Ordinal)
        && existing.ContentHash is { Length: > 0 } hash
        && !HashService.IsCloudStub(hash)
        && existing.Width is > 0
        && existing.Height is > 0;

    /// <summary>Throttle for per-file scan progress (the UI line), so large scans don't flood the dispatcher.</summary>
    public const int ProgressIntervalMs = 150;

    public static bool ShouldReport(ref long lastReportTicks, long nowTicks)
    {
        if (lastReportTicks != 0 && nowTicks - lastReportTicks < ProgressIntervalMs)
        {
            return false;
        }

        lastReportTicks = nowTicks;
        return true;
    }
}
