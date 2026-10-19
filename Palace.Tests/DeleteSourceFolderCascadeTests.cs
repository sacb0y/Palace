using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Removing a watched folder cascades assets (and tag/collection links via FK), but
/// OrganizeBatchItem has no ON DELETE CASCADE — DeleteSourceFolderAsync must clean those rows.
/// </summary>
public sealed class DeleteSourceFolderCascadeTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-src-cascade-" + Guid.NewGuid().ToString("N"));
    private PalaceDb _db = null!;
    private CatalogService _catalog = null!;
    private string _projectId = "";
    private SourceFolder _source = null!;

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
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CountAssetsForSource_ReturnsRowCount()
    {
        Assert.Equal(0, await _catalog.CountAssetsForSourceAsync(_source.Id));
        await AddAssetAsync("a.png");
        await AddAssetAsync("b.png");
        Assert.Equal(2, await _catalog.CountAssetsForSourceAsync(_source.Id));
    }

    [Fact]
    public async Task DeleteSourceFolder_RemovesOrganizeBatchItems_AndLeavesFilesOnDisk()
    {
        var asset = await AddAssetAsync("keep-on-disk.png", "pixels");
        var diskPath = asset.Path;
        Assert.True(File.Exists(diskPath));

        var batchId = await _catalog.CreateOrganizeBatchAsync(
        [
            new OrganizeBatchItem
            {
                AssetId = asset.Id,
                OldPath = asset.Path,
                NewPath = Path.Combine(_source.Path, "moved.png"),
                Status = OrganizeItemStatus.Applied
            }
        ]);
        Assert.False(string.IsNullOrEmpty(batchId));
        Assert.Single(await _catalog.GetBatchItemsAsync(batchId));

        await _catalog.DeleteSourceFolderAsync(_source.Id);

        Assert.Null(await _catalog.GetSourceFolderAsync(_source.Id));
        Assert.Null(await _catalog.GetAssetByIdAsync(asset.Id));
        Assert.Empty(await _catalog.GetBatchItemsAsync(batchId));
        Assert.Equal(0, await CountOrganizeBatchItemsAsync());
        Assert.True(File.Exists(diskPath), "Remove folder must not delete files on disk.");
    }

    [Fact]
    public async Task DeleteSourceFolder_WithOrganizeBatchItem_DoesNotThrowOnFk()
    {
        var asset = await AddAssetAsync("tagged.png");
        await _catalog.CreateOrganizeBatchAsync(
        [
            new OrganizeBatchItem
            {
                AssetId = asset.Id,
                OldPath = asset.Path,
                NewPath = asset.Path + ".new",
                Status = OrganizeItemStatus.Applied
            }
        ]);

        var ex = await Record.ExceptionAsync(() => _catalog.DeleteSourceFolderAsync(_source.Id));
        Assert.Null(ex);
    }

    private async Task<Asset> AddAssetAsync(string relativePath, string content = "x")
    {
        var path = Path.Combine(_source.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        var asset = new Asset
        {
            Id = PalaceDb.NewId(),
            SourceFolderId = _source.Id,
            Path = path,
            FileName = Path.GetFileName(path),
            Kind = AssetKind.Image,
            DateAdded = PalaceDb.NowIso()
        };
        await _catalog.UpsertAssetAsync(asset, "");
        return (await _catalog.GetAssetByIdAsync(asset.Id))!;
    }

    private Task<int> CountOrganizeBatchItemsAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM OrganizeBatchItem";
            return Convert.ToInt32(cmd.ExecuteScalar());
        });
}
