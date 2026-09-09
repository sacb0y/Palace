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

            CREATE TABLE IF NOT EXISTS TagFacet (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                ParentId TEXT,
                Priority INTEGER NOT NULL DEFAULT 0,
                Slug TEXT NOT NULL,
                FOREIGN KEY (ParentId) REFERENCES TagFacet(Id)
            );

            CREATE TABLE IF NOT EXISTS Tag (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                FacetId TEXT NOT NULL,
                ParentId TEXT,
                Priority INTEGER NOT NULL DEFAULT 0,
                Slug TEXT NOT NULL,
                FOREIGN KEY (FacetId) REFERENCES TagFacet(Id),
                FOREIGN KEY (ParentId) REFERENCES Tag(Id)
            );

            CREATE TABLE IF NOT EXISTS AssetTag (
                AssetId TEXT NOT NULL,
                TagId TEXT NOT NULL,
                Source TEXT NOT NULL,
                PRIMARY KEY (AssetId, TagId),
                FOREIGN KEY (AssetId) REFERENCES Asset(Id) ON DELETE CASCADE,
                FOREIGN KEY (TagId) REFERENCES Tag(Id) ON DELETE CASCADE
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
                SortOrder INTEGER NOT NULL DEFAULT 0
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
            CREATE INDEX IF NOT EXISTS IX_Tag_Facet ON Tag(FacetId);
            """;
        cmd.ExecuteNonQuery();
        SeedDefaults();
    }

    private void SeedDefaults()
    {
        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM TagFacet";
        var count = Convert.ToInt64(check.ExecuteScalar());
        if (count > 0)
        {
            return;
        }

        var subject = NewId();
        var character = NewId();
        InsertFacet(subject, "Subject", null, 100, "Subject");
        InsertFacet(character, "Character", subject, 90, "Character");
        InsertFacet(NewId(), "Clothing", null, 70, "Clothing");
        InsertFacet(NewId(), "Shot", null, 60, "Shot");
        InsertFacet(NewId(), "Style", null, 50, "Style");
        InsertFacet(NewId(), "Prompt", null, 20, "Prompt");

        using var rule = _connection.CreateCommand();
        rule.CommandText = """
            INSERT INTO OrganizeRule (Id, Name, FolderTemplate, FileTemplate, FacetIdsJson)
            VALUES ($id, 'Default', '{Character}/{tags:2}', '{Character}-{tags}.{ext}', NULL);
            """;
        rule.Parameters.AddWithValue("$id", NewId());
        rule.ExecuteNonQuery();
    }

    private void InsertFacet(string id, string name, string? parentId, int priority, string slug)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TagFacet (Id, Name, ParentId, Priority, Slug)
            VALUES ($id, $name, $parent, $priority, $slug);
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$parent", (object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$priority", priority);
        cmd.Parameters.AddWithValue("$slug", slug);
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
