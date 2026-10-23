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

    /// <summary>
    /// Recount tiles under each folder header after a prune (ordered mosaic).
    /// </summary>
    public static void RecomputeFolderItemCounts<T>(
        IReadOnlyList<T> ordered,
        Func<T, bool> isFolderHeader,
        Action<T, int> setCount)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (!isFolderHeader(ordered[i]))
            {
                continue;
            }

            var count = 0;
            for (var j = i + 1; j < ordered.Count && !isFolderHeader(ordered[j]); j++)
            {
                count++;
            }

            setCount(ordered[i], count);
        }
    }

    /// <summary>
    /// Non-header targets for Review / select-all from the full ordered mosaic
    /// (includes tiles under collapsed headers).
    /// </summary>
    public static List<T> OrderedNonHeaderTargets<T>(
        IReadOnlyList<T> ordered,
        Func<T, bool> isFolderHeader) =>
        ordered.Where(item => !isFolderHeader(item)).ToList();

    /// <summary>
    /// Diff <paramref name="display"/> toward <paramref name="desired"/> with
    /// <c>RemoveAt</c> / insert so collapsed headers kept by
    /// <see cref="FolderSectionPrefs.FilterCollapsedDisplay{T}"/> stay put.
    /// Do not <see cref="PruneItems{T}"/> the display list alone — collapsed
    /// headers have no following tiles and <see cref="StripEmptyHeaders{T}"/>
    /// would drop them.
    /// </summary>
    public static void SyncDisplay<T>(IList<T> display, IReadOnlyList<T> desired)
        where T : class
    {
        var want = new HashSet<T>(desired);
        for (var i = display.Count - 1; i >= 0; i--)
        {
            if (!want.Contains(display[i]))
            {
                display.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < display.Count && ReferenceEquals(display[i], desired[i]))
            {
                continue;
            }

            var existing = -1;
            for (var j = i; j < display.Count; j++)
            {
                if (ReferenceEquals(display[j], desired[i]))
                {
                    existing = j;
                    break;
                }
            }

            if (existing >= 0)
            {
                display.RemoveAt(existing);
            }

            display.Insert(i, desired[i]);
        }

        while (display.Count > desired.Count)
        {
            display.RemoveAt(display.Count - 1);
        }
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
