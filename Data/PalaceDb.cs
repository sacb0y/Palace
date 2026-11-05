using Microsoft.Data.Sqlite;
using Palace.Models;

namespace Palace.Data;

public sealed class PalaceDb : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Folder of the pre-T5 backup that purged materialized Implied rows, when one ran.</summary>
    public string? ImpliedPurgeBackupDir { get; private set; }

    /// <summary>How many <c>Source=Implied</c> rows the T5 purge removed (0 when already clean).</summary>
    public int ImpliedRowsPurged { get; private set; }

    /// <summary>Set when backup/verify failed and purge was skipped (retry next launch).</summary>
    public string? ImpliedPurgeSkipReason { get; private set; }

    internal const string ImpliedPurgeMetaKey = "ImpliedPurgeV1";
    private const int ImpliedPurgeBackupKeep = 3;

    public PalaceDb(string dbPath)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        _connection.Open();
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            """;
        pragma.ExecuteNonQuery();
        InitializeSchema();
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a thread-pool thread while holding the single-connection gate,
    /// so catalog queries never execute on the caller's (UI) thread. <paramref name="cancellationToken"/>
    /// cancels the wait for the gate or a not-yet-started run; work already running is not interrupted.
    /// </summary>
    public Task<T> ReadAsync<T>(Func<SqliteConnection, T> work, CancellationToken cancellationToken = default) =>
        RunGatedAsync(work, cancellationToken);

    public Task WriteAsync(Action<SqliteConnection> work, CancellationToken cancellationToken = default) =>
        RunGatedAsync<object?>(
            conn =>
            {
                work(conn);
                return null;
            },
            cancellationToken);

    public Task<T> WriteAsync<T>(Func<SqliteConnection, T> work, CancellationToken cancellationToken = default) =>
        RunGatedAsync(work, cancellationToken);

    private async Task<T> RunGatedAsync<T>(Func<SqliteConnection, T> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The gate is released only after the work task finishes, even if the caller stops waiting.
            return await Task.Run(() => work(_connection), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Project (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SourceFolder (
                Id TEXT PRIMARY KEY,
                Path TEXT NOT NULL UNIQUE,
                AccessToken TEXT,
                AutoOrganizeNewFiles INTEGER NOT NULL DEFAULT 0,
                FolderTemplate TEXT,
                FileTemplate TEXT,
                DestinationPolicy TEXT NOT NULL DEFAULT 'InSource',
                DestinationPath TEXT,
                ProjectId TEXT,
                Kind TEXT NOT NULL DEFAULT 'Local',
                CloudAccountId TEXT,
                CloudRootItemId TEXT,
                FOREIGN KEY (ProjectId) REFERENCES Project(Id)
            );

            CREATE TABLE IF NOT EXISTS Asset (
                RowId INTEGER PRIMARY KEY AUTOINCREMENT,
                Id TEXT NOT NULL UNIQUE,
                SourceFolderId TEXT NOT NULL,
                Path TEXT NOT NULL UNIQUE,
                FileName TEXT NOT NULL,
                ContentHash TEXT,
                Kind TEXT NOT NULL,
                Width INTEGER,
                Height INTEGER,
                DurationMs INTEGER,
                IsOrphan INTEGER NOT NULL DEFAULT 0,
                Model TEXT,
                Seed TEXT,
                Prompt TEXT,
                NegativePrompt TEXT,
                MetadataJson TEXT,
                OrganizeError TEXT,
                Rating INTEGER,
                Notes TEXT,
                DateAdded TEXT NOT NULL,
                DateModified TEXT,
                FileSize INTEGER,
                IsOnlineOnly INTEGER NOT NULL DEFAULT 0,
                CloudItemId TEXT,
                IsHdr INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (SourceFolderId) REFERENCES SourceFolder(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS CloudAccount (
                Id TEXT PRIMARY KEY,
                Provider TEXT NOT NULL,
                AccountId TEXT NOT NULL,
                DisplayName TEXT,
                VaultKey TEXT NOT NULL,
                UNIQUE (Provider, AccountId)
            );

            CREATE TABLE IF NOT EXISTS Tag (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Priority INTEGER NOT NULL DEFAULT 0,
                Slug TEXT NOT NULL,
                Color TEXT,
                IsStarred INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS TagMembership (
                ParentId TEXT NOT NULL,
                ChildId TEXT NOT NULL,
                PRIMARY KEY (ParentId, ChildId),
                FOREIGN KEY (ParentId) REFERENCES Tag(Id) ON DELETE CASCADE,
                FOREIGN KEY (ChildId) REFERENCES Tag(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AssetTag (
                AssetId TEXT NOT NULL,
                TagId TEXT NOT NULL,
                Source TEXT NOT NULL,
                PRIMARY KEY (AssetId, TagId),
                FOREIGN KEY (AssetId) REFERENCES Asset(Id) ON DELETE CASCADE,
                FOREIGN KEY (TagId) REFERENCES Tag(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS TagImplication (
                TagId TEXT NOT NULL,
                ImpliedTagId TEXT NOT NULL,
                PRIMARY KEY (TagId, ImpliedTagId),
                FOREIGN KEY (TagId) REFERENCES Tag(Id) ON DELETE CASCADE,
                FOREIGN KEY (ImpliedTagId) REFERENCES Tag(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS TagAlias (
                AliasTagId TEXT NOT NULL PRIMARY KEY,
                IdealTagId TEXT NOT NULL,
                FOREIGN KEY(AliasTagId) REFERENCES Tag(Id) ON DELETE CASCADE,
                FOREIGN KEY(IdealTagId) REFERENCES Tag(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS OrganizeRule (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                FolderTemplate TEXT NOT NULL,
                FileTemplate TEXT NOT NULL,
                FacetIdsJson TEXT
            );

            CREATE TABLE IF NOT EXISTS OrganizeBatch (
                Id TEXT PRIMARY KEY,
                CreatedAt TEXT NOT NULL,
                IsUndone INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS OrganizeBatchItem (
                Id TEXT PRIMARY KEY,
                BatchId TEXT NOT NULL,
                AssetId TEXT NOT NULL,
                OldPath TEXT NOT NULL,
                NewPath TEXT NOT NULL,
                Status TEXT NOT NULL,
                Error TEXT,
                FOREIGN KEY (BatchId) REFERENCES OrganizeBatch(Id),
                FOREIGN KEY (AssetId) REFERENCES Asset(Id)
            );

            CREATE TABLE IF NOT EXISTS FileOpBatch (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Summary TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                IsUndone INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS FileOpItem (
                BatchId TEXT NOT NULL,
                Seq INTEGER NOT NULL,
                Step TEXT NOT NULL,
                AssetId TEXT,
                OldPath TEXT NOT NULL,
                NewPath TEXT NOT NULL,
                OldSourceId TEXT,
                NewSourceId TEXT,
                PRIMARY KEY (BatchId, Seq),
                FOREIGN KEY (BatchId) REFERENCES FileOpBatch(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Collection (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Kind TEXT NOT NULL DEFAULT 'Room',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                ProjectId TEXT,
                Icon TEXT,
                FOREIGN KEY (ProjectId) REFERENCES Project(Id)
            );

            CREATE TABLE IF NOT EXISTS CollectionItem (
                CollectionId TEXT NOT NULL,
                AssetId TEXT NOT NULL,
                Section TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (CollectionId, AssetId),
                FOREIGN KEY (CollectionId) REFERENCES Collection(Id) ON DELETE CASCADE,
                FOREIGN KEY (AssetId) REFERENCES Asset(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AiJob (
                Id TEXT PRIMARY KEY,
                AssetId TEXT,
                Status TEXT,
                CreatedAt TEXT
            );

            CREATE TABLE IF NOT EXISTS CatalogMeta (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS AssetFts USING fts5(
                FileName,
                Path,
                Tags,
                Prompt,
                Model,
                Notes,
                content='',
                contentless_delete=1,
                tokenize='unicode61'
            );

            CREATE INDEX IF NOT EXISTS IX_Asset_Source ON Asset(SourceFolderId);
            CREATE INDEX IF NOT EXISTS IX_Asset_Hash ON Asset(ContentHash);
            CREATE INDEX IF NOT EXISTS IX_TagMembership_Parent ON TagMembership(ParentId);
            CREATE INDEX IF NOT EXISTS IX_TagMembership_Child ON TagMembership(ChildId);
            CREATE INDEX IF NOT EXISTS IX_TagImplication_Tag ON TagImplication(TagId);
            CREATE INDEX IF NOT EXISTS IX_TagImplication_Implied ON TagImplication(ImpliedTagId);
            CREATE INDEX IF NOT EXISTS IX_TagAlias_Ideal ON TagAlias(IdealTagId);
            CREATE INDEX IF NOT EXISTS IX_SourceFolder_Project ON SourceFolder(ProjectId);
            CREATE INDEX IF NOT EXISTS IX_CloudAccount_Provider ON CloudAccount(Provider);
            CREATE INDEX IF NOT EXISTS IX_FileOpBatch_Project ON FileOpBatch(ProjectId, IsUndone, CreatedAt);
            """;
        cmd.ExecuteNonQuery();
        MigrateLegacyFacets();
        EnsureTagColorColumn();
        EnsureColumn("Tag", "IsStarred", "INTEGER NOT NULL DEFAULT 0");
        EnsureCloudColumns();
        EnsureColumn("Asset", "IsHdr", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("Asset", "IndexVersion", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("Asset", "Undecodable", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("Asset", "DateTaken", "TEXT");
        EnsureColumn("Collection", "Icon", "TEXT");
        EnsureAssetFtsContentlessDelete();
        EnsureUniqueTagNames();
        MigratePurgeMaterializedImplied();
        SeedDefaults();
        EnsureDefaultProject();
    }

    /// <summary>
    /// T5: one-shot gate; backup+verify; then delete only <c>Source='Implied'</c> and refresh FTS
    /// in the same transaction. Backup failures skip the purge and retry next launch.
    /// </summary>
    private void MigratePurgeMaterializedImplied()
    {
        EnsureCatalogMeta();
        if (string.Equals(GetCatalogMeta(ImpliedPurgeMetaKey), "1", StringComparison.Ordinal))
        {
            return;
        }

        long impliedCount;
        long assetTagCount;
        using (var probe = _connection.CreateCommand())
        {
            probe.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM AssetTag WHERE Source = 'Implied'),
                    (SELECT COUNT(*) FROM AssetTag)
                """;
            using var reader = probe.ExecuteReader();
            reader.Read();
            impliedCount = reader.GetInt64(0);
            assetTagCount = reader.GetInt64(1);
        }

        if (impliedCount == 0)
        {
            SetCatalogMeta(ImpliedPurgeMetaKey, "1");
            return;
        }

        try
        {
            var root = Path.GetDirectoryName(_dbPath) ?? ".";
            var backupsRoot = Path.Combine(root, "backups");
            Directory.CreateDirectory(backupsRoot);
            var backupDir = Path.Combine(backupsRoot, $"pre-t5-implied-{DateTime.UtcNow.Ticks}");
            Directory.CreateDirectory(backupDir);
            var backupDb = Path.Combine(backupDir, "palace.db");
            BackupCatalogCopy(backupDb);
            VerifyCatalogBackup(backupDb, assetTagCount);
            ImpliedPurgeBackupDir = backupDir;
            PruneImpliedPurgeBackups(backupsRoot);
        }
        catch (Exception ex)
        {
            // Disk full, locked/busy, refuse-to-overwrite, dir create, verify mismatch — skip purge.
            ImpliedPurgeSkipReason = ex.Message;
            ImpliedPurgeBackupDir = null;
            return;
        }

        try
        {
            var touched = new List<string>();
            using (var tx = _connection.BeginTransaction())
            {
                using (var read = _connection.CreateCommand())
                {
                    read.Transaction = tx;
                    read.CommandText = "SELECT DISTINCT AssetId FROM AssetTag WHERE Source = 'Implied'";
                    using var reader = read.ExecuteReader();
                    while (reader.Read())
                    {
                        touched.Add(reader.GetString(0));
                    }
                }

                using (var del = _connection.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM AssetTag WHERE Source = 'Implied'";
                    ImpliedRowsPurged = del.ExecuteNonQuery();
                }

                foreach (var assetId in touched)
                {
                    RefreshAssetFtsTags(assetId, tx);
                }

                tx.Commit();
            }

            SetCatalogMeta(ImpliedPurgeMetaKey, "1");
        }
        catch (Exception ex)
        {
            ImpliedPurgeSkipReason = ex.Message;
            ImpliedRowsPurged = 0;
            // Leave gate unset so the next launch retries.
        }
    }

    private void EnsureCatalogMeta()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS CatalogMeta (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private string? GetCatalogMeta(string key)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM CatalogMeta WHERE Key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private void SetCatalogMeta(string key, string value)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CatalogMeta (Key, Value) VALUES ($k, $v)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private void BackupCatalogCopy(string destDb)
    {
        if (File.Exists(destDb))
        {
            throw new IOException($"Refusing to overwrite existing catalog at {destDb}.");
        }

        var tmp = destDb + ".partial";
        if (File.Exists(tmp))
        {
            File.Delete(tmp);
        }

        try
        {
            using (var src = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = _dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
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

    private static void VerifyCatalogBackup(string backupDb, long expectedAssetTagCount)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupDb,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        conn.Open();

        using (var check = conn.CreateCommand())
        {
            check.CommandText = "PRAGMA quick_check";
            var result = Convert.ToString(check.ExecuteScalar()) ?? "";
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Backup quick_check failed: {result}");
            }
        }

        using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM AssetTag";
            var n = Convert.ToInt64(countCmd.ExecuteScalar());
            if (n != expectedAssetTagCount)
            {
                throw new InvalidOperationException(
                    $"Backup AssetTag count {n} does not match live count {expectedAssetTagCount}.");
            }
        }
    }

    private static void PruneImpliedPurgeBackups(string backupsRoot)
    {
        if (!Directory.Exists(backupsRoot))
        {
            return;
        }

        var dirs = Directory.GetDirectories(backupsRoot, "pre-t5-implied-*")
            .OrderByDescending(d => d, StringComparer.Ordinal)
            .ToList();
        for (var i = ImpliedPurgeBackupKeep; i < dirs.Count; i++)
        {
            try
            {
                Directory.Delete(dirs[i], recursive: true);
            }
            catch
            {
                // best-effort prune
            }
        }
    }

    /// <summary>Rebuild one asset's contentless FTS row after Implied purge (applied tags only).</summary>
    private void RefreshAssetFtsTags(string assetId, SqliteTransaction? tx = null)
    {
        long rowId;
        string fileName;
        string path;
        string? prompt;
        string? model;
        string? notes;
        using (var cmd = _connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT RowId, FileName, Path, Prompt, Model, Notes FROM Asset WHERE Id = $id
                """;
            cmd.Parameters.AddWithValue("$id", assetId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return;
            }

            rowId = reader.GetInt64(0);
            fileName = reader.GetString(1);
            path = reader.GetString(2);
            prompt = reader.IsDBNull(3) ? null : reader.GetString(3);
            model = reader.IsDBNull(4) ? null : reader.GetString(4);
            notes = reader.IsDBNull(5) ? null : reader.GetString(5);
        }

        var tagNames = new List<string>();
        using (var tags = _connection.CreateCommand())
        {
            tags.Transaction = tx;
            tags.CommandText = """
                SELECT t.Name FROM AssetTag at
                JOIN Tag t ON t.Id = at.TagId
                WHERE at.AssetId = $id
                """;
            tags.Parameters.AddWithValue("$id", assetId);
            using var reader = tags.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                {
                    tagNames.Add(reader.GetString(0));
                }
            }
        }

        try
        {
            using (var del = _connection.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM AssetFts WHERE rowid = $row";
                del.Parameters.AddWithValue("$row", rowId);
                del.ExecuteNonQuery();
            }

            using var ins = _connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO AssetFts (rowid, FileName, Path, Tags, Prompt, Model, Notes)
                VALUES ($row, $name, $path, $tags, $prompt, $model, $notes)
                """;
            ins.Parameters.AddWithValue("$row", rowId);
            ins.Parameters.AddWithValue("$name", fileName);
            ins.Parameters.AddWithValue("$path", path);
            ins.Parameters.AddWithValue("$tags", string.Join(" ", tagNames));
            ins.Parameters.AddWithValue("$prompt", prompt ?? "");
            ins.Parameters.AddWithValue("$model", model ?? "");
            ins.Parameters.AddWithValue("$notes", notes ?? "");
            ins.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Contentless FTS may reject DELETE on older tables; EnsureAssetFts ran first.
        }
    }

    /// <summary>
    /// Older catalogs created AssetFts with <c>content=''</c> only. On those tables the FTS5
    /// <c>delete</c> command is a silent no-op, so tag removals left stale Tokens. Recreate with
    /// <c>contentless_delete=1</c> and reindex so <c>DELETE FROM AssetFts</c> works.
    /// </summary>
    private void EnsureAssetFtsContentlessDelete()
    {
        using (var probe = _connection.CreateCommand())
        {
            probe.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'AssetFts'";
            var sql = Convert.ToString(probe.ExecuteScalar()) ?? "";
            if (sql.Contains("contentless_delete", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var tx = _connection.BeginTransaction();
        using (var drop = _connection.CreateCommand())
        {
            drop.Transaction = tx;
            drop.CommandText = """
                DROP TABLE IF EXISTS AssetFts;
                CREATE VIRTUAL TABLE AssetFts USING fts5(
                    FileName,
                    Path,
                    Tags,
                    Prompt,
                    Model,
                    Notes,
                    content='',
                    contentless_delete=1,
                    tokenize='unicode61'
                );
                INSERT INTO AssetFts (rowid, FileName, Path, Tags, Prompt, Model, Notes)
                SELECT a.RowId,
                       a.FileName,
                       a.Path,
                       COALESCE((
                           SELECT group_concat(t.Name, ' ')
                           FROM AssetTag at
                           JOIN Tag t ON t.Id = at.TagId
                           WHERE at.AssetId = a.Id
                       ), ''),
                       COALESCE(a.Prompt, ''),
                       COALESCE(a.Model, ''),
                       COALESCE(a.Notes, '')
                FROM Asset a;
                """;
            drop.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>Survivors of the startup duplicate-name merge; their implications may need a backfill.</summary>
    public TagNameMergeResult TagNameMerge { get; private set; } = TagNameMergeResult.None;

    /// <summary>Set when the duplicate-name merge failed; the catalog still opens and the C# guards still apply.</summary>
    public Exception? TagNameMergeError { get; private set; }

    private void EnsureUniqueTagNames()
    {
        try
        {
            TagNameMerge = TagNames.EnsureUnique(_connection);
        }
        catch (SqliteException ex)
        {
            TagNameMergeError = ex;
        }
    }

    private void EnsureCloudColumns()
    {
        EnsureColumn("SourceFolder", "Kind", "TEXT NOT NULL DEFAULT 'Local'");
        EnsureColumn("SourceFolder", "CloudAccountId", "TEXT");
        EnsureColumn("SourceFolder", "CloudRootItemId", "TEXT");
        EnsureColumn("Asset", "IsOnlineOnly", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("Asset", "CloudItemId", "TEXT");

        using var accounts = _connection.CreateCommand();
        accounts.CommandText = """
            CREATE TABLE IF NOT EXISTS CloudAccount (
                Id TEXT PRIMARY KEY,
                Provider TEXT NOT NULL,
                AccountId TEXT NOT NULL,
                DisplayName TEXT,
                VaultKey TEXT NOT NULL,
                UNIQUE (Provider, AccountId)
            );
            CREATE INDEX IF NOT EXISTS IX_Asset_CloudItem ON Asset(SourceFolderId, CloudItemId);
            CREATE INDEX IF NOT EXISTS IX_CloudAccount_Provider ON CloudAccount(Provider);
            """;
        accounts.ExecuteNonQuery();
    }

    private void EnsureColumn(string table, string column, string definition)
    {
        if (ColumnExists(table, column))
        {
            return;
        }

        using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    private void EnsureTagColorColumn()
    {
        if (ColumnExists("Tag", "Color"))
        {
            return;
        }

        using var alter = _connection.CreateCommand();
        alter.CommandText = "ALTER TABLE Tag ADD COLUMN Color TEXT";
        alter.ExecuteNonQuery();
    }

    private void MigrateLegacyFacets()
    {
        if (!TableExists("TagFacet") || !ColumnExists("Tag", "FacetId"))
        {
            return;
        }

        using var off = _connection.CreateCommand();
        off.CommandText = "PRAGMA foreign_keys=OFF";
        off.ExecuteNonQuery();

        using var tx = _connection.BeginTransaction();
        using (var create = _connection.CreateCommand())
        {
            create.Transaction = tx;
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS Tag_dag (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Priority INTEGER NOT NULL DEFAULT 0,
                    Slug TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS TagMembership_dag (
                    ParentId TEXT NOT NULL,
                    ChildId TEXT NOT NULL,
                    PRIMARY KEY (ParentId, ChildId)
                );
                INSERT OR IGNORE INTO Tag_dag (Id, Name, Priority, Slug)
                SELECT Id, Name, Priority, Slug FROM TagFacet;
                INSERT OR IGNORE INTO Tag_dag (Id, Name, Priority, Slug)
                SELECT Id, Name, Priority, Slug FROM Tag;
                INSERT OR IGNORE INTO TagMembership_dag (ParentId, ChildId)
                SELECT ParentId, Id FROM TagFacet WHERE ParentId IS NOT NULL;
                INSERT OR IGNORE INTO TagMembership_dag (ParentId, ChildId)
                SELECT FacetId, Id FROM Tag WHERE FacetId IS NOT NULL AND FacetId != '';
                INSERT OR IGNORE INTO TagMembership_dag (ParentId, ChildId)
                SELECT ParentId, Id FROM Tag WHERE ParentId IS NOT NULL;
                DROP TABLE Tag;
                DROP TABLE TagFacet;
                DROP TABLE IF EXISTS TagMembership;
                ALTER TABLE Tag_dag RENAME TO Tag;
                ALTER TABLE TagMembership_dag RENAME TO TagMembership;
                """;
            create.ExecuteNonQuery();
        }

        tx.Commit();

        using var on = _connection.CreateCommand();
        on.CommandText = "PRAGMA foreign_keys=ON";
        on.ExecuteNonQuery();
    }

    private bool TableExists(string name)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $n";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is not null;
    }

    private bool ColumnExists(string table, string column)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void SeedDefaults()
    {
        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM Tag";
        var count = Convert.ToInt64(check.ExecuteScalar());
        if (count > 0)
        {
            EnsureDefaultRule();
            return;
        }

        var subject = NewId();
        var character = NewId();
        InsertTag(subject, "Subject", 100, "Subject");
        InsertTag(character, "Character", 90, "Character");
        InsertTag(NewId(), "Clothing", 70, "Clothing");
        InsertTag(NewId(), "Shot", 60, "Shot");
        InsertTag(NewId(), "Style", 50, "Style");
        InsertTag(NewId(), "Prompt", 20, "Prompt");
        InsertMembership(subject, character);
        EnsureDefaultRule();
    }

    private void EnsureDefaultRule()
    {
        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM OrganizeRule";
        if (Convert.ToInt64(check.ExecuteScalar()) > 0)
        {
            return;
        }

        using var rule = _connection.CreateCommand();
        rule.CommandText = """
            INSERT INTO OrganizeRule (Id, Name, FolderTemplate, FileTemplate, FacetIdsJson)
            VALUES ($id, 'Default', '{Character}/{tags:2}', '{Character}-{tags}.{ext}', NULL);
            """;
        rule.Parameters.AddWithValue("$id", NewId());
        rule.ExecuteNonQuery();
    }

    private void EnsureDefaultProject()
    {
        if (!ColumnExists("Collection", "ProjectId"))
        {
            using var alter = _connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Collection ADD COLUMN ProjectId TEXT";
            alter.ExecuteNonQuery();
        }

        string defaultId;
        using (var find = _connection.CreateCommand())
        {
            find.CommandText = "SELECT Id FROM Project WHERE Name = 'Palace' LIMIT 1";
            defaultId = find.ExecuteScalar() as string ?? "";
        }

        if (string.IsNullOrEmpty(defaultId))
        {
            using var countCmd = _connection.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM Project";
            if (Convert.ToInt64(countCmd.ExecuteScalar()) == 0)
            {
                defaultId = NewId();
                using var insert = _connection.CreateCommand();
                insert.CommandText = "INSERT INTO Project (Id, Name) VALUES ($id, 'Palace')";
                insert.Parameters.AddWithValue("$id", defaultId);
                insert.ExecuteNonQuery();
            }
            else
            {
                using var first = _connection.CreateCommand();
                first.CommandText = "SELECT Id FROM Project ORDER BY Name LIMIT 1";
                defaultId = (string)first.ExecuteScalar()!;
            }
        }

        using (var sources = _connection.CreateCommand())
        {
            sources.CommandText = "UPDATE SourceFolder SET ProjectId = $id WHERE ProjectId IS NULL OR ProjectId = ''";
            sources.Parameters.AddWithValue("$id", defaultId);
            sources.ExecuteNonQuery();
        }

        using (var rooms = _connection.CreateCommand())
        {
            rooms.CommandText = "UPDATE Collection SET ProjectId = $id WHERE ProjectId IS NULL OR ProjectId = ''";
            rooms.Parameters.AddWithValue("$id", defaultId);
            rooms.ExecuteNonQuery();
        }

        using var index = _connection.CreateCommand();
        index.CommandText = "CREATE INDEX IF NOT EXISTS IX_Collection_Project ON Collection(ProjectId)";
        index.ExecuteNonQuery();
    }

    private void InsertTag(string id, string name, int priority, string slug)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Tag (Id, Name, Priority, Slug) VALUES ($id, $name, $priority, $slug)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$priority", priority);
        cmd.Parameters.AddWithValue("$slug", slug);
        cmd.ExecuteNonQuery();
    }

    private void InsertMembership(string parentId, string childId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO TagMembership (ParentId, ChildId) VALUES ($p, $c)";
        cmd.Parameters.AddWithValue("$p", parentId);
        cmd.Parameters.AddWithValue("$c", childId);
        cmd.ExecuteNonQuery();
    }

    public static string NewId() => Guid.NewGuid().ToString("N");

    public static string NowIso() => DateTimeOffset.UtcNow.ToString("O");

    public void Dispose()
    {
        _connection.Dispose();
        _gate.Dispose();
    }
}
