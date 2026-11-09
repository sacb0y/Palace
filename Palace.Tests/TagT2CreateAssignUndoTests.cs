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
