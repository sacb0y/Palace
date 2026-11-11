using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>t2-03b Tags-page batch create journals CreateTag + AddMembership for Undo.</summary>
public sealed class TagCreateJournalTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-tag-create-" + Guid.NewGuid().ToString("N"));
    private PalaceDb _db = null!;
    private CatalogService _catalog = null!;
    private FileOpService _ops = null!;
    private string _projectId = "";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _db = new PalaceDb(Path.Combine(_dir, "catalog", "palace.db"));
        _catalog = new CatalogService(_db);
        _projectId = (await _catalog.GetProjectsAsync())[0].Id;
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
    public async Task CreateTagsInGroup_Journals_AndUndoRemoves()
    {
        var medium = await _catalog.CreateTagAsync("Medium");
        var result = await _ops.CreateTagsAsync(
            ["charcoal", "gouache", "pastel"],
            medium.Id,
            _projectId);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(3, result.Created);
        Assert.NotNull(result.BatchId);
        Assert.Contains("Created 3 tags in Medium", result.Message);

        var peek = await _ops.PeekUndoAsync(_projectId);
        Assert.NotNull(peek);
        Assert.Equal(FileOpKind.TagCreate, peek!.Kind);
        Assert.Equal(3, peek.Items.Count(i => i.Step == FileOpStep.CreateTag));
        Assert.Equal(3, peek.Items.Count(i => i.Step == FileOpStep.AddMembership));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);

        var tags = await _catalog.GetTagsAsync();
        Assert.DoesNotContain(tags, t => t.Name is "charcoal" or "gouache" or "pastel");
        Assert.Contains(tags, t => t.Name == "Medium");
    }

    [Fact]
    public async Task CreateTags_ExistingName_AddsMembershipOnly()
    {
        var medium = await _catalog.CreateTagAsync("Medium");
        var sketch = await _catalog.CreateTagAsync("sketch");

        var result = await _ops.CreateTagsAsync(["sketch"], medium.Id, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Existed);
        Assert.NotNull(result.BatchId);

        var peek = await _ops.PeekUndoAsync(_projectId);
        Assert.NotNull(peek);
        Assert.DoesNotContain(peek!.Items, i => i.Step == FileOpStep.CreateTag);
        Assert.Contains(peek.Items, i => i.Step == FileOpStep.AddMembership && i.NewPath == sketch.Id);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        var parents = await _catalog.GetParentsAsync(sketch.Id);
        Assert.Empty(parents);
    }
}
