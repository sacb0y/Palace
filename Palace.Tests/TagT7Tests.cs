using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>T7: bulk assign/remove/paste as one FileOp batch; Doktor #152 fold-ins.</summary>
public sealed class TagT7Tests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-t7-" + Guid.NewGuid().ToString("N"));
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
    public async Task BulkAssign_OneBatch_UndoRestoresAllAssets()
    {
        var tag = await _catalog.CreateTagAsync("bulk-a");
        var a = await AddAssetAsync("a.png");
        var b = await AddAssetAsync("b.png");

        var result = await _ops.AssignTagAsync(
            [a.Id, b.Id], tag.Id, tag.Name, TagSource.Manual, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.False(string.IsNullOrEmpty(result.BatchId));
        Assert.Contains(tag.Id, await TagIdsOn(a.Id));
        Assert.Contains(tag.Id, await TagIdsOn(b.Id));

        var peek = await _ops.PeekUndoAsync(_projectId);
        Assert.NotNull(peek);
        Assert.Equal(FileOpKind.TagAssign, peek!.Kind);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.DoesNotContain(tag.Id, await TagIdsOn(a.Id));
        Assert.DoesNotContain(tag.Id, await TagIdsOn(b.Id));
    }

    [Fact]
    public async Task BulkRemove_OneBatch_UndoRestoresSources()
    {
        var tag = await _catalog.CreateTagAsync("bulk-rm");
        var a = await AddAssetAsync("ra.png");
        var b = await AddAssetAsync("rb.png");
        await _catalog.AssignTagAsync(a.Id, tag.Id, TagSource.Manual);
        await _catalog.AssignTagAsync(b.Id, tag.Id, TagSource.Prompt);

        var result = await _ops.RemoveTagAsync(
            [(a.Id, TagSource.Manual), (b.Id, TagSource.Prompt)],
            tag.Id,
            tag.Name,
            _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.DoesNotContain(tag.Id, await TagIdsOn(a.Id));
        Assert.DoesNotContain(tag.Id, await TagIdsOn(b.Id));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(TagSource.Manual, await SourceOf(a.Id, tag.Id));
        Assert.Equal(TagSource.Prompt, await SourceOf(b.Id, tag.Id));
    }

    [Fact]
    public async Task MixedPaste_OneBatch_AliasesToIdeal_UndoClearsAll()
    {
        var doggy = await _catalog.CreateTagAsync("doggy-paste");
        var dog = await _catalog.CreateTagAsync("dog-paste");
        var wolf = await _catalog.CreateTagAsync("wolf-paste");
        Assert.True((await _catalog.AddAliasAsync(doggy.Id, dog.Id)).Ok);

        var a = await AddAssetAsync("p1.png");
        var b = await AddAssetAsync("p2.png");

        var result = await _ops.AssignTagsAsync(
            [a.Id, b.Id],
            [
                (doggy.Id, doggy.Name, TagSource.Manual),
                (wolf.Id, wolf.Name, TagSource.Manual),
                (doggy.Id, doggy.Name, TagSource.Implied) // ignored
            ],
            _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.False(string.IsNullOrEmpty(result.BatchId));

        // Alias rewritten to ideal; Implied paste row ignored.
        Assert.Contains(dog.Id, await TagIdsOn(a.Id));
        Assert.DoesNotContain(doggy.Id, await TagIdsOn(a.Id));
        Assert.Contains(wolf.Id, await TagIdsOn(a.Id));
        Assert.Contains(dog.Id, await TagIdsOn(b.Id));
        Assert.Contains(wolf.Id, await TagIdsOn(b.Id));

        // One undo batch undoes the whole paste.
        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Empty(await TagIdsOn(a.Id));
        Assert.Empty(await TagIdsOn(b.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task AssignTags_NeverJournalsImpliedSourceAlone()
    {
        var tag = await _catalog.CreateTagAsync("impl-only");
        var asset = await AddAssetAsync("impl.png");
        var result = await _ops.AssignTagsAsync(
            [asset.Id],
            [(tag.Id, tag.Name, TagSource.Implied)],
            _projectId);
        Assert.False(result.Ok);
        Assert.Empty(await TagIdsOn(asset.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task PurgeBackoff_SkipsAfterFtsFailureMark()
    {
        var fixtureDir = Path.Combine(_dir, "backoff");
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
            var parent = await catalog.CreateTagAsync("bo-p");
            var child = await catalog.CreateTagAsync("bo-c");
            childId = child.Id;
            var path = Path.Combine(source.Path, "bo.png");
            await File.WriteAllTextAsync(path, "x");
            var asset = new Asset
            {
                Id = PalaceDb.NewId(),
                SourceFolderId = source.Id,
                Path = path,
                FileName = "bo.png",
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

        // Simulate a prior FTS/delete failure backoff marker.
        var backoff = Path.Combine(fixtureDir, "catalog", "implied-purge-backoff.txt");
        await File.WriteAllTextAsync(backoff, DateTimeOffset.UtcNow.ToString("o"));

        SqliteConnection.ClearAllPools();
        using var blocked = new PalaceDb(fixtureDb);
        Assert.Equal(0, blocked.ImpliedRowsPurged);
        Assert.Contains("backoff", blocked.ImpliedPurgeSkipReason ?? "", StringComparison.OrdinalIgnoreCase);
        await blocked.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM AssetTag WHERE Source = 'Implied'";
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
            return 0;
        });
    }

    [Fact]
    public void ParentExpand_ReportsTruncation()
    {
        var emptyAlias = new Dictionary<string, string>(StringComparer.Ordinal);
        var idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["parent"] = "p" };
        var nameById = new Dictionary<string, string>(StringComparer.Ordinal) { ["p"] = "parent" };
        var implying = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var children = new List<string>();
        for (var i = 0; i < TagFtsQuery.MaxParentExpandOrTerms + 5; i++)
        {
            var id = $"c{i}";
            var name = $"child{i:D3}";
            children.Add(id);
            nameById[id] = name;
            idByName[name] = id;
        }

        implying["p"] = children;
        var q = TagFtsQuery.Build("parent", emptyAlias, idByName, nameById, implying, out var truncated);
        Assert.True(truncated);
        Assert.Contains("\"parent\"*", q);
    }

    [Fact]
    public async Task CatalogMeta_TableDropped()
    {
        await _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CatalogMeta'";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
            return 0;
        });
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

    private Task<List<string>> TagIdsOn(string assetId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TagId FROM AssetTag WHERE AssetId = $id";
            cmd.Parameters.AddWithValue("$id", assetId);
            var ids = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }

            return ids;
        });

    private async Task<TagSource?> SourceOf(string assetId, string tagId)
    {
        var map = await _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TagId, Source FROM AssetTag WHERE AssetId = $id";
            cmd.Parameters.AddWithValue("$id", assetId);
            var result = new Dictionary<string, TagSource>(StringComparer.Ordinal);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result[reader.GetString(0)] = Enum.Parse<TagSource>(reader.GetString(1));
            }

            return result;
        });
        return map.TryGetValue(tagId, out var s) ? s : null;
    }
}
