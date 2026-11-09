using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Bulk tag assign/remove/paste must not leave unjournaled partials when a mid-loop
/// or journal fault aborts. Mutates + FileOp journal share one SQLite transaction.
/// </summary>
public sealed class TagBulkFailureSafetyTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-bulk-fail-" + Guid.NewGuid().ToString("N"));
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
    public async Task Assign_EarlyFault_LeavesNoTagsAndNoJournal()
    {
        var tag = await _catalog.CreateTagAsync("early-a");
        var a = await AddAssetAsync("ea.png");
        var b = await AddAssetAsync("eb.png");

        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailAfterMutations = 1 };
        var result = await _ops.AssignTagsAsync(
            [a.Id, b.Id],
            [(tag.Id, tag.Name, TagSource.Manual)],
            _projectId);

        Assert.False(result.Ok);
        Assert.Equal(CatalogService.BulkTagAbortedMessage, result.Message);
        Assert.DoesNotContain(tag.Id, await TagIdsOn(a.Id));
        Assert.DoesNotContain(tag.Id, await TagIdsOn(b.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Assign_MiddleFault_LeavesNoPartialTags()
    {
        var tag = await _catalog.CreateTagAsync("mid-a");
        var a = await AddAssetAsync("ma.png");
        var b = await AddAssetAsync("mb.png");
        var c = await AddAssetAsync("mc.png");

        // First mutation succeeds inside the tx; second faults → full rollback.
        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailAfterMutations = 2 };
        var result = await _ops.AssignTagAsync(
            [a.Id, b.Id, c.Id], tag.Id, tag.Name, TagSource.Manual, _projectId);

        Assert.False(result.Ok);
        Assert.Empty(await TagIdsOn(a.Id));
        Assert.Empty(await TagIdsOn(b.Id));
        Assert.Empty(await TagIdsOn(c.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Assign_MidJournalFault_RollsBackTagsAndJournal()
    {
        var tag = await _catalog.CreateTagAsync("jrnl-a");
        var a = await AddAssetAsync("ja.png");
        var b = await AddAssetAsync("jb.png");
        var c = await AddAssetAsync("jc.png");

        // Header + first FileOpItem land inside the open tx, then fault — both tags and
        // journal rows must disappear on rollback (not merely "before InsertFileOpBatch").
        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailAfterJournalItems = 1 };
        var result = await _ops.AssignTagsAsync(
            [a.Id, b.Id, c.Id],
            [(tag.Id, tag.Name, TagSource.Manual)],
            _projectId);

        Assert.False(result.Ok);
        Assert.Equal(CatalogService.BulkTagAbortedMessage, result.Message);
        Assert.DoesNotContain(tag.Id, await TagIdsOn(a.Id));
        Assert.DoesNotContain(tag.Id, await TagIdsOn(b.Id));
        Assert.DoesNotContain(tag.Id, await TagIdsOn(c.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
        Assert.Equal(0, await CountFileOpBatchesAsync());
        Assert.Equal(0, await CountFileOpItemsAsync());
    }

    [Fact]
    public async Task Assign_JournalHeaderOnlyFault_RollsBackTagsAndJournal()
    {
        var tag = await _catalog.CreateTagAsync("jrnl-hdr");
        var a = await AddAssetAsync("jha.png");
        var b = await AddAssetAsync("jhb.png");

        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailAfterJournalItems = 0 };
        var result = await _ops.AssignTagsAsync(
            [a.Id, b.Id],
            [(tag.Id, tag.Name, TagSource.Manual)],
            _projectId);

        Assert.False(result.Ok);
        Assert.Empty(await TagIdsOn(a.Id));
        Assert.Empty(await TagIdsOn(b.Id));
        Assert.Equal(0, await CountFileOpBatchesAsync());
        Assert.Equal(0, await CountFileOpItemsAsync());
    }

    [Fact]
    public async Task Assign_BeforeJournalFault_RollsBackMutates()
    {
        var tag = await _catalog.CreateTagAsync("prej-a");
        var a = await AddAssetAsync("pa.png");
        var b = await AddAssetAsync("pb.png");

        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailBeforeJournal = true };
        var result = await _ops.AssignTagsAsync(
            [a.Id, b.Id],
            [(tag.Id, tag.Name, TagSource.Manual)],
            _projectId);

        Assert.False(result.Ok);
        Assert.Empty(await TagIdsOn(a.Id));
        Assert.Empty(await TagIdsOn(b.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Remove_MiddleFault_LeavesTagsIntact()
    {
        var tag = await _catalog.CreateTagAsync("mid-rm");
        var a = await AddAssetAsync("rma.png");
        var b = await AddAssetAsync("rmb.png");
        var c = await AddAssetAsync("rmc.png");
        await _catalog.AssignTagAsync(a.Id, tag.Id, TagSource.Manual);
        await _catalog.AssignTagAsync(b.Id, tag.Id, TagSource.Prompt);
        await _catalog.AssignTagAsync(c.Id, tag.Id, TagSource.Manual);

        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailAfterMutations = 2 };
        var result = await _ops.RemoveTagsAsync(
            [a.Id, b.Id, c.Id],
            [(tag.Id, tag.Name)],
            _projectId);

        Assert.False(result.Ok);
        Assert.Equal(TagSource.Manual, await SourceOf(a.Id, tag.Id));
        Assert.Equal(TagSource.Prompt, await SourceOf(b.Id, tag.Id));
        Assert.Equal(TagSource.Manual, await SourceOf(c.Id, tag.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Paste_MiddleFault_RollsBackAllTagsOnAllAssets()
    {
        var dog = await _catalog.CreateTagAsync("paste-dog");
        var wolf = await _catalog.CreateTagAsync("paste-wolf");
        var a = await AddAssetAsync("pfa.png");
        var b = await AddAssetAsync("pfb.png");

        // 2 assets × first tag = 2 mutates; fault on first mutate of second tag.
        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailAfterMutations = 3 };
        var result = await _ops.AssignTagsAsync(
            [a.Id, b.Id],
            [
                (dog.Id, dog.Name, TagSource.Manual),
                (wolf.Id, wolf.Name, TagSource.Manual)
            ],
            _projectId);

        Assert.False(result.Ok);
        Assert.Empty(await TagIdsOn(a.Id));
        Assert.Empty(await TagIdsOn(b.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task SuccessfulAssign_UndoThenRedoRestoresManualOnCanonical()
    {
        // Palace has no FileOp Redo stack; redo = re-apply the same bulk assign after Undo.
        var doggy = await _catalog.CreateTagAsync("redo-doggy");
        var dog = await _catalog.CreateTagAsync("redo-dog");
        Assert.True((await _catalog.AddAliasAsync(doggy.Id, dog.Id)).Ok);
        var a = await AddAssetAsync("oka.png");
        var b = await AddAssetAsync("okb.png");

        var paste = new (string TagId, string TagName, TagSource Source)[]
        {
            (doggy.Id, doggy.Name, TagSource.Manual)
        };
        var result = await _ops.AssignTagsAsync([a.Id, b.Id], paste, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.False(string.IsNullOrEmpty(result.BatchId));
        Assert.Equal(TagSource.Manual, await SourceOf(a.Id, dog.Id));
        Assert.Equal(TagSource.Manual, await SourceOf(b.Id, dog.Id));
        Assert.DoesNotContain(doggy.Id, await TagIdsOn(a.Id));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.DoesNotContain(dog.Id, await TagIdsOn(a.Id));
        Assert.DoesNotContain(dog.Id, await TagIdsOn(b.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));

        var redo = await _ops.AssignTagsAsync([a.Id, b.Id], paste, _projectId);
        Assert.True(redo.Ok, redo.Message);
        Assert.False(string.IsNullOrEmpty(redo.BatchId));
        Assert.Equal(TagSource.Manual, await SourceOf(a.Id, dog.Id));
        Assert.Equal(TagSource.Manual, await SourceOf(b.Id, dog.Id));
        Assert.DoesNotContain(doggy.Id, await TagIdsOn(a.Id));
        Assert.DoesNotContain(doggy.Id, await TagIdsOn(b.Id));

        var undoRedo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undoRedo.Ok, undoRedo.Message);
        Assert.Empty(await TagIdsOn(a.Id));
        Assert.Empty(await TagIdsOn(b.Id));
    }

    [Fact]
    public async Task SuccessfulRemove_UndoRestoresSources_RedoRemoveClearsAgain()
    {
        var tag = await _catalog.CreateTagAsync("ok-rm");
        var a = await AddAssetAsync("okra.png");
        var b = await AddAssetAsync("okrb.png");
        await _catalog.AssignTagAsync(a.Id, tag.Id, TagSource.Manual);
        await _catalog.AssignTagAsync(b.Id, tag.Id, TagSource.Prompt);

        var rows = new (string AssetId, TagSource Source)[]
        {
            (a.Id, TagSource.Manual),
            (b.Id, TagSource.Prompt)
        };
        var result = await _ops.RemoveTagAsync(rows, tag.Id, tag.Name, _projectId);
        Assert.True(result.Ok, result.Message);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(TagSource.Manual, await SourceOf(a.Id, tag.Id));
        Assert.Equal(TagSource.Prompt, await SourceOf(b.Id, tag.Id));

        var redoRemove = await _ops.RemoveTagAsync(rows, tag.Id, tag.Name, _projectId);
        Assert.True(redoRemove.Ok, redoRemove.Message);
        Assert.DoesNotContain(tag.Id, await TagIdsOn(a.Id));
        Assert.DoesNotContain(tag.Id, await TagIdsOn(b.Id));

        var undoRedo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undoRedo.Ok, undoRedo.Message);
        Assert.Equal(TagSource.Manual, await SourceOf(a.Id, tag.Id));
        Assert.Equal(TagSource.Prompt, await SourceOf(b.Id, tag.Id));
    }

    [Fact]
    public async Task SuccessfulPaste_AliasIdeal_UndoThenRedoRestoresCanonicalOnly()
    {
        var doggy = await _catalog.CreateTagAsync("failsafe-doggy");
        var dog = await _catalog.CreateTagAsync("failsafe-dog");
        var wolf = await _catalog.CreateTagAsync("failsafe-wolf");
        Assert.True((await _catalog.AddAliasAsync(doggy.Id, dog.Id)).Ok);

        var a = await AddAssetAsync("fsp1.png");
        var b = await AddAssetAsync("fsp2.png");
        var paste = new (string TagId, string TagName, TagSource Source)[]
        {
            (doggy.Id, doggy.Name, TagSource.Manual),
            (wolf.Id, wolf.Name, TagSource.Manual),
            (doggy.Id, doggy.Name, TagSource.Implied)
        };

        var result = await _ops.AssignTagsAsync([a.Id, b.Id], paste, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.Contains(dog.Id, await TagIdsOn(a.Id));
        Assert.DoesNotContain(doggy.Id, await TagIdsOn(a.Id));
        Assert.Contains(wolf.Id, await TagIdsOn(b.Id));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Empty(await TagIdsOn(a.Id));
        Assert.Empty(await TagIdsOn(b.Id));

        var redo = await _ops.AssignTagsAsync([a.Id, b.Id], paste, _projectId);
        Assert.True(redo.Ok, redo.Message);
        Assert.Equal(TagSource.Manual, await SourceOf(a.Id, dog.Id));
        Assert.Equal(TagSource.Manual, await SourceOf(a.Id, wolf.Id));
        Assert.Equal(TagSource.Manual, await SourceOf(b.Id, dog.Id));
        Assert.Equal(TagSource.Manual, await SourceOf(b.Id, wolf.Id));
        Assert.DoesNotContain(doggy.Id, await TagIdsOn(a.Id));
        Assert.DoesNotContain(doggy.Id, await TagIdsOn(b.Id));
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
