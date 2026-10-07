namespace Palace.Helpers;

/// <summary>
/// Resolves where <c>palace.db</c> lives. Default is outside the MSIX LocalState
/// container so <c>winapp unregister</c> does not wipe projects/tags. A pointer
/// file under <c>%LOCALAPPDATA%\Palace\</c> (not under Packages) survives
/// re-register. Requested by Sacb0y after a LocalState wipe.
/// </summary>
public static class CatalogStore
{
    public const string DbFileName = "palace.db";
    public const string RootPathKey = "CatalogRootPath";
    public const string AccessTokenKey = "CatalogAccessToken";
    public const string PointerFileName = "catalog-root.txt";
    public const string ProductFolderName = "Palace";
    public const string DefaultCatalogFolderName = "catalog";

    /// <summary>Directory that currently holds the open catalog (set by Resolve).</summary>
    public static string? ActiveRoot { get; private set; }

    /// <summary>Full path to the open <c>palace.db</c>.</summary>
    public static string? ActiveDbPath { get; private set; }

    /// <summary>True when Resolve copied LocalState into the durable root.</summary>
    public static bool MigratedFromLocalState { get; private set; }

    /// <summary>User-facing reason when falling back (missing drive, etc.).</summary>
    public static string? FallbackReason { get; private set; }

    public static string DefaultRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, ProductFolderName, DefaultCatalogFolderName);
    }

    public static string PointerPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, ProductFolderName, PointerFileName);
    }

    public static string DbPathIn(string root) =>
        Path.Combine(NormalizeRoot(root) ?? "", DbFileName);

    public static string? ParsePath(object? stored) => stored switch
    {
        string text when !string.IsNullOrWhiteSpace(text) => NormalizeRoot(text),
        _ => null
    };

    public static string? NormalizeRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return null;
        }
    }

    public static string? ReadPointer()
    {
        try
        {
            var pointer = PointerPath();
            if (!File.Exists(pointer))
            {
                return null;
            }

            var text = File.ReadAllText(pointer).Trim();
            return NormalizeRoot(text);
        }
        catch
        {
            return null;
        }
    }

    public static void WritePointer(string root)
    {
        var normalized = NormalizeRoot(root) ?? throw new ArgumentException("Catalog root is empty.", nameof(root));
        var pointer = PointerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
        File.WriteAllText(pointer, normalized + Environment.NewLine);
    }

    public static void ClearPointer()
    {
        try
        {
            var pointer = PointerPath();
            if (File.Exists(pointer))
            {
                File.Delete(pointer);
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    /// <summary>
    /// Pick the catalog directory, migrating LocalState when needed.
    /// Prefers: explicit override → durable pointer → LocalSettings → migrate
    /// LocalState → default durable root.
    /// </summary>
    public static string ResolveDbPath(
        string localStateRoot,
        string? settingsRoot = null,
        string? overrideRoot = null)
    {
        MigratedFromLocalState = false;
        FallbackReason = null;

        var candidates = new List<string?>
        {
            NormalizeRoot(overrideRoot),
            ReadPointer(),
            ParsePath(settingsRoot),
            DefaultRoot()
        };

        foreach (var root in candidates.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryPrepareRoot(root!, localStateRoot, out var dbPath, out var reason))
            {
                ActiveRoot = root;
                ActiveDbPath = dbPath;
                WritePointer(root!);
                return dbPath!;
            }

            FallbackReason = reason;
        }

        // Last resort: LocalState (legacy). Still writable after unregister wipe.
        var legacy = Path.Combine(localStateRoot, DbFileName);
        Directory.CreateDirectory(localStateRoot);
        ActiveRoot = NormalizeRoot(localStateRoot);
        ActiveDbPath = legacy;
        FallbackReason ??= "Using package LocalState (not durable across unregister).";
        return legacy;
    }

    public static bool TryPrepareRoot(
        string root,
        string localStateRoot,
        out string? dbPath,
        out string? reason)
    {
        dbPath = null;
        reason = null;
        var normalized = NormalizeRoot(root);
        if (normalized is null)
        {
            reason = "Invalid catalog path.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(normalized);
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }

        var dest = DbPathIn(normalized);
        var localDb = Path.Combine(localStateRoot, DbFileName);

        if (!File.Exists(dest) && LooksLikeCatalog(localDb))
        {
            try
            {
                CopyCatalogFiles(localStateRoot, normalized);
                MigratedFromLocalState = true;
            }
            catch (Exception ex)
            {
                reason = "Could not migrate LocalState catalog: " + ex.Message;
                return false;
            }
        }

        dbPath = dest;
        return true;
    }

    /// <summary>Copy palace.db (+ wal/shm if present) into <paramref name="destRoot"/>.</summary>
    public static void CopyCatalogFiles(string sourceRoot, string destRoot)
    {
        Directory.CreateDirectory(destRoot);
        foreach (var name in new[] { DbFileName, DbFileName + "-wal", DbFileName + "-shm" })
        {
            var src = Path.Combine(sourceRoot, name);
            if (!File.Exists(src))
            {
                continue;
            }

            File.Copy(src, Path.Combine(destRoot, name), overwrite: true);
        }
    }

    /// <summary>
    /// Heuristic: a real catalog is larger than an empty schema-only file (~4 KiB).
    /// </summary>
    public static bool LooksLikeCatalog(string dbPath)
    {
        try
        {
            if (!File.Exists(dbPath))
            {
                return false;
            }

            var info = new FileInfo(dbPath);
            if (info.Length > 16_384)
            {
                return true;
            }

            var wal = dbPath + "-wal";
            return File.Exists(wal) && new FileInfo(wal).Length > 1024;
        }
        catch
        {
            return false;
        }
    }

    public static string RootLabel(string? root = null)
    {
        var path = NormalizeRoot(root) ?? ActiveRoot ?? DefaultRoot();
        var durable = string.Equals(path, DefaultRoot(), StringComparison.OrdinalIgnoreCase);
        var suffix = durable
            ? " (durable — survives package unregister)"
            : " (custom durable folder)";
        if (string.Equals(path, NormalizeRoot(ActiveRoot), StringComparison.OrdinalIgnoreCase)
            && FallbackReason is not null
            && path.Contains(@"\Packages\", StringComparison.OrdinalIgnoreCase))
        {
            return path + " (package LocalState — wiped by unregister)";
        }

        return path + suffix;
    }
}
