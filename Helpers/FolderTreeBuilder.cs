namespace Palace.Helpers;

/// <summary>Sort order for the Library folder tree. Source roots keep their added order.</summary>
public enum FolderTreeSort
{
    NameAsc,
    NameDesc,
    MostItems,
    Newest
}

/// <summary>A watched source as the tree builder sees it.</summary>
/// <param name="ScanDisk">False for cloud-API sources (no local disk to list).</param>
public sealed record FolderTreeSource(string Id, string Path, bool ScanDisk);

/// <summary>A sub-directory listed from disk.</summary>
public sealed record FolderTreeDir(string Path, DateTime Modified);

/// <summary>One folder in the tree. <see cref="AssetCount"/> includes sub-folders.</summary>
public sealed class FolderTreeEntry
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string SourceFolderId { get; init; } = "";
    public bool IsRoot { get; init; }
    public int AssetCount { get; set; }
    public DateTime Modified { get; set; }
    public List<FolderTreeEntry> Children { get; } = [];
}

/// <summary>
/// Builds the folder tree from catalog asset folders plus real directories on disk, so empty
/// folders show. WinUI-free (tests, future CLI). Listing a cloud placeholder folder only reads
/// metadata; it never hydrates files.
/// </summary>
public static class FolderTreeBuilder
{
    /// <summary>Stop listing disk after this many directories per source (huge trees).</summary>
    public const int MaxDirectoriesPerSource = 4000;

    /// <summary>Deepest level listed from disk below a source root.</summary>
    public const int MaxDepth = 16;

    public static List<FolderTreeEntry> Build(
        IReadOnlyList<FolderTreeSource> sources,
        IEnumerable<(string SourceId, string Path)> assets,
        Func<string, IEnumerable<FolderTreeDir>>? listChildren,
        FolderTreeSort sort)
    {
        var bySource = assets
            .GroupBy(a => a.SourceId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Path).ToList(), StringComparer.Ordinal);
        var roots = new List<FolderTreeEntry>();
        foreach (var source in sources)
        {
            var rootPath = source.Path.TrimEnd('\\', '/');
            if (rootPath.Length == 0)
            {
                rootPath = source.Path;
            }

            var root = new FolderTreeEntry
            {
                Name = System.IO.Path.GetFileName(rootPath) is { Length: > 0 } n ? n : source.Path,
                Path = source.Path,
                SourceFolderId = source.Id,
                IsRoot = true
            };
            var index = new Dictionary<string, FolderTreeEntry>(StringComparer.OrdinalIgnoreCase) { [Key(source.Path)] = root };

            if (source.ScanDisk && listChildren is not null)
            {
                ScanDisk(root, index, listChildren);
            }

            if (bySource.TryGetValue(source.Id, out var paths))
            {
                foreach (var assetPath in paths)
                {
                    var dir = System.IO.Path.GetDirectoryName(assetPath);
                    if (dir is null || !FolderNames.IsSameOrUnder(dir, source.Path))
                    {
                        continue;
                    }

                    var node = Ensure(root, index, source.Path, dir);
                    node.AssetCount++;
                }
            }

            Total(root);
            roots.Add(root);
        }

        foreach (var root in roots)
        {
            SortChildren(root, sort);
        }

