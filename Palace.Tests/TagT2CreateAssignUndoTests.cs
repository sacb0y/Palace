using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// T2 Doktor: quick-create via GetOrCreate is journaled; Undo of apply removes unused Tag rows.
/// Mirrors LibraryViewModel.ApplyPendingTagsAsync (create → AssignTagsAsync → Undo).
/// </summary>
public sealed class TagT2CreateAssignUndoTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-t2-create-" + Guid.NewGuid().ToString("N"));
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
    public async Task QuickCreate_Assign_Undo_RemovesTagRow()
    {
        var asset = await AddAssetAsync("new.png");
        var (tag, created) = await _catalog.GetOrCreateTagResultAsync("value study");
        Assert.True(created);

        var result = await _ops.AssignTagsAsync(
            [asset.Id],
            [(tag.Id, tag.Name, TagSource.Manual)],
            _projectId,
            createdTags: [(tag.Id, tag.Name)]);
        Assert.True(result.Ok, result.Message);

        var peek = await _ops.PeekUndoAsync(_projectId);
        Assert.NotNull(peek);
        Assert.Equal(FileOpKind.TagAssign, peek!.Kind);
        Assert.Contains(peek.Items, i => i.Step == FileOpStep.CreateTag && i.OldPath == tag.Id);
        Assert.Contains(peek.Items, i => i.Step == FileOpStep.AssignTag && i.OldPath == tag.Id);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Empty(await TagIdsOn(asset.Id));
        Assert.Null(await _catalog.FindTagByNameAsync("value study"));
    }

    [Fact]
    public async Task QuickCreate_Assign_Undo_ThenFileOpRedo_RestoresSameTagId()
    {
        // Doktor #157 / P-60: true journal Redo — not re-calling AssignTagAsync.
        var asset = await AddAssetAsync("redo.png");
        var (tag, created) = await _catalog.GetOrCreateTagResultAsync("gouache study");
        Assert.True(created);
        var tagId = tag.Id;

        var result = await _ops.AssignTagsAsync(
            [asset.Id],
            [(tag.Id, tag.Name, TagSource.Manual)],
            _projectId,
            createdTags: [(tag.Id, tag.Name)]);
        Assert.True(result.Ok, result.Message);
        Assert.False(string.IsNullOrEmpty(result.BatchId));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(result.BatchId, undo.BatchId);
        Assert.Empty(await TagIdsOn(asset.Id));
        Assert.Null(await _catalog.FindTagByNameAsync("gouache study"));
        Assert.NotNull(await _ops.PeekRedoAsync(_projectId));

        var redo = await _ops.RedoLastAsync(_projectId);
        Assert.True(redo.Ok, redo.Message);
        Assert.Equal(result.BatchId, redo.BatchId);
        Assert.Contains(tagId, await TagIdsOn(asset.Id));
        var restored = await _catalog.FindTagByNameAsync("gouache study");
        Assert.NotNull(restored);
        Assert.Equal(tagId, restored!.Id);
        Assert.Null(await _ops.PeekRedoAsync(_projectId));
        Assert.NotNull(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task PreexistingTag_NoCreateTagStep()
    {
        var existing = await _catalog.CreateTagAsync("sketch");
        var asset = await AddAssetAsync("pre.png");

        var (tag, created) = await _catalog.GetOrCreateTagResultAsync("sketch");
        Assert.False(created);
        Assert.Equal(existing.Id, tag.Id);

        var result = await _ops.AssignTagsAsync(
            [asset.Id],
            [(tag.Id, tag.Name, TagSource.Manual)],
            _projectId,
            createdTags: null);
        Assert.True(result.Ok, result.Message);

        var peek = await _ops.PeekUndoAsync(_projectId);
        Assert.NotNull(peek);
        Assert.DoesNotContain(peek!.Items, i => i.Step == FileOpStep.CreateTag);

        await _ops.UndoLastAsync(_projectId);
        Assert.NotNull(await _catalog.FindTagByNameAsync("sketch"));
    }

    [Fact]
    public async Task AliasResolve_AssignsIdeal_NoCreate()
    {
        var ideal = await _catalog.CreateTagAsync("landscape");
        var alias = await _catalog.CreateTagAsync("scenery");
        await _catalog.AddAliasAsync(alias.Id, ideal.Id);

        var asset = await AddAssetAsync("alias.png");
        var (resolved, created) = await _catalog.GetOrCreateTagResultAsync("scenery");
        Assert.False(created);
        Assert.Equal(ideal.Id, resolved.Id);

        var result = await _ops.AssignTagsAsync(
            [asset.Id],
            [(resolved.Id, resolved.Name, TagSource.Manual)],
            _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.Contains(ideal.Id, await TagIdsOn(asset.Id));
        Assert.DoesNotContain(alias.Id, await TagIdsOn(asset.Id));
    }

    [Fact]
    public async Task MixedPaste_CreateAndExisting_OneBatch_UndoDropsCreatedOnly()
    {
        var existing = await _catalog.CreateTagAsync("wip");
        var asset = await AddAssetAsync("mix.png");

        var (createdTag, wasCreated) = await _catalog.GetOrCreateTagResultAsync("gouache");
        Assert.True(wasCreated);
        var (existingAgain, createdAgain) = await _catalog.GetOrCreateTagResultAsync("wip");
        Assert.False(createdAgain);

        var result = await _ops.AssignTagsAsync(
            [asset.Id],
            [
                (createdTag.Id, createdTag.Name, TagSource.Manual),
                (existingAgain.Id, existingAgain.Name, TagSource.Manual)
            ],
            _projectId,
            createdTags: [(createdTag.Id, createdTag.Name)]);
        Assert.True(result.Ok, result.Message);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Empty(await TagIdsOn(asset.Id));
        Assert.Null(await _catalog.FindTagByNameAsync("gouache"));
        Assert.NotNull(await _catalog.FindTagByNameAsync("wip"));
        Assert.Equal(existing.Id, (await _catalog.FindTagByNameAsync("wip"))!.Id);
    }

    [Fact]
    public async Task CreateTagUndo_SkipsWhenStillOnAnotherAsset()
    {
        var a = await AddAssetAsync("keep-a.png");
        var b = await AddAssetAsync("keep-b.png");
        var (tag, created) = await _catalog.GetOrCreateTagResultAsync("shared");
        Assert.True(created);

        var result = await _ops.AssignTagsAsync(
            [a.Id],
            [(tag.Id, tag.Name, TagSource.Manual)],
            _projectId,
            createdTags: [(tag.Id, tag.Name)]);
        Assert.True(result.Ok, result.Message);

        // Second asset holds the tag outside the undo batch → CreateTag undo must keep the row.
        await _catalog.AssignTagAsync(b.Id, tag.Id, TagSource.Manual);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.DoesNotContain(tag.Id, await TagIdsOn(a.Id));
        Assert.Contains(tag.Id, await TagIdsOn(b.Id));
        Assert.NotNull(await _catalog.FindTagByNameAsync("shared"));
    }

    [Fact]
    public async Task CreateTagUndo_SkipsWhenMembershipRemains()
    {
        var parent = await _catalog.CreateTagAsync("Medium");
        var asset = await AddAssetAsync("mem.png");
        var (tag, created) = await _catalog.GetOrCreateTagResultAsync("charcoal");
        Assert.True(created);

        var result = await _ops.AssignTagsAsync(
            [asset.Id],
            [(tag.Id, tag.Name, TagSource.Manual)],
            _projectId,
            createdTags: [(tag.Id, tag.Name)]);
        Assert.True(result.Ok, result.Message);
        Assert.True(await _catalog.AddMembershipAsync(parent.Id, tag.Id));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Empty(await TagIdsOn(asset.Id));
        Assert.NotNull(await _catalog.FindTagByNameAsync("charcoal"));
        Assert.False(await _catalog.TryDeleteUnusedCreatedTagAsync(tag.Id));
    }

    [Fact]
    public async Task CreateTagUndo_SkipsWhenImplicationRemains()
    {
        var figure = await _catalog.CreateTagAsync("imp-figure");
        var asset = await AddAssetAsync("imp.png");
        var (tag, created) = await _catalog.GetOrCreateTagResultAsync("imp-character");
        Assert.True(created, "quick-create should insert a new Tag row");

        var result = await _ops.AssignTagsAsync(
            [asset.Id],
            [(tag.Id, tag.Name, TagSource.Manual)],
            _projectId,
            createdTags: [(tag.Id, tag.Name)]);
        Assert.True(result.Ok, result.Message);
        var implied = await _catalog.AddImplicationAsync(tag.Id, figure.Id);
        Assert.True(implied, "character → figure implication should insert");

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Empty(await TagIdsOn(asset.Id));
        Assert.NotNull(await _catalog.FindTagByNameAsync("imp-character"));
        Assert.False(await _catalog.TryDeleteUnusedCreatedTagAsync(tag.Id));
    }

    [Fact]
    public async Task CreateTagUndo_SkipsWhenAliasRemains()
    {
        var ideal = await _catalog.CreateTagAsync("landscape");
        var asset = await AddAssetAsync("als.png");
        var (tag, created) = await _catalog.GetOrCreateTagResultAsync("vista");
        Assert.True(created);

        var result = await _ops.AssignTagsAsync(
            [asset.Id],
            [(tag.Id, tag.Name, TagSource.Manual)],
            _projectId,
            createdTags: [(tag.Id, tag.Name)]);
        Assert.True(result.Ok, result.Message);
        Assert.True((await _catalog.AddAliasAsync(tag.Id, ideal.Id)).Ok);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Empty(await TagIdsOn(asset.Id));
        // Alias edge keeps the alias Tag row alive through CreateTag undo.
        Assert.NotNull(await _catalog.FindTagByNameAsync("vista"));
        Assert.False(await _catalog.TryDeleteUnusedCreatedTagAsync(tag.Id));
    }

    [Fact]
    public async Task TryDeleteUnusedCreatedTag_Atomic_FalseWhenUsed_TrueWhenFree()
    {
        var tag = await _catalog.CreateTagAsync("temp-free");
        var asset = await AddAssetAsync("tmp.png");
        await _catalog.AssignTagAsync(asset.Id, tag.Id, TagSource.Manual);
        Assert.False(await _catalog.TryDeleteUnusedCreatedTagAsync(tag.Id));
        Assert.NotNull(await _catalog.FindTagByNameAsync("temp-free"));

        await _catalog.RemoveTagAsync(asset.Id, tag.Id);
        Assert.True(await _catalog.TryDeleteUnusedCreatedTagAsync(tag.Id));
        Assert.Null(await _catalog.FindTagByNameAsync("temp-free"));
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
}
