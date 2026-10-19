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
/// placeholder file still lists it in the scan notice. An unchanged undecodable row skips the
/// size and cached-thumbnail checks (<see cref="NeedsCurrentThumb"/>).
/// Version 3 adds <see cref="Asset.DateTaken"/>; a version 2 row that is otherwise unchanged only
/// gets its EXIF date read (<see cref="NeedsDateTakenOnly"/>), not a full re-index.
/// </summary>
public static class ScanFastPath
{
    public const int IndexVersion = 3;

    /// <summary>Last version before Date taken; such rows take the light backfill.</summary>
    public const int DateTakenBackfillFrom = 2;

    /// <summary>Same format the scan stores in <see cref="Asset.DateModified"/>.</summary>
    public static string Stamp(DateTime lastWriteUtc) =>
        DateTime.SpecifyKind(lastWriteUtc, DateTimeKind.Utc).ToString("O");

    public static bool IsUnchanged(Asset? existing, long length, DateTime lastWriteUtc, bool onlineOnly) =>
        existing is not null
        && existing.IndexVersion == IndexVersion
        && SameContent(existing, length, lastWriteUtc, onlineOnly);

    /// <summary>Unchanged version 2 row: only Date taken is missing.</summary>
    public static bool NeedsDateTakenOnly(Asset? existing, long length, DateTime lastWriteUtc, bool onlineOnly) =>
        existing is not null
        && existing.IndexVersion == DateTakenBackfillFrom
        && SameContent(existing, length, lastWriteUtc, onlineOnly);

    private static bool SameContent(Asset existing, long length, DateTime lastWriteUtc, bool onlineOnly) =>
        !onlineOnly
        && !existing.IsOnlineOnly
        && !existing.IsOrphan
        && existing.FileSize == length
        && string.Equals(existing.DateModified, Stamp(lastWriteUtc), StringComparison.Ordinal)
        && existing.ContentHash is { Length: > 0 } hash
        && !HashService.IsCloudStub(hash)
        && (existing.Undecodable || (existing.Width is > 0 && existing.Height is > 0));

    /// <summary>
    /// The fast path also needs a current cached thumbnail, except for a file already recorded as
    /// undecodable: re-decoding the same bytes cannot succeed, and some (videos with no shell or
    /// ffmpeg thumbnail) have no cached placeholder or size, so they were re-hashed and counted
    /// "updated" on every rescan. Such a file is retried once it changes or
    /// <see cref="IndexVersion"/> is bumped.
    /// </summary>
    public static bool NeedsCurrentThumb(Asset existing) => !existing.Undecodable;

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