        return roots;
    }

    /// <summary>Real sub-directories of <paramref name="dir"/>, skipping hidden, system, dot and link folders.</summary>
    public static IEnumerable<FolderTreeDir> ListDiskChildren(string dir)
    {
        IEnumerable<DirectoryInfo> children;
        try
        {
            children = new DirectoryInfo(dir).EnumerateDirectories("*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            }).ToList();
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (IsSkippedName(child.Name))
            {
                continue;
            }

            DateTime modified;
            try
            {
                // Symlinks / junctions can loop or leave the root. Cloud placeholders are reparse
                // points too, but have no LinkTarget, so they stay.
                if (child.LinkTarget is not null)
                {
                    continue;
                }

                modified = child.LastWriteTimeUtc;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            yield return new FolderTreeDir(child.FullName, modified);
        }
    }

    /// <summary>Dot folders (".git", ".dropbox.cache", Palace's own temp names) never show.</summary>
    public static bool IsSkippedName(string name) => name.Length == 0 || name[0] == '.' || name[0] == '$';

    public static void SortChildren(FolderTreeEntry node, FolderTreeSort sort)
    {
        if (node.Children.Count > 1)
        {
            var sorted = Sorted(node.Children, sort).ToList();
            node.Children.Clear();
            node.Children.AddRange(sorted);
        }

        foreach (var child in node.Children)
        {
            SortChildren(child, sort);
        }
    }

    public static IEnumerable<FolderTreeEntry> Sorted(IEnumerable<FolderTreeEntry> items, FolderTreeSort sort) => sort switch
    {
        FolderTreeSort.NameDesc => items.OrderByDescending(c => c.Name, NaturalStringComparer.Instance),
        FolderTreeSort.MostItems => items.OrderByDescending(c => c.AssetCount).ThenBy(c => c.Name, NaturalStringComparer.Instance),
        FolderTreeSort.Newest => items.OrderByDescending(c => c.Modified).ThenBy(c => c.Name, NaturalStringComparer.Instance),
        _ => items.OrderBy(c => c.Name, NaturalStringComparer.Instance)
    };

    public static string Label(FolderTreeSort sort) => sort switch
    {
        FolderTreeSort.NameDesc => "Name (Z to A)",
        FolderTreeSort.MostItems => "Most items",
        FolderTreeSort.Newest => "Recently changed",
        _ => "Name (A to Z)"
    };

    public static FolderTreeSort Parse(string? value) =>
        Enum.TryParse<FolderTreeSort>(value, out var s) && Enum.IsDefined(s) ? s : FolderTreeSort.NameAsc;

    private static void ScanDisk(FolderTreeEntry root, Dictionary<string, FolderTreeEntry> index, Func<string, IEnumerable<FolderTreeDir>> listChildren)
    {
        var queue = new Queue<(FolderTreeEntry Node, int Depth)>();
        queue.Enqueue((root, 0));
        var listed = 0;
        while (queue.Count > 0 && listed < MaxDirectoriesPerSource)
        {
            var (node, depth) = queue.Dequeue();
            if (depth >= MaxDepth)
            {
                continue;
            }

            foreach (var dir in listChildren(node.Path))
            {
                if (listed >= MaxDirectoriesPerSource)
                {
                    break;
                }

                var key = Key(dir.Path);
                if (index.ContainsKey(key) || !FolderNames.IsSameOrUnder(dir.Path, root.Path))
                {
                    continue;
                }

                var child = new FolderTreeEntry
                {
                    Name = System.IO.Path.GetFileName(dir.Path.TrimEnd('\\', '/')),
                    Path = dir.Path,
                    SourceFolderId = root.SourceFolderId,
                    Modified = dir.Modified
                };
                index[key] = child;
                node.Children.Add(child);
                listed++;
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static FolderTreeEntry Ensure(FolderTreeEntry root, Dictionary<string, FolderTreeEntry> index, string sourcePath, string fullDir)
    {
        if (index.TryGetValue(Key(fullDir), out var hit))
        {
            return hit;
        }

        var relative = System.IO.Path.GetRelativePath(sourcePath, fullDir);
        var current = root;
        var acc = sourcePath;
        foreach (var part in relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            acc = System.IO.Path.Combine(acc, part);
            if (!index.TryGetValue(Key(acc), out var next))
            {
                next = new FolderTreeEntry { Name = part, Path = acc, SourceFolderId = root.SourceFolderId };
                index[Key(acc)] = next;
                current.Children.Add(next);
            }

            current = next;
        }

        return current;
    }

    private static int Total(FolderTreeEntry node)
    {
        var total = node.AssetCount;
        var newest = node.Modified;
        foreach (var child in node.Children)
        {
            total += Total(child);
            if (child.Modified > newest)
            {
                newest = child.Modified;
            }
        }

        node.AssetCount = total;
        node.Modified = newest;
        return total;
    }

    private static string Key(string path) => path.TrimEnd('\\', '/');
}
