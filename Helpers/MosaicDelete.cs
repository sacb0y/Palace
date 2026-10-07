namespace Palace.Helpers;

/// <summary>
/// In-place mosaic / catalog list pruning after Recycle Bin delete.
/// Off WinUI so multi-delete UI updates stay testable without a full refresh.
/// </summary>
public static class MosaicDelete
{
    /// <summary>
    /// Directory <c>Changed</c> (parent LastWrite) must not walk the whole source —
    /// multi-delete floods those events. Created / Renamed on a folder may index
    /// that subtree; Deleted paths are handled as missing files/dirs.
    /// </summary>
    public static bool ShouldIndexDirectory(WatcherChangeKinds change, bool directoryExists) =>
        directoryExists && change is WatcherChangeKinds.Created or WatcherChangeKinds.Renamed;

    /// <summary>
    /// True when a watcher path that is not an existing file/dir should be treated
    /// as a deleted directory prefix (orphan everything under it) rather than a
    /// single missing catalog file. Dotted folder names (<c>shots.backup</c>,
    /// <c>.cache</c>) still count — only catalog media extensions are files.
    /// </summary>
    public static bool LooksLikeDeletedDirectory(string path, bool hadMatchingAsset) =>
        !hadMatchingAsset
        && !string.IsNullOrWhiteSpace(path)
        && !PathSafe.IsCatalogExt(PathSafe.Extension(path));

    /// <summary>Drop assets whose ids were deleted; strip empty folder headers.</summary>
    public static List<T> PruneItems<T>(
        IEnumerable<T> items,
        Func<T, bool> isFolderHeader,
        Func<T, string?> id,
        IReadOnlyCollection<string> removedIds)
    {
        var remove = removedIds is HashSet<string> set
            ? set
            : new HashSet<string>(removedIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);

        var kept = new List<T>();
        foreach (var item in items)
        {
            if (isFolderHeader(item))
            {
                kept.Add(item);
                continue;
            }

            var assetId = id(item);
            if (assetId is null || remove.Count == 0 || !remove.Contains(assetId))
            {
                kept.Add(item);
            }
        }

        return StripEmptyHeaders(kept, isFolderHeader);
    }

    /// <summary>Filter a catalog asset list by removed ids (ordinal).</summary>
    public static List<T> ExceptIds<T>(IEnumerable<T> assets, Func<T, string> id, IReadOnlyCollection<string> removedIds)
    {
        var remove = removedIds is HashSet<string> set
            ? set
            : new HashSet<string>(removedIds.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal);
        if (remove.Count == 0)
        {
            return assets.ToList();
        }

        return assets.Where(a => !remove.Contains(id(a))).ToList();
    }

    /// <summary>SQL IN-list chunk size (keeps under SQLite's variable limit).</summary>
    public const int SqlIdChunkSize = 200;

    public static IEnumerable<IReadOnlyList<string>> ChunkIds(IEnumerable<string> ids, int chunkSize = SqlIdChunkSize)
    {
        var size = chunkSize < 1 ? SqlIdChunkSize : chunkSize;
        var batch = new List<string>(size);
        foreach (var id in ids.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
        {
            batch.Add(id);
            if (batch.Count >= size)
            {
                yield return batch;
                batch = new List<string>(size);
            }
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private static List<T> StripEmptyHeaders<T>(List<T> items, Func<T, bool> isFolderHeader)
    {
        var result = new List<T>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            if (!isFolderHeader(items[i]))
            {
                result.Add(items[i]);
                continue;
            }

            var next = i + 1;
            if (next < items.Count && !isFolderHeader(items[next]))
            {
                result.Add(items[i]);
            }
        }

        return result;
    }
}

/// <summary>Subset of <see cref="System.IO.WatcherChangeTypes"/> used by delete/scan policy.</summary>
public enum WatcherChangeKinds
{
    Created = 1,
    Deleted = 2,
    Changed = 4,
    Renamed = 8,
}
