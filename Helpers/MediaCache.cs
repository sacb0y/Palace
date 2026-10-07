using System.Globalization;

namespace Palace.Helpers;

/// <summary>
/// Optional managed media cache (thumbs + future preview decode). Persist via
/// LocalSettings. Default off — LocalFolder <c>thumbs/</c> still works without
/// a size cap. When on, enforce <see cref="MaxBytes"/> and optional custom root.
/// Off WinUI. Never write beside sources; never recall On-Demand to fill.
/// </summary>
public static class MediaCache
{
    public const string EnabledKey = "MediaCacheEnabled";
    public const string MaxMbKey = "MediaCacheMaxMb";
    public const string RootPathKey = "MediaCacheRootPath";
    public const string AccessTokenKey = "MediaCacheAccessToken";

    public const string ThumbsFolderName = "thumbs";
    public const string PreviewFolderName = "preview";

    public const double MinMaxMb = 256;
    public const double MaxMaxMb = 32768;
    public const double DefaultMaxMb = 2048;

    /// <summary>Evict down to this fraction of the budget so writes have headroom.</summary>
    public const double EvictHeadroom = 0.90;

    public static bool Enabled { get; private set; }

    public static double MaxMb { get; private set; } = DefaultMaxMb;

    public static long MaxBytes => (long)(ClampMaxMb(MaxMb) * 1024d * 1024d);

    /// <summary>User-picked cache root, or null for LocalFolder.</summary>
    public static string? RootPath { get; private set; }

    public static string? AccessToken { get; private set; }

    public static event EventHandler? Changed;

    public static void Apply(bool enabled, double maxMb, string? rootPath, string? accessToken = null)
    {
        maxMb = ClampMaxMb(maxMb);
        rootPath = NormalizeRoot(rootPath);
        accessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken.Trim();
        if (Enabled == enabled
            && Math.Abs(MaxMb - maxMb) < 0.01
            && string.Equals(RootPath, rootPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(AccessToken, accessToken, StringComparison.Ordinal))
        {
            return;
        }

        Enabled = enabled;
        MaxMb = maxMb;
        RootPath = rootPath;
        AccessToken = accessToken;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static bool ParseEnabled(object? stored) => stored switch
    {
        bool value => value,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => false
    };

    public static double ParseMaxMb(object? stored)
    {
        var mb = stored switch
        {
            double value => value,
            float value => value,
            int value => value,
            long value => value,
            string text when double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => DefaultMaxMb
        };
        return ClampMaxMb(mb);
    }

    public static string? ParsePath(object? stored) => stored switch
    {
        string text when !string.IsNullOrWhiteSpace(text) => text.Trim(),
        _ => null
    };

    public static double ClampMaxMb(double mb) =>
        Math.Clamp(mb, MinMaxMb, MaxMaxMb);

    public static string MaxMbLabel(double mb) =>
        $"{ClampMaxMb(mb):0} MB";

    public static string UsageLabel(long bytes, long maxBytes)
    {
        var used = FormatBytes(bytes);
        var max = FormatBytes(maxBytes);
        return $"{used} of {max}";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kb = bytes / 1024d;
        if (kb < 1024)
        {
            return $"{kb:0.#} KB";
        }

        var mb = kb / 1024d;
        if (mb < 1024)
        {
            return $"{mb:0.#} MB";
        }

        return $"{mb / 1024d:0.##} GB";
    }

    /// <summary>
    /// Active cache root: custom path when enabled and set, else LocalFolder.
    /// </summary>
    public static string ResolveRoot(string localRoot)
    {
        if (Enabled && !string.IsNullOrWhiteSpace(RootPath))
        {
            return RootPath!;
        }

        return localRoot;
    }

    public static string ResolveThumbsRoot(string localRoot) =>
        Path.Combine(ResolveRoot(localRoot), ThumbsFolderName);

    public static string ResolvePreviewRoot(string localRoot) =>
        Path.Combine(ResolveRoot(localRoot), PreviewFolderName);

    public static string DefaultRootLabel(string localRoot) =>
        string.IsNullOrWhiteSpace(localRoot) ? "App local folder" : localRoot;

    public static string RootLabel(string localRoot) =>
        Enabled && !string.IsNullOrWhiteSpace(RootPath)
            ? RootPath!
            : DefaultRootLabel(localRoot);

    /// <summary>
    /// Refuse watched sources and On-Demand / cloud-sync roots. Empty path means default LocalFolder (allowed).
    /// </summary>
    public static bool IsAllowedRoot(
        string? candidate,
        IEnumerable<string> sourcePaths,
        out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return true;
        }

        string full;
        try
        {
            full = Path.GetFullPath(candidate.Trim());
        }
        catch
        {
            reason = "That folder path is not valid.";
            return false;
        }

        if (CloudFile.IsCloudBackedFolder(full))
        {
            reason = "Cache cannot live in a cloud sync or Files On-Demand folder.";
            return false;
        }

        foreach (var source in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            try
            {
                if (PathSafe.IsUnderRoot(full, source) || PathSafe.IsUnderRoot(source, full))
                {
                    reason = "Cache cannot be inside a watched Library source (or contain one).";
                    return false;
                }
            }
            catch
            {
                // Skip unresolvable source paths.
            }
        }

        return true;
    }

    public static long MeasureBytes(string cacheRoot)
    {
        if (string.IsNullOrWhiteSpace(cacheRoot) || !Directory.Exists(cacheRoot))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in EnumerateCacheFiles(cacheRoot))
        {
            try
            {
                total += file.Length;
            }
            catch
            {
                // Skipping locked / gone files.
            }
        }

        return total;
    }

