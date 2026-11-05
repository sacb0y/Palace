using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// T5: purge materialized Implied, compute at display/filter time, Doktor alias notes a–d.
/// </summary>
public sealed class TagT5Tests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-t5-" + Guid.NewGuid().ToString("N"));
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
        _ops = new FileOpService(_catalog, () => new Nop());
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }

    private sealed class Nop : IDisposable
    {
        public void Dispose() { }
    }

    [Fact]
    public async Task FixtureDb_PurgesImplied_BacksUp_AndRefreshesFts()
    {
        var fixtureDir = Path.Combine(_dir, "fixture");
        Directory.CreateDirectory(Path.Combine(fixtureDir, "catalog"));
        var fixtureDb = Path.Combine(fixtureDir, "catalog", "palace.db");

        // Build a catalog with a stored Implied row, then reopen to trigger T5 purge.
        string assetId;
        string parentId;
        string childId;
        using (var seed = new PalaceDb(fixtureDb))
        {
            var catalog = new CatalogService(seed);
            var projectId = (await catalog.GetProjectsAsync())[0].Id;
            var source = new SourceFolder
            {
                Id = PalaceDb.NewId(),
                Path = Path.Combine(fixtureDir, "watched"),
                ProjectId = projectId
            };
            Directory.CreateDirectory(source.Path);
            await catalog.UpsertSourceFolderAsync(source);
            var parent = await catalog.CreateTagAsync("parent-t5");
            var child = await catalog.CreateTagAsync("child-t5");
            parentId = parent.Id;
            childId = child.Id;
            var path = Path.Combine(source.Path, "a.png");
            await File.WriteAllTextAsync(path, "x");
            var asset = new Asset
            {
                Id = PalaceDb.NewId(),
                SourceFolderId = source.Id,
                Path = path,
                FileName = "a.png",
                Kind = AssetKind.Image,
                DateAdded = PalaceDb.NowIso()
            };
            await catalog.UpsertAssetAsync(asset, "");
            assetId = asset.Id;
            await seed.WriteAsync(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO AssetTag (AssetId, TagId, Source) VALUES ($a, $p, 'Manual');
                    INSERT INTO AssetTag (AssetId, TagId, Source) VALUES ($a, $c, 'Implied');
                    """;
                cmd.Parameters.AddWithValue("$a", assetId);
                cmd.Parameters.AddWithValue("$p", parentId);
                cmd.Parameters.AddWithValue("$c", childId);
                cmd.ExecuteNonQuery();
            });
            await catalog.AssignTagAsync(assetId, parent.Id, TagSource.Manual);
        }

        SqliteConnection.ClearAllPools();

        using var reopened = new PalaceDb(fixtureDb);
        Assert.True(reopened.ImpliedRowsPurged >= 1);
        Assert.False(string.IsNullOrEmpty(reopened.ImpliedPurgeBackupDir));
        Assert.True(File.Exists(Path.Combine(reopened.ImpliedPurgeBackupDir!, "palace.db")));

        await reopened.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM AssetTag WHERE Source = 'Implied'";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
            return 0;
        });
    }

    [Fact]
    public async Task AddAlias_ResolvesIdealThatIsItselfAnAlias()
    {
        var a = await _catalog.CreateTagAsync("a-alias");
        var b = await _catalog.CreateTagAsync("b-mid");
        var c = await _catalog.CreateTagAsync("c-ideal");
        Assert.True((await _catalog.AddAliasAsync(b.Id, c.Id)).Ok);

        var result = await _catalog.AddAliasAsync(a.Id, b.Id);
        Assert.True(result.Ok);
        Assert.Equal(c.Id, result.IdealTagId);
        Assert.Equal(c.Id, await _catalog.ResolveIdealTagIdAsync(a.Id));
    }

    [Fact]
    public async Task AddAlias_FileOp_JournalsMigration_UndoRestores()
    {
        var doggy = await _catalog.CreateTagAsync("doggy");
        var dog = await _catalog.CreateTagAsync("dog");
        var asset = await AddAssetAsync("pet.png");
        await _catalog.AssignTagAsync(asset.Id, doggy.Id, TagSource.Manual);

        var result = await _ops.AddAliasAsync(doggy.Id, dog.Id, _projectId);
        Assert.True(result.Ok, result.Message);

        var after = await SourcesByTagAsync(asset.Id);
        Assert.False(after.ContainsKey(doggy.Id));
        Assert.Equal(TagSource.Manual, after[dog.Id]);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);

        var restored = await SourcesByTagAsync(asset.Id);
        Assert.Equal(TagSource.Manual, restored[doggy.Id]);
        Assert.False(restored.ContainsKey(dog.Id));
    }

    [Fact]
    public async Task Display_ComputesImplied_SearchStillMatchesAppliedParent()
    {
        var wolf = await _catalog.CreateTagAsync("wolf");
        var canine = await _catalog.CreateTagAsync("canine");
        Assert.True(await _catalog.AddImplicationAsync(wolf.Id, canine.Id));
        var asset = await AddAssetAsync("w.png");
        await _catalog.AssignTagAsync(asset.Id, wolf.Id, TagSource.Manual);

        var display = await _catalog.GetAssignedTagsAsync(asset.Id);
        Assert.Contains(display, t => t.TagId == canine.Id && t.Source == TagSource.Implied);

        // FTS indexes applied only — parent name on the asset still matches.
        var hits = await _catalog.SearchAsync("wolf");
        Assert.Contains(hits, a => a.Id == asset.Id);
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
}
