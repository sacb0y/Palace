using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// WinUI-free Library folder-tree harness (<see cref="FolderTreeVm"/>): create folder → move one
/// file → rebuild → leaf count is 1 (organizing-review A1). Same path as
/// <c>LibraryViewModel</c> after FileOp create / move.
/// </summary>
public sealed class FolderTreeVmTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-tree-vm-" + Guid.NewGuid().ToString("N"));
    private PalaceDb _db = null!;
    private CatalogService _catalog = null!;
    private FileOpService _ops = null!;
    private string _projectId = "";
    private SourceFolder _root = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _db = new PalaceDb(Path.Combine(_dir, "catalog", "palace.db"));
        _catalog = new CatalogService(_db);
        _projectId = (await _catalog.GetProjectsAsync())[0].Id;
        var path = Path.Combine(_dir, "Library");
        Directory.CreateDirectory(path);
        _root = new SourceFolder { Id = PalaceDb.NewId(), Path = path, ProjectId = _projectId };
        await _catalog.UpsertSourceFolderAsync(_root);
        _ops = new FileOpService(_catalog, () => new Scope());
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() { }
    }

    private async Task<Asset> AddAssetAsync(string relativePath, string content = "pixels")
    {
        var path = Path.Combine(_root.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        var asset = new Asset
        {
            Id = PalaceDb.NewId(),
            SourceFolderId = _root.Id,
            Path = path,
            FileName = Path.GetFileName(path),
            Kind = AssetKind.Image,
            DateAdded = PalaceDb.NowIso()
        };
        await _catalog.UpsertAssetAsync(asset, "");
        return (await _catalog.GetAssetByIdAsync(asset.Id))!;
    }

    private async Task<List<FolderTreeEntry>> RebuildAsync()
    {
        var sources = await _catalog.GetSourceFoldersAsync(_projectId);
        var assets = await _catalog.GetAssetsAsync(projectId: _projectId);
        return FolderTreeVm.BuildFromCatalog(sources, assets, FolderTreeSort.NameAsc);
    }

    [Fact]
    public async Task Create_folder_then_move_one_file_rebuild_leaf_count_is_1()
    {
        var asset = await AddAssetAsync("top.png");

        var created = await _ops.CreateFolderAsync(_root.Path, "Sorted", _projectId);
        Assert.True(created.Ok, created.Message);
        Assert.NotNull(created.Path);

        var emptyTree = await RebuildAsync();
        var emptyLeaf = FolderTreeVm.Find(emptyTree, created.Path!);
        Assert.NotNull(emptyLeaf);
        Assert.Equal(0, emptyLeaf.AssetCount);
        Assert.Equal("0", FolderTreeVm.CountText(emptyLeaf.AssetCount));
        Assert.Equal("Sorted, empty", FolderTreeVm.AutomationName(emptyLeaf.Name, emptyLeaf.AssetCount));

        var moved = await _ops.MoveAssetsAsync([asset], created.Path!, _projectId);
        Assert.True(moved.Ok, moved.Message);
        Assert.Single(moved.Moves);

        var tree = await RebuildAsync();
        var leaf = FolderTreeVm.Find(tree, created.Path!);
        Assert.NotNull(leaf);
        Assert.Equal(1, leaf.AssetCount);
        Assert.Equal("1", FolderTreeVm.CountText(leaf.AssetCount));
        Assert.Equal("Sorted, 1 items", FolderTreeVm.AutomationName(leaf.Name, leaf.AssetCount));

        var root = Assert.Single(tree);
        Assert.Equal(1, root.AssetCount);
    }

    [Theory]
    [InlineData(0, "0", "Album, empty")]
    [InlineData(1, "1", "Album, 1 items")]
    public void Count_and_automation_face_strings(int count, string countText, string automation)
    {
        Assert.Equal(countText, FolderTreeVm.CountText(count));
        Assert.Equal(automation, FolderTreeVm.AutomationName("Album", count));
    }

    [Fact]
    public void CountText_uses_N0_grouping() =>
        Assert.Equal(1000.ToString("N0"), FolderTreeVm.CountText(1000));
}
