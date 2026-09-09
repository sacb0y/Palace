using Microsoft.Data.Sqlite;
using Palace.Models;

namespace Palace.Data;

public sealed class PalaceDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PalaceDb(string dbPath)
    {
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

    public async Task<T> ReadAsync<T>(Func<SqliteConnection, T> work)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return work(_connection);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(Action<SqliteConnection> work)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            work(_connection);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> WriteAsync<T>(Func<SqliteConnection, T> work)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return work(_connection);
        }
        finally
        {
            _gate.Release();
        }
    }

    public T Read<T>(Func<SqliteConnection, T> work)
    {
        _gate.Wait();
        try
        {
            return work(_connection);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Write(Action<SqliteConnection> work)
    {
        _gate.Wait();
        try
        {
            work(_connection);
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
                FOREIGN KEY (SourceFolderId) REFERENCES SourceFolder(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Tag (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Priority INTEGER NOT NULL DEFAULT 0,
                Slug TEXT NOT NULL
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

            CREATE TABLE IF NOT EXISTS Collection (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Kind TEXT NOT NULL DEFAULT 'Room',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                ProjectId TEXT,
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

            CREATE VIRTUAL TABLE IF NOT EXISTS AssetFts USING fts5(
                FileName,
                Path,
                Tags,
                Prompt,
                Model,
                Notes,
                content='',
                tokenize='unicode61'
            );

            CREATE INDEX IF NOT EXISTS IX_Asset_Source ON Asset(SourceFolderId);
            CREATE INDEX IF NOT EXISTS IX_Asset_Hash ON Asset(ContentHash);
            CREATE INDEX IF NOT EXISTS IX_TagMembership_Parent ON TagMembership(ParentId);
            CREATE INDEX IF NOT EXISTS IX_TagMembership_Child ON TagMembership(ChildId);
            CREATE INDEX IF NOT EXISTS IX_TagImplication_Tag ON TagImplication(TagId);
            CREATE INDEX IF NOT EXISTS IX_TagImplication_Implied ON TagImplication(ImpliedTagId);
            CREATE INDEX IF NOT EXISTS IX_SourceFolder_Project ON SourceFolder(ProjectId);
            """;
        cmd.ExecuteNonQuery();
        MigrateLegacyFacets();
        SeedDefaults();
        EnsureDefaultProject();
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
