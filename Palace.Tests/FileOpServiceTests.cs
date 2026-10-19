using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// FileOpService against a real temp catalog and temp folders: move / new / rename / delete folder,
/// name clashes never overwrite, everything stays inside watched sources, and Undo reverses it.
/// </summary>
public sealed class FileOpServiceTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-fileops-" + Guid.NewGuid().ToString("N"));
    private PalaceDb _db = null!;
    private CatalogService _catalog = null!;
    private FileOpService _ops = null!;
    private string _projectId = "";
    private SourceFolder _rootA = null!;
    private SourceFolder _rootB = null!;
    private string _outside = "";
    private int _suppressed;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _db = new PalaceDb(Path.Combine(_dir, "catalog", "palace.db"));
        _catalog = new CatalogService(_db);
        _projectId = (await _catalog.GetProjectsAsync())[0].Id;
        _rootA = await AddSourceAsync("A");
        _rootB = await AddSourceAsync("B");
        _outside = Path.Combine(_dir, "outside");
        Directory.CreateDirectory(_outside);
        _ops = new FileOpService(_catalog, () =>
        {
            Interlocked.Increment(ref _suppressed);
            return new Scope();
        });
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

    private async Task<SourceFolder> AddSourceAsync(string name)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(path);
        var source = new SourceFolder { Id = PalaceDb.NewId(), Path = path, ProjectId = _projectId };
        await _catalog.UpsertSourceFolderAsync(source);
        return source;
    }

    private async Task<Asset> AddAssetAsync(SourceFolder source, string relativePath, string content = "pixels")
    {
        var path = Path.Combine(source.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        var asset = new Asset
        {
            Id = PalaceDb.NewId(),
            SourceFolderId = source.Id,
            Path = path,
            FileName = Path.GetFileName(path),
            Kind = AssetKind.Image,
            DateAdded = PalaceDb.NowIso()
        };
        await _catalog.UpsertAssetAsync(asset, "");
        return (await _catalog.GetAssetByIdAsync(asset.Id))!;
    }

    private string Dir(SourceFolder source, string relative)
    {
        var path = Path.Combine(source.Path, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task Move_moves_the_file_updates_the_catalog_and_undo_puts_it_back()
    {
        var asset = await AddAssetAsync(_rootA, "in.png");
        var dest = Dir(_rootA, "Sorted");

        var result = await _ops.MoveAssetsAsync([asset], dest, _projectId);

        Assert.True(result.Ok, result.Message);
        var newPath = Path.Combine(dest, "in.png");
        Assert.True(File.Exists(newPath));
        Assert.False(File.Exists(asset.Path));
        Assert.Equal(newPath, (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);
        Assert.Equal("Moved 1 file to Sorted.", result.Message);
        Assert.True(_suppressed > 0);

        var peek = await _ops.PeekUndoAsync(_projectId);
        Assert.Equal("Undo move of 1 file", FileOpService.UndoLabel(peek));

        var undo = await _ops.UndoLastAsync(_projectId);

        Assert.True(undo.Ok, undo.Message);
        Assert.Equal("Undid move of 1 file.", undo.Message);
        Assert.True(File.Exists(asset.Path));
        Assert.False(File.Exists(newPath));
        Assert.Equal(asset.Path, (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Move_never_overwrites_a_file_with_the_same_name()
    {
        var asset = await AddAssetAsync(_rootA, "same.png", "mine");
        var dest = Dir(_rootA, "Sorted");
        await File.WriteAllTextAsync(Path.Combine(dest, "same.png"), "theirs");

        var result = await _ops.MoveAssetsAsync([asset], dest, _projectId);

        Assert.True(result.Ok);
        Assert.Equal("theirs", await File.ReadAllTextAsync(Path.Combine(dest, "same.png")));
        Assert.Equal("mine", await File.ReadAllTextAsync(Path.Combine(dest, "same (2).png")));
        Assert.Equal(Path.Combine(dest, "same (2).png"), (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);
    }

    [Fact]
    public async Task Move_of_two_files_with_one_name_gives_each_a_unique_name()
    {
        var one = await AddAssetAsync(_rootA, Path.Combine("x", "a.png"), "one");
        var two = await AddAssetAsync(_rootA, Path.Combine("y", "a.png"), "two");
        var dest = Dir(_rootA, "Sorted");

        var result = await _ops.MoveAssetsAsync([one, two], dest, _projectId);

        Assert.Equal(2, result.Moves.Count);
        Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(dest, "a.png")));
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(dest, "a (2).png")));
    }

    [Fact]
    public async Task Move_skips_a_name_still_owned_by_a_stale_catalog_row()
    {
        var dest = Dir(_rootA, "Sorted");
        var stale = await AddAssetAsync(_rootA, Path.Combine("Sorted", "pic.png"));
        File.Delete(stale.Path); // row stays (orphan-to-be), file is gone
        var asset = await AddAssetAsync(_rootA, "pic.png");

        var result = await _ops.MoveAssetsAsync([asset], dest, _projectId);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(Path.Combine(dest, "pic (2).png"), result.Moves[0].NewPath);
    }

    [Fact]
    public async Task Move_outside_every_watched_folder_is_refused_and_nothing_moves()
    {
        var asset = await AddAssetAsync(_rootA, "in.png");

        var result = await _ops.MoveAssetsAsync([asset], _outside, _projectId);

        Assert.False(result.Ok);
        Assert.True(File.Exists(asset.Path));
        Assert.Empty(Directory.GetFiles(_outside));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Move_to_another_watched_folder_updates_the_source_and_undo_restores_it()
    {
        var asset = await AddAssetAsync(_rootA, "in.png");
        var dest = Dir(_rootB, "Inbox");

        var result = await _ops.MoveAssetsAsync([asset], dest, _projectId);
        Assert.True(result.Ok);
        Assert.Equal(_rootB.Id, (await _catalog.GetAssetByIdAsync(asset.Id))!.SourceFolderId);

        await _ops.UndoLastAsync(_projectId);
        var back = (await _catalog.GetAssetByIdAsync(asset.Id))!;
        Assert.Equal(_rootA.Id, back.SourceFolderId);
        Assert.Equal(asset.Path, back.Path);
    }

    [Fact]
    public async Task Move_skips_missing_files_and_files_already_there()
    {
        var gone = await AddAssetAsync(_rootA, "gone.png");
        File.Delete(gone.Path);
        var dest = Dir(_rootA, "Sorted");
        var there = await AddAssetAsync(_rootA, Path.Combine("Sorted", "there.png"));

        var result = await _ops.MoveAssetsAsync([gone, there], dest, _projectId);

        Assert.False(result.Ok);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Move_of_an_online_only_file_to_another_watched_folder_is_skipped()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // FILE_ATTRIBUTE_OFFLINE can only be stamped on Windows.
        }

        var asset = await AddAssetAsync(_rootA, "cloud.png");
        File.SetAttributes(asset.Path, File.GetAttributes(asset.Path) | FileAttributes.Offline);
        Assert.True(CloudFile.IsOnlineOnly(asset.Path));

        var across = await _ops.MoveAssetsAsync([asset], Dir(_rootB, "Inbox"), _projectId);
        Assert.False(across.Ok);
        Assert.Contains("online-only", across.Skipped[0]);
        Assert.True(File.Exists(asset.Path));

        var within = await _ops.MoveAssetsAsync([asset], Dir(_rootA, "Sorted"), _projectId);
        Assert.True(within.Ok, within.Message);
        Assert.True(CloudFile.IsOnlineOnly(within.Moves[0].NewPath));
        File.SetAttributes(within.Moves[0].NewPath, FileAttributes.Normal);
    }

    [Fact]
    public async Task Undo_puts_a_file_back_under_a_unique_name_when_its_old_spot_is_taken()
    {
        var asset = await AddAssetAsync(_rootA, "in.png", "mine");
        var dest = Dir(_rootA, "Sorted");
        await _ops.MoveAssetsAsync([asset], dest, _projectId);
        await File.WriteAllTextAsync(asset.Path, "new file in the old spot");

        var undo = await _ops.UndoLastAsync(_projectId);

        Assert.True(undo.Ok);
        Assert.Equal("new file in the old spot", await File.ReadAllTextAsync(asset.Path));
        var restored = Path.Combine(_rootA.Path, "in (2).png");
        Assert.Equal("mine", await File.ReadAllTextAsync(restored));
        Assert.Equal(restored, (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);
    }

    [Fact]
    public async Task Undo_is_last_in_first_out_and_scoped_to_the_project()
    {
        var asset = await AddAssetAsync(_rootA, "in.png");
        var first = Dir(_rootA, "First");
        var second = Dir(_rootA, "Second");
        await _ops.MoveAssetsAsync([asset], first, _projectId);
        var moved = (await _catalog.GetAssetByIdAsync(asset.Id))!;
        await _ops.MoveAssetsAsync([moved], second, _projectId);

        Assert.Null(await _ops.PeekUndoAsync("another-project"));
        Assert.False((await _ops.UndoLastAsync("another-project")).Ok);

        await _ops.UndoLastAsync(_projectId);
        Assert.Equal(Path.Combine(first, "in.png"), (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);
        await _ops.UndoLastAsync(_projectId);
        Assert.Equal(asset.Path, (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);
        Assert.Equal("Nothing to undo.", (await _ops.UndoLastAsync(_projectId)).Message);
    }

    [Fact]
    public async Task CreateFolder_validates_the_name_and_refuses_duplicates_and_outside_roots()
    {
        Assert.False((await _ops.CreateFolderAsync(_rootA.Path, "  ", _projectId)).Ok);
        Assert.False((await _ops.CreateFolderAsync(_rootA.Path, "a:b", _projectId)).Ok);
        Assert.False((await _ops.CreateFolderAsync(_rootA.Path, "CON", _projectId)).Ok);
        Assert.False((await _ops.CreateFolderAsync(_outside, "New", _projectId)).Ok);
        Dir(_rootA, "Taken");
        var dup = await _ops.CreateFolderAsync(_rootA.Path, "taken", _projectId);
        Assert.False(dup.Ok);
        Assert.Contains("already exists", dup.Message);
        Assert.Empty(Directory.GetDirectories(_outside));
    }

    [Fact]
    public async Task CreateFolder_then_undo_removes_it_only_while_empty()
    {
        var made = await _ops.CreateFolderAsync(_rootA.Path, " Refs ", _projectId);
        Assert.True(made.Ok, made.Message);
        Assert.Equal(Path.Combine(_rootA.Path, "Refs"), made.Path);
        Assert.True(Directory.Exists(made.Path));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok);
        Assert.False(Directory.Exists(made.Path));

        var again = await _ops.CreateFolderAsync(_rootA.Path, "Refs", _projectId);
        await File.WriteAllTextAsync(Path.Combine(again.Path!, "keep.txt"), "x");
        var kept = await _ops.UndoLastAsync(_projectId);
        Assert.False(kept.Ok);
        Assert.Contains("isn't empty", kept.Message);
        Assert.True(File.Exists(Path.Combine(again.Path!, "keep.txt")));
    }

    [Fact]
    public async Task RenameFolder_rebases_nested_catalog_rows_and_undo_renames_back()
    {
        var nested = await AddAssetAsync(_rootA, Path.Combine("Old", "deep", "n.png"));
        var top = await AddAssetAsync(_rootA, Path.Combine("Old", "t.png"));
        var sibling = await AddAssetAsync(_rootA, Path.Combine("Older", "s.png"));
        var old = Path.Combine(_rootA.Path, "Old");

        var result = await _ops.RenameFolderAsync(old, "New", _projectId);

        Assert.True(result.Ok, result.Message);
        var renamed = Path.Combine(_rootA.Path, "New");
        Assert.True(Directory.Exists(renamed));
        Assert.Equal(Path.Combine(renamed, "deep", "n.png"), (await _catalog.GetAssetByIdAsync(nested.Id))!.Path);
        Assert.Equal(Path.Combine(renamed, "t.png"), (await _catalog.GetAssetByIdAsync(top.Id))!.Path);
        Assert.Equal(sibling.Path, (await _catalog.GetAssetByIdAsync(sibling.Id))!.Path);

        await _ops.UndoLastAsync(_projectId);
        Assert.True(Directory.Exists(old));
        Assert.Equal(nested.Path, (await _catalog.GetAssetByIdAsync(nested.Id))!.Path);
        Assert.Equal(top.Path, (await _catalog.GetAssetByIdAsync(top.Id))!.Path);
    }

    [Fact]
    public async Task RenameFolder_refuses_roots_existing_names_and_outside_paths()
    {
        Dir(_rootA, "One");
        Dir(_rootA, "Two");
        Assert.False((await _ops.RenameFolderAsync(_rootA.Path, "Renamed", _projectId)).Ok);
        Assert.False((await _ops.RenameFolderAsync(Path.Combine(_rootA.Path, "One"), "Two", _projectId)).Ok);
        Assert.False((await _ops.RenameFolderAsync(_outside, "Nope", _projectId)).Ok);
        Assert.True(Directory.Exists(Path.Combine(_rootA.Path, "One")));
        Assert.True(Directory.Exists(_outside));
    }

    [Fact]
    public async Task RenameFolder_handles_a_case_only_rename()
    {
        var asset = await AddAssetAsync(_rootA, Path.Combine("refs", "r.png"));

        var result = await _ops.RenameFolderAsync(Path.Combine(_rootA.Path, "refs"), "Refs", _projectId);

        Assert.True(result.Ok, result.Message);
        Assert.Contains("Refs", Directory.GetDirectories(_rootA.Path).Select(Path.GetFileName));
        Assert.Equal(Path.Combine(_rootA.Path, "Refs", "r.png"), (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);
    }

    [Fact]
    public async Task DeleteEmptyFolder_only_deletes_empty_folders_and_undo_recreates_it()
    {
        var full = Dir(_rootA, "Full");
        await File.WriteAllTextAsync(Path.Combine(full, ".hidden"), "x");
        var refused = await _ops.DeleteEmptyFolderAsync(full, _projectId);
        Assert.False(refused.Ok);
        Assert.True(Directory.Exists(full));

        Assert.False((await _ops.DeleteEmptyFolderAsync(_rootA.Path, _projectId)).Ok);
        Assert.True(Directory.Exists(_rootA.Path));

        var empty = Dir(_rootA, "Empty");
        var deleted = await _ops.DeleteEmptyFolderAsync(empty, _projectId);
        Assert.True(deleted.Ok, deleted.Message);
        Assert.False(Directory.Exists(empty));
        Assert.Equal("Undo delete of Empty", FileOpService.UndoLabel(await _ops.PeekUndoAsync(_projectId)));

        await _ops.UndoLastAsync(_projectId);
        Assert.True(Directory.Exists(empty));
    }

    [Fact]
    public void ResolveRoot_picks_the_deepest_local_source_and_ignores_cloud_api_sources()
    {
        var sources = new[]
        {
            new SourceFolder { Id = "outer", Path = _dir },
            new SourceFolder { Id = "inner", Path = _rootA.Path },
            new SourceFolder { Id = "api", Path = _rootB.Path, Kind = SourceKind.Dropbox }
        };

        Assert.Equal("inner", FileOpService.ResolveRoot(Path.Combine(_rootA.Path, "x", "y.png"), sources)!.Id);
        Assert.Equal("outer", FileOpService.ResolveRoot(Path.Combine(_rootB.Path, "x"), sources)!.Id);
        Assert.Null(FileOpService.ResolveRoot(Path.Combine(Path.GetTempPath(), "elsewhere"), sources));
        Assert.Null(FileOpService.ResolveRoot(_rootA.Path + "-sibling", sources[1..]));
    }
}