    /// <summary>
    /// Delete oldest files under thumbs/ and preview/ until under the budget.
    /// No-op when managed cache is off. Returns bytes deleted.
    /// </summary>
    public static long EnforceMaxSize(string cacheRoot, long? maxBytes = null)
    {
        if (!Enabled)
        {
            return 0;
        }

        var budget = maxBytes ?? MaxBytes;
        if (budget <= 0)
        {
            return 0;
        }

        var target = (long)(budget * EvictHeadroom);
        var files = EnumerateCacheFiles(cacheRoot)
            .OrderBy(f => f.LastAccessUtc)
            .ThenBy(f => f.LastWriteUtc)
            .ToList();

        long total = 0;
        foreach (var file in files)
        {
            total += file.Length;
        }

        if (total <= budget)
        {
            return 0;
        }

        long deleted = 0;
        foreach (var file in files)
        {
            if (total - deleted <= target)
            {
                break;
            }

            try
            {
                if (File.Exists(file.Path))
                {
                    File.Delete(file.Path);
                    deleted += file.Length;
                }
            }
            catch
            {
                // Best-effort eviction.
            }
        }

        return deleted;
    }

    /// <summary>Delete all files under thumbs/ and preview/ (not palace.db).</summary>
    public static long Clear(string cacheRoot)
    {
        if (string.IsNullOrWhiteSpace(cacheRoot) || !Directory.Exists(cacheRoot))
        {
            return 0;
        }

        long deleted = 0;
        foreach (var file in EnumerateCacheFiles(cacheRoot))
        {
            try
            {
                if (File.Exists(file.Path))
                {
                    File.Delete(file.Path);
                    deleted += file.Length;
                }
            }
            catch
            {
                // Best-effort clear.
            }
        }

        return deleted;
    }

    public static IReadOnlyList<CacheFileInfo> EnumerateCacheFiles(string cacheRoot)
    {
        var list = new List<CacheFileInfo>();
        if (string.IsNullOrWhiteSpace(cacheRoot) || !Directory.Exists(cacheRoot))
        {
            return list;
        }

        foreach (var folderName in new[] { ThumbsFolderName, PreviewFolderName })
        {
            var dir = Path.Combine(cacheRoot, folderName);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var path in paths)
            {
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists)
                    {
                        continue;
                    }

                    list.Add(new CacheFileInfo(
                        path,
                        info.Length,
                        info.LastAccessTimeUtc,
                        info.LastWriteTimeUtc));
                }
                catch
                {
                    // Skip.
                }
            }
        }

        return list;
    }

    /// <summary>
    /// Pure eviction planner for tests: which paths to delete given sizes and budget.
    /// </summary>
    public static IReadOnlyList<string> PlanEviction(
        IEnumerable<CacheFileInfo> files,
        long totalBytes,
        long maxBytes)
    {
        if (totalBytes <= maxBytes || maxBytes <= 0)
        {
            return [];
        }

        var target = (long)(maxBytes * EvictHeadroom);
        var ordered = files
            .OrderBy(f => f.LastAccessUtc)
            .ThenBy(f => f.LastWriteUtc)
            .ToList();
        var remove = new List<string>();
        long deleted = 0;
        foreach (var file in ordered)
        {
            if (totalBytes - deleted <= target)
            {
                break;
            }

            remove.Add(file.Path);
            deleted += file.Length;
        }

        return remove;
    }

    private static string? NormalizeRoot(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();
}

public readonly record struct CacheFileInfo(
    string Path,
    long Length,
    DateTime LastAccessUtc,
    DateTime LastWriteUtc);
