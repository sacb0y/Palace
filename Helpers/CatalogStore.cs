using Microsoft.Data.Sqlite;

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
    public const string BackupsFolderName = "backups";

    /// <summary>Set when LocalState still held a catalog next to an existing durable one; a raw copy was kept here.</summary>
    public static string? UnmergedLocalStateBackup { get; private set; }

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
        UnmergedLocalStateBackup = null;

        var candidates = new List<string?>
        {
            NormalizeRoot(overrideRoot),
            ReadPointer(),
            ParsePath(settingsRoot),
            DefaultRoot()
        };

        var preferredFailed = false;
        foreach (var root in candidates.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryPrepareRoot(root!, localStateRoot, out var dbPath, out var reason))
            {
                ActiveRoot = root;
                ActiveDbPath = dbPath;
                // Only persist the pointer when the preferred root opened. If a custom
                // root failed (unplugged drive), keep the pointer so the next launch retries it.
                if (!preferredFailed)
                {
                    WritePointer(root!);
                }
                return dbPath!;
            }

            FallbackReason = reason;
            preferredFailed = true;
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
        var sameFolder = string.Equals(NormalizeRoot(localStateRoot), normalized, StringComparison.OrdinalIgnoreCase);

        try
        {
            if (File.Exists(dest) && !sameFolder && LooksLikeCatalog(localDb) && !HasUserData(dest) && HasUserData(localDb))
            {
                // Durable catalog is schema-only (e.g. created on a launch right after a wipe) but
                // LocalState holds real data again: set the empty one aside and migrate.
                SetAsideCatalog(normalized, "empty-durable");
                BackupRawCatalog(localStateRoot, normalized, "localstate-premigrate");
                SnapshotDatabase(localDb, dest);
                MigratedFromLocalState = true;
            }
            else if (File.Exists(dest))
            {
                // Both exist: open the durable one, never overwrite it, and keep a raw
                // copy of the LocalState catalog so nothing is silently orphaned.
                if (!sameFolder && LooksLikeCatalog(localDb))
                {
                    UnmergedLocalStateBackup = BackupRawCatalog(localStateRoot, normalized, "localstate-unmerged");
                }
            }
            else if (!sameFolder && File.Exists(localDb) && new FileInfo(localDb).Length > 0)
            {
                // Keep an untouched raw backup first, then snapshot into place.
                BackupRawCatalog(localStateRoot, normalized, "localstate-premigrate");
                MoveStaleSidecars(normalized);
                SnapshotDatabase(localDb, dest);
                MigratedFromLocalState = true;
            }
        }
        catch (Exception ex)
        {
            reason = "Could not migrate LocalState catalog: " + ex.Message;
            return false;
        }

        dbPath = dest;
        return true;
    }

    /// <summary>
    /// Copy a SQLite catalog (including any WAL content) to <paramref name="destDb"/> using the
    /// SQLite online backup API, into a temp file that is renamed into place only when complete.
    /// Never overwrites an existing destination. Safe while the source is open.
    /// </summary>
    public static void SnapshotDatabase(string sourceDb, string destDb)
    {
        if (File.Exists(destDb))
        {
            throw new IOException($"Refusing to overwrite existing catalog at {destDb}.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destDb)!);
        var tmp = destDb + ".partial";
        if (File.Exists(tmp))
        {
            File.Delete(tmp);
        }

        try
        {
            using (var src = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = sourceDb, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            using (var dst = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = tmp, Pooling = false }.ToString()))
            {
                src.Open();
                dst.Open();
                src.BackupDatabase(dst);
            }

            File.Move(tmp, destDb);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
            throw;
        }
    }

    /// <summary>
    /// Copy the active catalog into <paramref name="destRoot"/> for a folder switch.
    /// Returns false (and copies nothing) when the destination already has a catalog.
    /// </summary>
    public static bool TryCopyActiveCatalogTo(string destRoot)
    {
        var dest = DbPathIn(destRoot);
        if (File.Exists(dest))
        {
            return false;
        }

        if (ActiveDbPath is { } active && File.Exists(active))
        {
            MoveStaleSidecars(NormalizeRoot(destRoot)!);
            SnapshotDatabase(active, dest);
        }

        return true;
    }

    /// <summary>
    /// Raw copy of palace.db (+ wal/shm) into <c>&lt;root&gt;\backups\&lt;label&gt;-&lt;ticks&gt;</c>, once per
    /// source write time. Returns the backup folder, or null when there was nothing to copy.
    /// </summary>
    public static string? BackupRawCatalog(string sourceRoot, string catalogRoot, string label)
    {
        var srcDb = Path.Combine(sourceRoot, DbFileName);
        if (!File.Exists(srcDb))
        {
            return null;
        }

        var stamp = File.GetLastWriteTimeUtc(srcDb).Ticks;
        var dir = Path.Combine(catalogRoot, BackupsFolderName, $"{label}-{stamp}");
        if (File.Exists(Path.Combine(dir, DbFileName)))
        {
            return dir;
        }

        CopyCatalogFiles(sourceRoot, dir);
        return dir;
    }

    /// <summary>
    /// A leftover -wal/-shm without its db would be applied to a freshly migrated db; move them aside.
    /// </summary>
    public static void MoveStaleSidecars(string root)
    {
        if (File.Exists(DbPathIn(root)))
        {
            return;
        }

        string? dir = null;
        foreach (var name in new[] { DbFileName + "-wal", DbFileName + "-shm", DbFileName + "-journal" })
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path))
            {
                continue;
            }

            dir ??= Path.Combine(root, BackupsFolderName, $"stale-sidecars-{DateTime.UtcNow.Ticks}");
            Directory.CreateDirectory(dir);
            File.Move(path, Path.Combine(dir, name));
        }
    }

    /// <summary>True when the catalog has assets, watched folders, tag assignments/implications, or non-seed tags. Errors count as data (never discard).</summary>
    public static bool HasUserData(string dbPath)
    {
        try
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            conn.Open();
            // PalaceDb.SeedDefaults inserts these on every fresh catalog; they are not user data.
            const string seededTags = "'Subject','Character','Clothing','Shot','Style','Prompt'";
            var probes = new (string Table, string Sql)[]
            {
                ("Asset", "SELECT EXISTS(SELECT 1 FROM Asset)"),
                ("SourceFolder", "SELECT EXISTS(SELECT 1 FROM SourceFolder)"),
                ("AssetTag", "SELECT EXISTS(SELECT 1 FROM AssetTag)"),
                ("TagImplication", "SELECT EXISTS(SELECT 1 FROM TagImplication)"),
                ("Tag", $"SELECT EXISTS(SELECT 1 FROM Tag WHERE Name NOT IN ({seededTags}))")
            };
            foreach (var (table, sql) in probes)
            {
                using var exists = conn.CreateCommand();
                exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $n";
                exists.Parameters.AddWithValue("$n", table);
                if (Convert.ToInt64(exists.ExecuteScalar()) == 0)
                {
                    continue;
                }

                using var any = conn.CreateCommand();
                any.CommandText = sql;
                if (Convert.ToInt64(any.ExecuteScalar()) != 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Move palace.db and its sidecars into backups/&lt;label&gt;-&lt;ticks&gt;.</summary>
    public static string SetAsideCatalog(string root, string label)
    {
        var dir = Path.Combine(root, BackupsFolderName, $"{label}-{DateTime.UtcNow.Ticks}");
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { DbFileName, DbFileName + "-wal", DbFileName + "-shm", DbFileName + "-journal" })
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path))
            {
                File.Move(path, Path.Combine(dir, name));
            }
        }

        return dir;
    }

    /// <summary>Copy palace.db (+ wal/shm if present) into <paramref name="destRoot"/>. Never overwrites.</summary>
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

            File.Copy(src, Path.Combine(destRoot, name), overwrite: false);
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
