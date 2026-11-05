using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// T4 follow-up (0.3.19): N1 FTS escape, N2 file-op ideal journal, N3 AddAlias migrate,
/// N4 multi-word search, N5 implied-as-alias fans out ideal implications.
/// </summary>
public sealed class TagAliasFollowupTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-alias-fu-" + Guid.NewGuid().ToString("N"));
    private PalaceDb _db = null!;
    private CatalogService _catalog = null!;
    private FileOpService _ops = null!;
    private SourceFolder _source = null!;
    private string _projectId = "";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _db = new PalaceDb(Path.Combine(_dir, "catalog", "palace.db"));
        _catalog = new CatalogService(_db);
        _projectId = (await _catalog.GetProjectsAsync())[0].Id;
        _source = new SourceFolder
        {
            Id = PalaceDb.NewId(),
            Path = Path.Combine(_dir, "watched"),
            ProjectId = _projectId
        };
        Directory.CreateDirectory(_source.Path);
        await _catalog.UpsertSourceFolderAsync(_source);
        _ops = new FileOpService(_catalog, () => new NopScope());
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }

    private sealed class NopScope : IDisposable
    {
        public void Dispose() { }
    }

    [Fact]
    public async Task N1_Search_SpecialChars_DoNotThrow_AndMatchQuotedTags()
    {
        var tag = await _catalog.CreateTagAsync("c++");
        var asset = await AddAssetAsync("cpp.png");
        await _catalog.AssignTagAsync(asset.Id, tag.Id, TagSource.Manual);

        // Would throw / return empty before quoting when MATCH sees bare +.
        var hits = await _catalog.SearchAsync("c++");
        Assert.Contains(hits, a => a.Id == asset.Id);

        var notTag = await _catalog.CreateTagAsync("NOT");
        var asset2 = await AddAssetAsync("not.png");
        await _catalog.AssignTagAsync(asset2.Id, notTag.Id, TagSource.Manual);
        var notHits = await _catalog.SearchAsync("NOT");
        Assert.Contains(notHits, a => a.Id == asset2.Id);
    }

    [Fact]
    public async Task N2_FileOp_AssignRemoveUndo_JournalsIdeal()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        Assert.True(await _catalog.AddAliasAsync(doggy.Id, dog.Id));
        var asset = await AddAssetAsync("pet.png");

        var assign = await _ops.AssignTagAsync([asset.Id], doggy.Id, doggy.Name, TagSource.Manual, _projectId);
        Assert.True(assign.Ok, assign.Message);
        Assert.Equal(dog.Id, (await SourcesByTagAsync(asset.Id)).Keys.Single());

        var batches = await _catalog.GetUndoableFileOpsAsync(_projectId);
        var assignBatch = batches[0];
        Assert.Equal(FileOpKind.TagAssign, assignBatch.Kind);
        Assert.Equal(dog.Id, assignBatch.Items[0].OldPath);
        Assert.Equal("dog", assignBatch.Items[0].NewPath);

        var remove = await _ops.RemoveTagAsync(
            [(asset.Id, TagSource.Manual)],
            doggy.Id,
            doggy.Name,
            _projectId);
        Assert.True(remove.Ok, remove.Message);
        Assert.Empty(await SourcesByTagAsync(asset.Id));

        batches = await _catalog.GetUndoableFileOpsAsync(_projectId);
        var removeBatch = batches[0];
        Assert.Equal(FileOpKind.TagRemove, removeBatch.Kind);
        Assert.Equal(dog.Id, removeBatch.Items[0].OldPath);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(TagSource.Manual, (await SourcesByTagAsync(asset.Id))[dog.Id]);
    }

    [Fact]
    public async Task N2_SnapshotRestore_RewritesAssignmentToIdeal()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        Assert.True(await _catalog.AddAliasAsync(doggy.Id, dog.Id));
        var asset = await AddAssetAsync("snap.png");

        // Craft a snapshot that still points at the alias id (pre-migrate / old journal).
        var snapshot = new TagDeleteSnapshot
        {
            Tag = doggy,
            Aliases = [new TagAlias { AliasTagId = doggy.Id, IdealTagId = dog.Id }],
            Assignments =
            [
                new AssetTag { AssetId = asset.Id, TagId = doggy.Id, Source = TagSource.Manual }
            ]
        };
        await _catalog.DeleteTagAsync(doggy.Id);
        await _catalog.RestoreTagSnapshotAsync(snapshot);

        var assigned = await SourcesByTagAsync(asset.Id);
        Assert.False(assigned.ContainsKey(doggy.Id));
        Assert.Equal(TagSource.Manual, assigned[dog.Id]);
    }

    [Fact]
    public async Task N3_AddAlias_MigratesExistingAssetTags_Deduped()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        var asset = await AddAssetAsync("legacy.png");

        // Pre-alias world: asset still carries the soon-to-be alias id.
        await UpsertRawAsync(asset.Id, doggy.Id, TagSource.Manual);
        await UpsertRawAsync(asset.Id, dog.Id, TagSource.Implied);

        Assert.True(await _catalog.AddAliasAsync(doggy.Id, dog.Id));

        var assigned = await SourcesByTagAsync(asset.Id);
        Assert.False(assigned.ContainsKey(doggy.Id));
        Assert.Equal(TagSource.Manual, assigned[dog.Id]);
        Assert.True(await FtsHasAsync(asset.Id, "dog"));
        Assert.False(await FtsHasAsync(asset.Id, "doggy"));
    }

    [Fact]
    public async Task N4_Search_MultiWordAlias_FindsIdeal()
    {
        var alias = await _catalog.CreateTagAsync("blue doggy");
        var ideal = await _catalog.CreateTagAsync("large canine");
        Assert.True(await _catalog.AddAliasAsync(alias.Id, ideal.Id));
        var asset = await AddAssetAsync("hound.png");
        await _catalog.AssignTagAsync(asset.Id, ideal.Id, TagSource.Manual);

        var hits = await _catalog.SearchAsync("blue doggy");
        Assert.Contains(hits, a => a.Id == asset.Id);
    }

    [Fact]
    public async Task N5_ImpliedAlias_AppliesIdealImplications()
    {
        var wolf = await _catalog.CreateTagAsync("wolf");
        var canineAlias = await _catalog.CreateTagAsync("canine-alias");
        var canine = await _catalog.CreateTagAsync("canine");
        var mammal = await _catalog.CreateTagAsync("mammal");

        Assert.True(await _catalog.AddAliasAsync(canineAlias.Id, canine.Id));
        // Implication targets the alias; ideal carries the next hop.
        Assert.True(await _catalog.AddImplicationAsync(wolf.Id, canineAlias.Id));
        Assert.True(await _catalog.AddImplicationAsync(canine.Id, mammal.Id));

        var asset = await AddAssetAsync("pack.png");
        await _catalog.AssignTagAsync(asset.Id, wolf.Id, TagSource.Manual);

        var assigned = await SourcesByTagAsync(asset.Id);
        Assert.Equal(TagSource.Manual, assigned[wolf.Id]);
        Assert.False(assigned.ContainsKey(canineAlias.Id));
        Assert.Equal(TagSource.Implied, assigned[canine.Id]);
        Assert.Equal(TagSource.Implied, assigned[mammal.Id]);
    }

    private async Task<Asset> AddAssetAsync(string fileName)
    {
        var path = Path.Combine(_source.Path, fileName);
        await File.WriteAllTextAsync(path, "pixels");
        var asset = new Asset
        {
            Id = PalaceDb.NewId(),
            SourceFolderId = _source.Id,
            Path = path,
            FileName = fileName,
            Kind = AssetKind.Image,
            DateAdded = PalaceDb.NowIso()
        };
        await _catalog.UpsertAssetAsync(asset, "");
        return (await _catalog.GetAssetByIdAsync(asset.Id))!;
    }

    private Task UpsertRawAsync(string assetId, string tagId, TagSource source) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO AssetTag (AssetId, TagId, Source) VALUES ($a, $t, $s)
                ON CONFLICT(AssetId, TagId) DO UPDATE SET Source = excluded.Source
                """;
            cmd.Parameters.AddWithValue("$a", assetId);
            cmd.Parameters.AddWithValue("$t", tagId);
            cmd.Parameters.AddWithValue("$s", source.ToString());
            cmd.ExecuteNonQuery();
        });

    private Task<Dictionary<string, TagSource>> SourcesByTagAsync(string assetId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TagId, Source FROM AssetTag WHERE AssetId = $id";
            cmd.Parameters.AddWithValue("$id", assetId);
            var map = new Dictionary<string, TagSource>(StringComparer.Ordinal);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                map[reader.GetString(0)] = Enum.Parse<TagSource>(reader.GetString(1));
            }

            return map;
        });

    private Task<bool> FtsHasAsync(string assetId, string tagName) =>
        _db.ReadAsync(conn =>
        {
            using var row = conn.CreateCommand();
            row.CommandText = "SELECT RowId FROM Asset WHERE Id = $id";
            row.Parameters.AddWithValue("$id", assetId);
            var rowId = Convert.ToInt64(row.ExecuteScalar());

            using var fts = conn.CreateCommand();
            fts.CommandText = "SELECT COUNT(*) FROM AssetFts WHERE AssetFts MATCH $q AND rowid = $row";
            fts.Parameters.AddWithValue("$q", "Tags:" + tagName);
            fts.Parameters.AddWithValue("$row", rowId);
            return Convert.ToInt64(fts.ExecuteScalar()) > 0;
        });
}
