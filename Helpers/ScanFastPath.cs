using Palace.Models;
using Palace.Services;

namespace Palace.Helpers;

/// <summary>
/// Rescan fast path: a local file whose size and write time match its catalog row (and that was
/// indexed by the current <see cref="IndexVersion"/>) is not re-hashed, re-parsed or re-written.
/// Re-hashing every unchanged file was the bulk of a rescan (the whole library is read back).
/// Bump <see cref="IndexVersion"/> whenever scan-time extraction changes (metadata fields, HDR
/// probe, kind mapping) so every file is indexed once more.
/// Version 2 records <see cref="Asset.Undecodable"/>, so a rescan that skips an unchanged
/// placeholder file still lists it in the scan notice.
/// </summary>
public static class ScanFastPath
{
    public const int IndexVersion = 2;

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

    /// <summary>
    /// Cloud API rescan: the item's size and modified time match its row, so the rescan only
    /// re-confirms it (counted unchanged, not updated).
    /// </summary>
    public static bool CloudUnchanged(Asset? existing, string displayPath, long? size, DateTimeOffset? modifiedUtc) =>
        existing is not null
        && !existing.IsOrphan
        && modifiedUtc is { } modified
        && string.Equals(existing.Path, displayPath, StringComparison.Ordinal)
        && existing.FileSize == (size ?? 0)
        && string.Equals(existing.DateModified, modified.ToString("O"), StringComparison.Ordinal);

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
