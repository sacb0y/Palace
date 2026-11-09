using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Tag JSON import: pre-import DB backup + single SQLite transaction.
/// Mid-import failure must leave the catalog unchanged.
/// </summary>
public sealed class TagImportAtomicTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-tag-import-atomic-" + Guid.NewGuid().ToString("N"));
    private PalaceDb _db = null!;
    private CatalogService _catalog = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _db = new PalaceDb(Path.Combine(_dir, "catalog", "palace.db"));
        _catalog = new CatalogService(_db);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task MidImportFailure_LeavesCatalogUnchanged_AndWroteBackup()
    {
        var before = await _catalog.CreateTagAsync("keep-me");
        var beforeNames = (await _catalog.GetTagsAsync()).Select(t => t.Name).OrderBy(n => n).ToList();

        var json = """
            {
              "schema": "palace-tags/1",
              "tags": [
                { "name": "alpha" },
                { "name": "beta" },
                { "name": "gamma" }
              ]
            }
            """;

        _catalog.TestTagImportFault = new TagImportFaultProbe { FailAfterCreates = 2 };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _catalog.ImportTagsJsonAsync(json));
        Assert.Contains("FailAfterCreates", ex.Message, StringComparison.Ordinal);

        var afterNames = (await _catalog.GetTagsAsync()).Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(beforeNames, afterNames);
        Assert.Null(await _catalog.FindTagByNameAsync("alpha"));
        Assert.Null(await _catalog.FindTagByNameAsync("beta"));
        Assert.Null(await _catalog.FindTagByNameAsync("gamma"));
        Assert.NotNull(await _catalog.FindTagByNameAsync("keep-me"));
        Assert.Equal(before.Id, (await _catalog.FindTagByNameAsync("keep-me"))!.Id);

        Assert.False(string.IsNullOrEmpty(_db.LastTagImportBackupDir));
        Assert.True(Directory.Exists(_db.LastTagImportBackupDir));
        Assert.True(File.Exists(Path.Combine(_db.LastTagImportBackupDir!, "palace.db")));
    }

    [Fact]
    public async Task SuccessfulImport_CommitsCreates_AndWritesBackup()
    {
        var json = """
            {
              "schema": "palace-tags/1",
              "tags": [
                { "name": "ink" },
                { "name": "wash", "parents": ["ink"] }
              ]
            }
            """;

        var plan = await _catalog.ImportTagsJsonAsync(json);
        Assert.True(plan.Creates.Count >= 2);
        Assert.NotNull(await _catalog.FindTagByNameAsync("ink"));
        Assert.NotNull(await _catalog.FindTagByNameAsync("wash"));
        Assert.False(string.IsNullOrEmpty(_db.LastTagImportBackupDir));
        Assert.True(File.Exists(Path.Combine(_db.LastTagImportBackupDir!, "palace.db")));
    }
}
