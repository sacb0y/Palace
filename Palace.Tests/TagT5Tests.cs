using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// T5 purge + Doktor blockers; T6 fold-in: launch-time Implied check (no permanent gate).
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

        // Clean reopen: no Implied rows → no backup/purge work.
        SqliteConnection.ClearAllPools();
        using var again = new PalaceDb(fixtureDb);
        Assert.Equal(0, again.ImpliedRowsPurged);
        Assert.Null(again.ImpliedPurgeBackupDir);
    }

    [Fact]
    public async Task DowngradeReupgrade_StillPurgesImpliedRows()
    {
        var fixtureDir = Path.Combine(_dir, "reupgrade");
        Directory.CreateDirectory(Path.Combine(fixtureDir, "catalog"));
        var fixtureDb = Path.Combine(fixtureDir, "catalog", "palace.db");

        string assetId;
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
            var parent = await catalog.CreateTagAsync("ru-parent");
            var child = await catalog.CreateTagAsync("ru-child");
            childId = child.Id;
            var path = Path.Combine(source.Path, "r.png");
            await File.WriteAllTextAsync(path, "x");
            var asset = new Asset
            {
                Id = PalaceDb.NewId(),
                SourceFolderId = source.Id,
                Path = path,
                FileName = "r.png",
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
                cmd.Parameters.AddWithValue("$p", parent.Id);
                cmd.Parameters.AddWithValue("$c", childId);
                cmd.ExecuteNonQuery();
            });
        }

        SqliteConnection.ClearAllPools();
        using (var first = new PalaceDb(fixtureDb))
        {
            Assert.True(first.ImpliedRowsPurged >= 1);
        }

        // Simulate old build rematerializing Implied after a downgrade.
        SqliteConnection.ClearAllPools();
        await using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = fixtureDb, Pooling = false }.ToString()))
        {
            await raw.OpenAsync();
            using var cmd = raw.CreateCommand();
            cmd.CommandText = "INSERT INTO AssetTag (AssetId, TagId, Source) VALUES ($a, $c, 'Implied')";
            cmd.Parameters.AddWithValue("$a", assetId);
            cmd.Parameters.AddWithValue("$c", childId);
            await cmd.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
        using var second = new PalaceDb(fixtureDb);
        Assert.True(second.ImpliedRowsPurged >= 1);
        await second.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM AssetTag WHERE Source = 'Implied'";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
            return 0;
        });
    }

    [Fact]
    public async Task BackupFailure_SkipsPurge_LogsReason_RetriesNextLaunch()
    {
        var fixtureDir = Path.Combine(_dir, "backup-fail");
        Directory.CreateDirectory(Path.Combine(fixtureDir, "catalog"));
        var fixtureDb = Path.Combine(fixtureDir, "catalog", "palace.db");

        string assetId;
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
            var parent = await catalog.CreateTagAsync("bf-parent");
            var child = await catalog.CreateTagAsync("bf-child");
            childId = child.Id;
            var path = Path.Combine(source.Path, "b.png");
            await File.WriteAllTextAsync(path, "x");
            var asset = new Asset
            {
                Id = PalaceDb.NewId(),
                SourceFolderId = source.Id,
                Path = path,
                FileName = "b.png",
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
                cmd.Parameters.AddWithValue("$p", parent.Id);
                cmd.Parameters.AddWithValue("$c", childId);
                cmd.ExecuteNonQuery();
            });
        }

        SqliteConnection.ClearAllPools();

        var backupsBlock = Path.Combine(fixtureDir, "catalog", "backups");
        await File.WriteAllTextAsync(backupsBlock, "not-a-directory");

        using (var blocked = new PalaceDb(fixtureDb))
        {
            Assert.Equal(0, blocked.ImpliedRowsPurged);
            Assert.False(string.IsNullOrEmpty(blocked.ImpliedPurgeSkipReason));
            Assert.Null(blocked.ImpliedPurgeBackupDir);
            await blocked.ReadAsync(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM AssetTag WHERE Source = 'Implied'";
                Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
                return 0;
            });
        }

        SqliteConnection.ClearAllPools();
        File.Delete(backupsBlock);

        using var retry = new PalaceDb(fixtureDb);
        Assert.True(retry.ImpliedRowsPurged >= 1);
        Assert.False(string.IsNullOrEmpty(retry.ImpliedPurgeBackupDir));
        await retry.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM AssetTag WHERE Source = 'Implied'";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
            return 0;
        });
    }

    [Fact]
    public async Task AssignTagForUndo_SkipsImplied_DoesNotDowngradeManual_SurvivesPurge()
    {
        var canine = await _catalog.CreateTagAsync("undo-canine");
        var decoy = await _catalog.CreateTagAsync("undo-decoy");
        var asset = await AddAssetAsync("undo.png");
        await _catalog.AssignTagAsync(asset.Id, canine.Id, TagSource.Manual);

        await _catalog.AssignTagForUndoAsync(asset.Id, canine.Id, TagSource.Implied);
        var afterUndo = await SourcesByTagAsync(asset.Id);
        Assert.Equal(TagSource.Manual, afterUndo[canine.Id]);

        await _catalog.AssignTagForUndoAsync(asset.Id, decoy.Id, TagSource.Implied);
        afterUndo = await SourcesByTagAsync(asset.Id);
        Assert.False(afterUndo.ContainsKey(decoy.Id));

        await _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO AssetTag (AssetId, TagId, Source) VALUES ($a, $d, 'Implied');
                """;
            cmd.Parameters.AddWithValue("$a", asset.Id);
            cmd.Parameters.AddWithValue("$d", decoy.Id);
            cmd.ExecuteNonQuery();
        });

        _db.Dispose();
        SqliteConnection.ClearAllPools();
        _db = new PalaceDb(Path.Combine(_dir, "catalog", "palace.db"));
        _catalog = new CatalogService(_db);
        Assert.True(_db.ImpliedRowsPurged >= 1);

        var survived = await SourcesByTagAsync(asset.Id);
        Assert.Equal(TagSource.Manual, survived[canine.Id]);
        Assert.False(survived.ContainsKey(decoy.Id));
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
    public async Task Display_ComputesImplied_SearchParentAndChildTerms()
    {
        var wolf = await _catalog.CreateTagAsync("wolf");
        var canine = await _catalog.CreateTagAsync("canine");
        Assert.True(await _catalog.AddImplicationAsync(wolf.Id, canine.Id));
        var asset = await AddAssetAsync("w.png");
        await _catalog.AssignTagAsync(asset.Id, wolf.Id, TagSource.Manual);

        var display = await _catalog.GetAssignedTagsAsync(asset.Id);
        Assert.Contains(display, t => t.TagId == canine.Id && t.Source == TagSource.Implied);

        var wolfHits = await _catalog.SearchAsync("wolf");
        Assert.Contains(wolfHits, a => a.Id == asset.Id);

        var canineHits = await _catalog.SearchAsync("canine");
        Assert.Contains(canineHits, a => a.Id == asset.Id);
    }

    [Fact]
    public async Task PruneImpliedPurgeBackups_KeepsLatestThree()
    {
        var fixtureDir = Path.Combine(_dir, "prune");
        var catalogDir = Path.Combine(fixtureDir, "catalog");
        Directory.CreateDirectory(catalogDir);
        var fixtureDb = Path.Combine(catalogDir, "palace.db");
        var backupsRoot = Path.Combine(catalogDir, "backups");
        Directory.CreateDirectory(backupsRoot);

        for (var i = 1; i <= 4; i++)
        {
            var d = Path.Combine(backupsRoot, $"pre-t5-implied-{i:D4}");
            Directory.CreateDirectory(d);
            await File.WriteAllTextAsync(Path.Combine(d, "palace.db"), "old");
        }

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
            var parent = await catalog.CreateTagAsync("prune-p");
            var child = await catalog.CreateTagAsync("prune-c");
            var path = Path.Combine(source.Path, "p.png");
            await File.WriteAllTextAsync(path, "x");
            var asset = new Asset
            {
                Id = PalaceDb.NewId(),
                SourceFolderId = source.Id,
                Path = path,
                FileName = "p.png",
                Kind = AssetKind.Image,
                DateAdded = PalaceDb.NowIso()
            };
            await catalog.UpsertAssetAsync(asset, "");
            await seed.WriteAsync(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO AssetTag (AssetId, TagId, Source) VALUES ($a, $p, 'Manual');
                    INSERT INTO AssetTag (AssetId, TagId, Source) VALUES ($a, $c, 'Implied');
                    """;
                cmd.Parameters.AddWithValue("$a", asset.Id);
                cmd.Parameters.AddWithValue("$p", parent.Id);
                cmd.Parameters.AddWithValue("$c", child.Id);
                cmd.ExecuteNonQuery();
            });
        }

        SqliteConnection.ClearAllPools();
        using var reopened = new PalaceDb(fixtureDb);
        Assert.True(reopened.ImpliedRowsPurged >= 1);

        var remaining = Directory.GetDirectories(backupsRoot, "pre-t5-implied-*");
        Assert.True(remaining.Length <= 3, $"expected <=3 backups, got {remaining.Length}");
        Assert.DoesNotContain(remaining, d => d.EndsWith("pre-t5-implied-0001", StringComparison.Ordinal));
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
