using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Source-only tag upserts (Prompt→Manual, leftover Implied→Manual) must journal
/// and Undo without rematerializing Implied.
/// </summary>
public sealed class TagSourceOnlyUndoTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-src-undo-" + Guid.NewGuid().ToString("N"));
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
    public async Task SourceOnly_PromptToManual_JournalsAndUndoes()
    {
        var tag = await _catalog.CreateTagAsync("src-up");
        var asset = await AddAssetAsync("up.png");
        await _catalog.AssignTagAsync(asset.Id, tag.Id, TagSource.Prompt);
        Assert.Equal(TagSource.Prompt, await SourceOf(asset.Id, tag.Id));

        var result = await _ops.AssignTagAsync(
            [asset.Id], tag.Id, tag.Name, TagSource.Manual, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.False(string.IsNullOrEmpty(result.BatchId));
        Assert.Equal(TagSource.Manual, await SourceOf(asset.Id, tag.Id));

        var peek = await _ops.PeekUndoAsync(_projectId);
        Assert.NotNull(peek);
        Assert.Equal(FileOpKind.TagAssign, peek!.Kind);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(TagSource.Prompt, await SourceOf(asset.Id, tag.Id));
    }

    [Fact]
    public async Task SourceOnly_ImpliedToManual_Journals_UndoDeletes_NoImpliedRematerialized()
    {
        var tag = await _catalog.CreateTagAsync("imp-up");
        var asset = await AddAssetAsync("imp.png");
        await _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO AssetTag (AssetId, TagId, Source)
                VALUES ($a, $t, $s)
                """;
            cmd.Parameters.AddWithValue("$a", asset.Id);
            cmd.Parameters.AddWithValue("$t", tag.Id);
            cmd.Parameters.AddWithValue("$s", TagSource.Implied.ToString());
            cmd.ExecuteNonQuery();
        });

        var result = await _ops.AssignTagAsync(
            [asset.Id], tag.Id, tag.Name, TagSource.Manual, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.False(string.IsNullOrEmpty(result.BatchId));
        Assert.Equal(TagSource.Manual, await SourceOf(asset.Id, tag.Id));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Null(await SourceOf(asset.Id, tag.Id));
    }

    [Fact]
    public async Task Assign_DoesNotJournalImpliedSource()
    {
        var tag = await _catalog.CreateTagAsync("no-imp");
        var asset = await AddAssetAsync("ni.png");

        var result = await _ops.AssignTagsAsync(
            [asset.Id],
            [(tag.Id, tag.Name, TagSource.Implied)],
            _projectId);

        Assert.False(result.Ok);
        Assert.Null(await SourceOf(asset.Id, tag.Id));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task FreshAssign_StillJournalsAndUndoes()
    {
        var tag = await _catalog.CreateTagAsync("fresh");
        var asset = await AddAssetAsync("fr.png");

        var result = await _ops.AssignTagAsync(
            [asset.Id], tag.Id, tag.Name, TagSource.Manual, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(TagSource.Manual, await SourceOf(asset.Id, tag.Id));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Null(await SourceOf(asset.Id, tag.Id));
    }

    [Fact]
    public async Task SameSourceReassign_IsNoOpWithoutJournal()
    {
        var tag = await _catalog.CreateTagAsync("same");
        var asset = await AddAssetAsync("sm.png");
        await _catalog.AssignTagAsync(asset.Id, tag.Id, TagSource.Manual);

        var result = await _ops.AssignTagAsync(
            [asset.Id], tag.Id, tag.Name, TagSource.Manual, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.True(string.IsNullOrEmpty(result.BatchId));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
        Assert.Equal(TagSource.Manual, await SourceOf(asset.Id, tag.Id));
    }

    [Fact]
    public async Task SourceOnly_ManualToPrompt_JournalsAndUndoes()
    {
        var tag = await _catalog.CreateTagAsync("man-prompt");
        var asset = await AddAssetAsync("mp.png");
        await _catalog.AssignTagAsync(asset.Id, tag.Id, TagSource.Manual);

        var result = await _ops.AssignTagAsync(
            [asset.Id], tag.Id, tag.Name, TagSource.Prompt, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.False(string.IsNullOrEmpty(result.BatchId));
        Assert.Equal(TagSource.Prompt, await SourceOf(asset.Id, tag.Id));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(TagSource.Manual, await SourceOf(asset.Id, tag.Id));
    }

    [Fact]
    public async Task KeepAssigned_Undo_StripsImpliedOnly_KeepsAssignedSource()
    {
        var parent = await _catalog.CreateTagAsync("keep-parent");
        var winter = await _catalog.CreateTagAsync("keep-winter");
        var asset = await AddAssetAsync("ki.png");
        await _catalog.AssignTagAsync(asset.Id, parent.Id, TagSource.Manual);
        await _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO AssetTag (AssetId, TagId, Source)
                VALUES ($a, $t, $s)
                """;
            cmd.Parameters.AddWithValue("$a", asset.Id);
            cmd.Parameters.AddWithValue("$t", winter.Id);
            cmd.Parameters.AddWithValue("$s", TagSource.Implied.ToString());
            cmd.ExecuteNonQuery();
        });

        // Simulate a KeepAssigned journal: assigned tag stayed Manual; Implied winter was added.
        await _catalog.AddFileOpBatchAsync(new FileOpBatch
        {
            ProjectId = _projectId,
            Kind = FileOpKind.TagAssign,
            Summary = "Tagged 1 item with keep-parent",
            Items =
            [
                new FileOpItem
                {
                    Step = FileOpStep.AssignTag,
                    AssetId = asset.Id,
                    OldPath = parent.Id,
                    NewPath = parent.Name,
                    OldSourceId = TagSource.Manual.ToString(),
                    NewSourceId = TagAssignUndo.Encode([winter.Id], touchAssignedTag: false)
                }
            ]
        });

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(TagSource.Manual, await SourceOf(asset.Id, parent.Id));
        Assert.Null(await SourceOf(asset.Id, winter.Id));
    }

    [Fact]
    public async Task Bulk_MultiAsset_SourceOnly_Journals_Undoes_AndRedo()
    {
        var tag = await _catalog.CreateTagAsync("bulk-src");
        var a = await AddAssetAsync("bsa.png");
        var b = await AddAssetAsync("bsb.png");
        var c = await AddAssetAsync("bsc.png");
        await _catalog.AssignTagAsync(a.Id, tag.Id, TagSource.Prompt);
        await _catalog.AssignTagAsync(b.Id, tag.Id, TagSource.Prompt);
        await _catalog.AssignTagAsync(c.Id, tag.Id, TagSource.Manual);

        var result = await _ops.AssignTagAsync(
            [a.Id, b.Id, c.Id], tag.Id, tag.Name, TagSource.Manual, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.False(string.IsNullOrEmpty(result.BatchId));
        Assert.Equal(TagSource.Manual, await SourceOf(a.Id, tag.Id));
        Assert.Equal(TagSource.Manual, await SourceOf(b.Id, tag.Id));
        Assert.Equal(TagSource.Manual, await SourceOf(c.Id, tag.Id));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(TagSource.Prompt, await SourceOf(a.Id, tag.Id));
        Assert.Equal(TagSource.Prompt, await SourceOf(b.Id, tag.Id));
        // c was already Manual — not journaled; stays Manual.
        Assert.Equal(TagSource.Manual, await SourceOf(c.Id, tag.Id));

        var redo = await _ops.AssignTagAsync(
            [a.Id, b.Id, c.Id], tag.Id, tag.Name, TagSource.Manual, _projectId);
        Assert.True(redo.Ok, redo.Message);
        Assert.Equal(TagSource.Manual, await SourceOf(a.Id, tag.Id));
        Assert.Equal(TagSource.Manual, await SourceOf(b.Id, tag.Id));
        Assert.Equal(TagSource.Manual, await SourceOf(c.Id, tag.Id));
    }

    [Fact]
    public async Task SourceOnly_PromptToManual_UndoThenRedo()
    {
        var tag = await _catalog.CreateTagAsync("redo-src");
        var asset = await AddAssetAsync("rd.png");
        await _catalog.AssignTagAsync(asset.Id, tag.Id, TagSource.Prompt);

        var first = await _ops.AssignTagAsync(
            [asset.Id], tag.Id, tag.Name, TagSource.Manual, _projectId);
        Assert.True(first.Ok, first.Message);
        Assert.Equal(TagSource.Manual, await SourceOf(asset.Id, tag.Id));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(TagSource.Prompt, await SourceOf(asset.Id, tag.Id));

        var redo = await _ops.AssignTagAsync(
            [asset.Id], tag.Id, tag.Name, TagSource.Manual, _projectId);
        Assert.True(redo.Ok, redo.Message);
        Assert.Equal(TagSource.Manual, await SourceOf(asset.Id, tag.Id));
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
