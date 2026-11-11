using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>Alias manager (audit j): journaled add / remove / "Move it here" and their Undo.</summary>
public sealed class TagAliasManagerJournalTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-alias-mgr-" + Guid.NewGuid().ToString("N"));
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

    private async Task<Dictionary<string, string>> AliasMapAsync() =>
        (await _catalog.GetAliasesAsync()).ToDictionary(a => a.AliasTagId, a => a.IdealTagId, StringComparer.Ordinal);

    [Fact]
    public async Task AddAlias_withoutMigration_isStillJournaled_andUndoDropsTheEdge()
    {
        var brown = await _catalog.CreateTagAsync("brown hair");
        var brunette = await _catalog.CreateTagAsync("brunette");

        var add = await _ops.AddAliasAsync(brunette.Id, brown.Id, _projectId);
        Assert.True(add.Ok, add.Message);
        Assert.NotNull(add.BatchId);
        Assert.Equal(brown.Id, (await AliasMapAsync())[brunette.Id]);
        Assert.Equal(FileOpKind.TagAliasAdd, (await _ops.PeekUndoAsync(_projectId))!.Kind);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Empty(await AliasMapAsync());
        Assert.NotNull(await _catalog.GetTagAsync(brunette.Id));
    }

    [Fact]
    public async Task AddNewAliases_createsTagsAndEdges_andOneUndoRemovesBoth()
    {
        var brown = await _catalog.CreateTagAsync("brown hair");

        var add = await _ops.AddNewAliasesAsync(brown.Id, ["brunette", "chestnut hair", "Brown_Hair"], _projectId);
        Assert.True(add.Ok, add.Message);
        Assert.Equal("Added 2 aliases to brown hair", add.Summary);

        var map = await AliasMapAsync();
        Assert.Equal(2, map.Count);
        Assert.All(map.Values, ideal => Assert.Equal(brown.Id, ideal));
        var tags = await _catalog.GetTagsAsync();
        Assert.Contains(tags, t => t.Name == "brunette");
        Assert.Single(tags, t => TagNames.Same(t.Name, "brown hair"));

        var batches = await _catalog.GetUndoableFileOpsAsync(_projectId);
        Assert.Single(batches);
        Assert.Equal(FileOpKind.TagAliasAdd, batches[0].Kind);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Empty(await AliasMapAsync());
        var names = (await _catalog.GetTagsAsync()).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("brown hair", names);
        Assert.DoesNotContain("brunette", names);
        Assert.DoesNotContain("chestnut hair", names);
    }

    [Fact]
    public async Task AddNewAliases_refusesNamesThatAreAlreadyTags()
    {
        var brown = await _catalog.CreateTagAsync("brown hair");
        await _catalog.CreateTagAsync("dark hair");

        var add = await _ops.AddNewAliasesAsync(brown.Id, ["Dark_Hair", "brown-hair"], _projectId);
        Assert.False(add.Ok);
        Assert.Empty(await AliasMapAsync());
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task RemoveAlias_journals_andUndoRestoresTheEdge()
    {
        var brown = await _catalog.CreateTagAsync("brown hair");
        var brunette = await _catalog.CreateTagAsync("brunette");
        Assert.True((await _catalog.AddAliasAsync(brunette.Id, brown.Id)).Ok);

        var remove = await _ops.RemoveAliasAsync(brunette.Id, _projectId);
        Assert.True(remove.Ok, remove.Message);
        Assert.Equal("Removed alias brunette.", remove.Message);
        Assert.Empty(await AliasMapAsync());

        var peek = (await _ops.PeekUndoAsync(_projectId))!;
        Assert.Equal(FileOpKind.TagAliasRemove, peek.Kind);
        var item = Assert.Single(peek.Items);
        Assert.Equal(FileOpStep.RemoveAlias, item.Step);
        Assert.Equal(brunette.Id, item.OldPath);
        Assert.Equal(brown.Id, item.NewPath);
        Assert.Equal("Undo remove alias", FileOpService.UndoLabel(peek));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(brown.Id, (await AliasMapAsync())[brunette.Id]);
    }

    [Fact]
    public async Task RemoveAlias_failsWhenTheEdgeIsGone()
    {
        var tag = await _catalog.CreateTagAsync("orphan");
        var result = await _ops.RemoveAliasAsync(tag.Id, _projectId);
        Assert.False(result.Ok);
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task RemoveAlias_undoSkipsWhenTheIdealWasDeleted()
    {
        var brown = await _catalog.CreateTagAsync("brown hair");
        var brunette = await _catalog.CreateTagAsync("brunette");
        Assert.True((await _catalog.AddAliasAsync(brunette.Id, brown.Id)).Ok);
        Assert.True((await _ops.RemoveAliasAsync(brunette.Id, _projectId)).Ok);
        await _catalog.DeleteTagAsync(brown.Id);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.Empty(await AliasMapAsync());
        Assert.NotNull(undo);
    }

    [Fact]
    public async Task RepointAlias_movesTheEdge_asOneUndoStep()
    {
        var dark = await _catalog.CreateTagAsync("dark hair");
        var brown = await _catalog.CreateTagAsync("brown hair");
        var brunette = await _catalog.CreateTagAsync("brunette");
        Assert.True((await _catalog.AddAliasAsync(brunette.Id, dark.Id)).Ok);

        var move = await _ops.RepointAliasAsync(brunette.Id, brown.Id, _projectId);
        Assert.True(move.Ok, move.Message);
        Assert.Equal("Moved alias brunette to brown hair.", move.Message);
        Assert.Equal(brown.Id, (await AliasMapAsync())[brunette.Id]);

        var peek = (await _ops.PeekUndoAsync(_projectId))!;
        Assert.Equal(FileOpKind.TagAliasRepoint, peek.Kind);
        var item = Assert.Single(peek.Items);
        Assert.Equal(FileOpStep.RepointAlias, item.Step);
        Assert.Equal(brunette.Id, item.OldPath);
        Assert.Equal(brown.Id, item.NewPath);
        Assert.Equal(dark.Id, item.OldSourceId);
        Assert.Equal("Undo move alias", FileOpService.UndoLabel(peek));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(dark.Id, (await AliasMapAsync())[brunette.Id]);
    }

    [Fact]
    public async Task RepointAlias_toTheSameIdeal_isRefusedWithoutAJournalRow()
    {
        var dark = await _catalog.CreateTagAsync("dark hair");
        var brunette = await _catalog.CreateTagAsync("brunette");
        Assert.True((await _catalog.AddAliasAsync(brunette.Id, dark.Id)).Ok);

        var move = await _ops.RepointAliasAsync(brunette.Id, dark.Id, _projectId);
        Assert.False(move.Ok);
        Assert.Equal(dark.Id, (await AliasMapAsync())[brunette.Id]);
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task RepointAlias_ontoItself_isRefused_andKeepsTheOldEdge()
    {
        var dark = await _catalog.CreateTagAsync("dark hair");
        var brunette = await _catalog.CreateTagAsync("brunette");
        Assert.True((await _catalog.AddAliasAsync(brunette.Id, dark.Id)).Ok);

        var move = await _ops.RepointAliasAsync(brunette.Id, brunette.Id, _projectId);
        Assert.False(move.Ok);
        Assert.Equal(dark.Id, (await AliasMapAsync())[brunette.Id]);
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task RepointAlias_whenTheAliasHasNoEdge_fails()
    {
        var brown = await _catalog.CreateTagAsync("brown hair");
        var plain = await _catalog.CreateTagAsync("plain");
        var move = await _ops.RepointAliasAsync(plain.Id, brown.Id, _projectId);
        Assert.False(move.Ok);
    }

    [Fact]
    public async Task Merge_viaAddAlias_migratesFiles_andUndoRestoresThem()
    {
        var brown = await _catalog.CreateTagAsync("brown hair");
        var dark = await _catalog.CreateTagAsync("dark hair");
        var asset = await AddAssetAsync("a.png");
        await _catalog.AssignTagAsync(asset.Id, dark.Id, TagSource.Manual);

        var merge = await _ops.AddAliasAsync(dark.Id, brown.Id, _projectId);
        Assert.True(merge.Ok, merge.Message);
        var after = (await _catalog.GetAssignedTagsAsync(asset.Id)).Select(t => t.TagId).ToArray();
        Assert.Equal([brown.Id], after);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        var restored = (await _catalog.GetAssignedTagsAsync(asset.Id)).Select(t => t.TagId).ToArray();
        Assert.Equal([dark.Id], restored);
        Assert.Empty(await AliasMapAsync());
    }

    [Fact]
    public async Task RemoveAlias_fault_beforeJournal_rollsBackTheDelete()
    {
        var brown = await _catalog.CreateTagAsync("brown hair");
        var brunette = await _catalog.CreateTagAsync("brunette");
        Assert.True((await _catalog.AddAliasAsync(brunette.Id, brown.Id)).Ok);

        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailBeforeJournal = true };
        var remove = await _ops.RemoveAliasAsync(brunette.Id, _projectId);
        Assert.False(remove.Ok);
        Assert.Equal(brown.Id, (await AliasMapAsync())[brunette.Id]);
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
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
}
