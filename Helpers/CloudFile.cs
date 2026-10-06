using System.IO;

namespace Palace.Helpers;

/// <summary>
/// Detects Windows Files On-Demand / cloud placeholders without opening a stream.
/// Uses <c>GetFileAttributes</c> flags only — never <c>File.Exists</c> and never file bytes.
/// </summary>
public static class CloudFile
{
    /// <summary>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS — not fully present locally.</summary>
    public const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    /// <summary>FILE_ATTRIBUTE_RECALL_ON_OPEN</summary>
    public const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

    /// <summary>FILE_ATTRIBUTE_PINNED — "Always keep on this device."</summary>
    public const FileAttributes Pinned = (FileAttributes)0x00080000;

    /// <summary>FILE_ATTRIBUTE_UNPINNED — "Free up space."</summary>
    public const FileAttributes Unpinned = (FileAttributes)0x00100000;

    /// <summary>FILE_ATTRIBUTE_OFFLINE</summary>
    public const FileAttributes Offline = FileAttributes.Offline;

    /// <summary>
    /// Flags that mean a read will hydrate. <see cref="Offline"/> is not in this
    /// mask by itself — leftover Offline on a hydrated reparse point is local.
    /// </summary>
    public const FileAttributes RecallMask = RecallOnDataAccess | RecallOnOpen;

    public const FileAttributes OnlineOnlyMask = RecallMask | Offline;

    /// <summary>
    /// True when the path names a file (including an On-Demand placeholder).
    /// Uses <see cref="File.GetAttributes(string)"/> only — does not open a stream.
    /// </summary>
    public static bool Exists(string? path) => TryGetAttributes(path, out _);

    /// <summary>
    /// Reads file attributes without opening a stream. Returns false for missing
    /// paths, directories, and attribute errors. Placeholders still succeed.
    /// </summary>
    public static bool TryGetAttributes(string? path, out FileAttributes attributes)
    {
        attributes = default;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsOnlineOnly(string? path) =>
        TryGetAttributes(path, out var attributes) && IsOnlineOnly(attributes);

    /// <summary>
    /// On-Demand / placeholder that is not fully present locally.
    /// Recall flags always win. Pinned files are local. Dropbox online-only
    /// files are <see cref="FileAttributes.SparseFile"/> +
    /// <see cref="FileAttributes.ReparsePoint"/> and often omit Recall bits —
    /// those must stay online-only so scan never opens the original.
    /// Hydrated OneDrive files keep ReparsePoint and leftover
    /// <see cref="Offline"/> without Sparse — those are local. Bare Offline
    /// (legacy HSM or tests) is online-only.
    /// </summary>
    public static bool IsOnlineOnly(FileAttributes attributes)
    {
        if ((attributes & FileAttributes.Directory) != 0)
        {
            return false;
        }

        if ((attributes & RecallMask) != 0)
        {
            return true;
        }

        if ((attributes & Pinned) != 0)
        {
            return false;
        }

        // Dropbox Files On-Demand placeholders: Sparse + Reparse, usually no
        // RecallOnDataAccess. Treating any ReparsePoint as local (leftover
        // Offline on hydrated OneDrive) made scan HashFileAsync / Extract
        // the original and recall every placeholder.
        if ((attributes & FileAttributes.SparseFile) != 0
            && (attributes & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }

        // Hydrated OneDrive/Dropbox files stay reparse points. Treating leftover
        // Offline on those as online-only skipped hash/thumbs on local files.
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        return (attributes & Offline) != 0;
    }

    /// <summary>
    /// File lives on OneDrive / Dropbox (On-Demand placeholder, pinned /
    /// unpinned sync copy, leftover Offline, or Cloud Filter reparse).
    /// Attributes only — never opens a stream. API display paths that are
    /// not on disk return false; callers also check <c>CloudItemId</c>.
    /// </summary>
    public static bool IsCloudBacked(string? path) =>
        TryGetAttributes(path, out var attributes) && IsCloudBacked(attributes);

    public static bool IsCloudBacked(FileAttributes attributes)
    {
        if ((attributes & FileAttributes.Directory) != 0)
        {
            return false;
        }

        return HasCloudSignals(attributes);
    }

    /// <summary>
    /// Source folder (file or directory) is OneDrive / Dropbox backed.
    /// Directories are excluded from <see cref="IsCloudBacked"/> so a sync
    /// root still stamps every tile in that source.
    /// </summary>
    public static bool IsCloudBackedFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return HasCloudSignals(File.GetAttributes(path));
        }
        catch
        {
            return false;
        }
    }

    public static bool IsCloudBackedFolder(FileAttributes attributes) =>
        HasCloudSignals(attributes);

    private static bool HasCloudSignals(FileAttributes attributes)
    {
        const FileAttributes cloudSignals =
            RecallMask | Pinned | Unpinned | Offline | FileAttributes.ReparsePoint | FileAttributes.SparseFile;
        return (attributes & cloudSignals) != 0;
    }

    public static IReadOnlyList<string> FilterLocalPaths(IEnumerable<string> paths, out int skippedOnlineOnly)
    {
        var kept = new List<string>();
        var skipped = 0;
        foreach (var path in paths)
        {
            if (IsOnlineOnly(path))
            {
                skipped++;
                continue;
            }

            kept.Add(path);
        }

        skippedOnlineOnly = skipped;
        return kept;
    }
}
