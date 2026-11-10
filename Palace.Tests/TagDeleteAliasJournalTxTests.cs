using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Delete-tag / AddAlias mutate + FileOp journal share one SQLite transaction
/// (same failure-safety pattern as bulk assign/remove).
/// </summary>
public sealed class TagDeleteAliasJournalTxTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-del-alias-tx-" + Guid.NewGuid().ToString("N"));
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
    public async Task DeleteTag_BeforeJournalFault_LeavesTagAndNoJournal()
    {
        var tag = await _catalog.CreateTagAsync("keep-me");
        var asset = await AddAssetAsync("keep.png");
        await _catalog.AssignTagAsync(asset.Id, tag.Id, TagSource.Manual);

        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailBeforeJournal = true };
        var result = await _ops.DeleteTagAsync(tag.Id, _projectId);

        Assert.False(result.Ok);
        Assert.Equal(CatalogService.BulkTagAbortedMessage, result.Message);
        Assert.NotNull(await _catalog.GetTagAsync(tag.Id));
        Assert.Contains(tag.Id, await TagIdsOn(asset.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
        Assert.Equal(0, await CountFileOpBatchesAsync());
    }

    [Fact]
    public async Task DeleteTag_MidJournalFault_RollsBackDeleteAndJournal()
    {
        var tag = await _catalog.CreateTagAsync("jrnl-del");
        var asset = await AddAssetAsync("jd.png");
        await _catalog.AssignTagAsync(asset.Id, tag.Id, TagSource.Manual);

        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailAfterJournalItems = 0 };
        var result = await _ops.DeleteTagAsync(tag.Id, _projectId);

        Assert.False(result.Ok);
        Assert.Equal(CatalogService.BulkTagAbortedMessage, result.Message);
        Assert.NotNull(await _catalog.GetTagAsync(tag.Id));
        Assert.Contains(tag.Id, await TagIdsOn(asset.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
        Assert.Equal(0, await CountFileOpBatchesAsync());
        Assert.Equal(0, await CountFileOpItemsAsync());
    }

    [Fact]
    public async Task DeleteTag_Success_UndoRestores()
    {
        var tag = await _catalog.CreateTagAsync("gone");
        var asset = await AddAssetAsync("gone.png");
        await _catalog.AssignTagAsync(asset.Id, tag.Id, TagSource.Manual);

        var result = await _ops.DeleteTagAsync(tag.Id, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.Null(await _catalog.GetTagAsync(tag.Id));
        Assert.DoesNotContain(tag.Id, await TagIdsOn(asset.Id));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.NotNull(await _catalog.GetTagAsync(tag.Id));
        Assert.Contains(tag.Id, await TagIdsOn(asset.Id));
    }

    [Fact]
    public async Task AddAlias_BeforeJournalFault_LeavesNoAliasAndNoJournal()
    {
        var doggy = await _catalog.CreateTagAsync("fault-doggy");
        var dog = await _catalog.CreateTagAsync("fault-dog");
        var asset = await AddAssetAsync("pet.png");
        await _catalog.AssignTagAsync(asset.Id, doggy.Id, TagSource.Manual);

        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailBeforeJournal = true };
        var result = await _ops.AddAliasAsync(doggy.Id, dog.Id, _projectId);

        Assert.False(result.Ok);
        Assert.Equal(CatalogService.BulkTagAbortedMessage, result.Message);
        Assert.Equal(doggy.Id, await _catalog.ResolveIdealTagIdAsync(doggy.Id));
        Assert.Contains(doggy.Id, await TagIdsOn(asset.Id));
        Assert.DoesNotContain(dog.Id, await TagIdsOn(asset.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
        Assert.Equal(0, await CountFileOpBatchesAsync());
    }

    [Fact]
    public async Task AddAlias_MidJournalFault_RollsBackMigrateAndJournal()
    {
        var doggy = await _catalog.CreateTagAsync("mj-doggy");
        var dog = await _catalog.CreateTagAsync("mj-dog");
        var asset = await AddAssetAsync("mj.png");
        await _catalog.AssignTagAsync(asset.Id, doggy.Id, TagSource.Manual);

        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailAfterJournalItems = 0 };
        var result = await _ops.AddAliasAsync(doggy.Id, dog.Id, _projectId);

        Assert.False(result.Ok);
        Assert.Equal(CatalogService.BulkTagAbortedMessage, result.Message);
        Assert.Equal(doggy.Id, await _catalog.ResolveIdealTagIdAsync(doggy.Id));
        Assert.Contains(doggy.Id, await TagIdsOn(asset.Id));
        Assert.DoesNotContain(dog.Id, await TagIdsOn(asset.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
        Assert.Equal(0, await CountFileOpBatchesAsync());
        Assert.Equal(0, await CountFileOpItemsAsync());
    }

    [Fact]
    public async Task AddAlias_NoMigration_CommitsWithoutJournal()
    {
        var a = await _catalog.CreateTagAsync("lonely-a");
        var b = await _catalog.CreateTagAsync("lonely-b");

        var result = await _ops.AddAliasAsync(a.Id, b.Id, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(b.Id, await _catalog.ResolveIdealTagIdAsync(a.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
        Assert.Equal(0, await CountFileOpBatchesAsync());
    }

    [Fact]
    public async Task AddAlias_WithMigration_UndoRestores()
    {
        var doggy = await _catalog.CreateTagAsync("ok-doggy");
        var dog = await _catalog.CreateTagAsync("ok-dog");
        var asset = await AddAssetAsync("ok.png");
        await _catalog.AssignTagAsync(asset.Id, doggy.Id, TagSource.Manual);

        var result = await _ops.AddAliasAsync(doggy.Id, dog.Id, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.DoesNotContain(doggy.Id, await TagIdsOn(asset.Id));
        Assert.Contains(dog.Id, await TagIdsOn(asset.Id));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Contains(doggy.Id, await TagIdsOn(asset.Id));
        Assert.DoesNotContain(dog.Id, await TagIdsOn(asset.Id));
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

    private Task<long> CountFileOpBatchesAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM FileOpBatch";
            return Convert.ToInt64(cmd.ExecuteScalar());
        });

    private Task<long> CountFileOpItemsAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM FileOpItem";
            return Convert.ToInt64(cmd.ExecuteScalar());
        });
}
