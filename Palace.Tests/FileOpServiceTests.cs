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
        Assert.Equal("Moved 1 file to \u201cSorted\u201d.", result.Message);
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
    public async Task Undo_that_fails_on_a_locked_file_stays_undoable_and_works_on_retry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Locking a file against rename is a Windows sharing rule.
        }

        var asset = await AddAssetAsync(_rootA, "in.png");
        var dest = Dir(_rootA, "Sorted");
        await _ops.MoveAssetsAsync([asset], dest, _projectId);
        var movedPath = Path.Combine(dest, "in.png");

        FileOpResult failed;
        using (new FileStream(movedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            failed = await _ops.UndoLastAsync(_projectId);
        }

        Assert.False(failed.Ok);
        Assert.Null(failed.BatchId);
        Assert.Contains("Try again", failed.Message);
        Assert.True(File.Exists(movedPath));
        Assert.Equal("Undo move of 1 file", FileOpService.UndoLabel(await _ops.PeekUndoAsync(_projectId)));

        var retry = await _ops.UndoLastAsync(_projectId);

        Assert.True(retry.Ok, retry.Message);
        Assert.True(File.Exists(asset.Path));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
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
    public async Task UndoBatch_refuses_when_not_newest_then_succeeds_when_it_is()
    {
        var asset = await AddAssetAsync(_rootA, "review.png");
        var rejectDir = Dir(_rootA, "_rejected");
        var otherDir = Dir(_rootA, "Other");

        var rejectMove = await _ops.MoveAssetsAsync([asset], rejectDir, _projectId);
        Assert.True(rejectMove.Ok, rejectMove.Message);
        Assert.False(string.IsNullOrWhiteSpace(rejectMove.BatchId));
        var rejectBatchId = rejectMove.BatchId!;

        var moved = (await _catalog.GetAssetByIdAsync(asset.Id))!;
        var unrelated = await _ops.MoveAssetsAsync([moved], otherDir, _projectId);
        Assert.True(unrelated.Ok, unrelated.Message);

        var refused = await _ops.UndoBatchAsync(_projectId, rejectBatchId);
        Assert.False(refused.Ok);
        Assert.Contains("another file operation", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(otherDir, "review.png"), (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);

        var undoUnrelated = await _ops.UndoLastAsync(_projectId);
        Assert.True(undoUnrelated.Ok, undoUnrelated.Message);

        var undoReject = await _ops.UndoBatchAsync(_projectId, rejectBatchId);
        Assert.True(undoReject.Ok, undoReject.Message);
        Assert.Equal(asset.Path, (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);
    }

    [Fact]
    public async Task UndoBatch_already_undone_is_success_with_no_moves()
    {
        var asset = await AddAssetAsync(_rootA, "already.png");
        var rejectDir = Dir(_rootA, "_rejected");
        var rejectMove = await _ops.MoveAssetsAsync([asset], rejectDir, _projectId);
        Assert.True(rejectMove.Ok, rejectMove.Message);
        var rejectBatchId = rejectMove.BatchId!;

        var viaToolbar = await _ops.UndoLastAsync(_projectId);
        Assert.True(viaToolbar.Ok, viaToolbar.Message);
        Assert.Equal(asset.Path, (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);

        var again = await _ops.UndoBatchAsync(_projectId, rejectBatchId);
        Assert.True(again.Ok, again.Message);
        Assert.Empty(again.Moves);
        Assert.Equal(rejectBatchId, again.BatchId);
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

    private async Task<SourceFolder> AddOtherProjectSourceAsync(string path)
    {
        var other = await _catalog.CreateProjectAsync("Other");
        Directory.CreateDirectory(path);
        var source = new SourceFolder { Id = PalaceDb.NewId(), Path = path, ProjectId = other.Id };
        await _catalog.UpsertSourceFolderAsync(source);
        return source;
    }

    /// <summary>Folder symlink; false where the OS refuses (Windows without symlink rights).</summary>
    private static bool TryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Move_into_a_junction_or_symlink_folder_is_refused_and_nothing_moves()
    {
        var asset = await AddAssetAsync(_rootA, "in.png");
        var link = Path.Combine(_rootA.Path, "Linked");
        if (!TryLink(link, _outside))
        {
            return;
        }

        var result = await _ops.MoveAssetsAsync([asset], link, _projectId);
        var nested = await _ops.CreateFolderAsync(link, "Sub", _projectId);

        Assert.False(result.Ok);
        Assert.Contains("shortcut", result.Message);
        Assert.True(File.Exists(asset.Path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_outside));
        Assert.False(nested.Ok);
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Move_of_a_file_reached_through_a_symlink_folder_is_skipped()
    {
        var real = Path.Combine(_outside, "x.png");
        await File.WriteAllTextAsync(real, "pixels");
        if (!TryLink(Path.Combine(_rootA.Path, "Linked"), _outside))
        {
            return;
        }
        var asset = new Asset
        {
            Id = PalaceDb.NewId(),
            SourceFolderId = _rootA.Id,
            Path = Path.Combine(_rootA.Path, "Linked", "x.png"),
            FileName = "x.png",
            Kind = AssetKind.Image,
            DateAdded = PalaceDb.NowIso()
        };
        await _catalog.UpsertAssetAsync(asset, "");

        var result = await _ops.MoveAssetsAsync([asset], Dir(_rootA, "Sorted"), _projectId);

        Assert.False(result.Ok);
        Assert.True(File.Exists(real));
    }

    [Fact]
    public async Task Move_and_new_folder_inside_another_projects_nested_root_are_refused()
    {
        var asset = await AddAssetAsync(_rootA, "in.png");
        var nestedRoot = await AddOtherProjectSourceAsync(Path.Combine(_rootA.Path, "TheirRoot"));

        var moved = await _ops.MoveAssetsAsync([asset], nestedRoot.Path, _projectId);
        var created = await _ops.CreateFolderAsync(nestedRoot.Path, "Mine", _projectId);
        var renamed = await _ops.RenameFolderAsync(nestedRoot.Path, "Gone", _projectId);

        Assert.False(moved.Ok);
        Assert.Contains("another project", moved.Message);
        Assert.True(File.Exists(asset.Path));
        Assert.False(created.Ok);
        Assert.False(Directory.Exists(Path.Combine(nestedRoot.Path, "Mine")));
        Assert.False(renamed.Ok);
        Assert.True(Directory.Exists(nestedRoot.Path));
    }

    [Fact]
    public async Task RenameFolder_refuses_a_folder_that_contains_a_watched_source()
    {
        var holder = Dir(_rootA, "Holder");
        await AddOtherProjectSourceAsync(Path.Combine(holder, "Inner"));

        var result = await _ops.RenameFolderAsync(holder, "Moved", _projectId);

        Assert.False(result.Ok);
        Assert.Contains("contains the Library folder", result.Message);
        Assert.True(Directory.Exists(holder));
        Assert.False(Directory.Exists(Path.Combine(_rootA.Path, "Moved")));
    }

    [Fact]
    public async Task RenameFolder_keeps_the_orphan_flag_of_missing_files()
    {
        var present = await AddAssetAsync(_rootA, Path.Combine("Old", "here.png"));
        var missing = await AddAssetAsync(_rootA, Path.Combine("Old", "gone.png"));
        File.Delete(missing.Path);
        await _catalog.MarkOrphanAsync(missing.Id, true);

        var result = await _ops.RenameFolderAsync(Path.Combine(_rootA.Path, "Old"), "New", _projectId);

        Assert.True(result.Ok, result.Message);
        Assert.True((await _catalog.GetAssetByIdAsync(missing.Id))!.IsOrphan);
        Assert.False((await _catalog.GetAssetByIdAsync(present.Id))!.IsOrphan);
    }

    [Fact]
    public async Task Move_cancelled_before_any_change_throws_and_moves_nothing()
    {
        var asset = await AddAssetAsync(_rootA, "in.png");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _ops.MoveAssetsAsync([asset], Dir(_rootA, "Sorted"), _projectId, cts.Token));

        Assert.True(File.Exists(asset.Path));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Move_cancelled_midway_logs_what_moved_so_undo_reverses_it()
    {
        var first = await AddAssetAsync(_rootA, "1.png");
        var second = await AddAssetAsync(_rootA, "2.png");
        var dest = Dir(_rootA, "Sorted");
        using var cts = new CancellationTokenSource();
        var assets = new CancelAfterFirst([first, second], cts);

        var result = await _ops.MoveAssetsAsync(assets, dest, _projectId, cts.Token);

        Assert.True(result.Ok, result.Message);
        Assert.Single(result.Moves);
        Assert.Contains("Stopped before the other 1", result.Message);
        Assert.True(File.Exists(Path.Combine(dest, "1.png")));
        Assert.True(File.Exists(second.Path));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.True(File.Exists(first.Path));
    }

    /// <summary>Cancels the token as soon as the second item is asked for.</summary>
    private sealed class CancelAfterFirst(IReadOnlyList<Asset> items, CancellationTokenSource cts) : IReadOnlyList<Asset>
    {
        public Asset this[int index] => items[index];
        public int Count => items.Count;

        public IEnumerator<Asset> GetEnumerator()
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (i == 1)
                {
                    cts.Cancel();
                }

                yield return items[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task Move_of_a_file_inside_another_projects_nested_root_is_skipped()
    {
        var nestedRoot = await AddOtherProjectSourceAsync(Path.Combine(_rootA.Path, "TheirRoot"));
        var theirs = await AddAssetAsync(_rootA, Path.Combine("TheirRoot", "x.png"));

        var result = await _ops.MoveAssetsAsync([theirs], Dir(_rootA, "Sorted"), _projectId);

        Assert.False(result.Ok);
        Assert.Contains("another project", result.Message);
        Assert.True(File.Exists(Path.Combine(nestedRoot.Path, "x.png")));
    }

    [Fact]
    public async Task Concurrent_moves_are_serialized_and_undo_stays_last_in_first_out()
    {
        var one = await AddAssetAsync(_rootA, "1.png");
        var two = await AddAssetAsync(_rootA, "2.png");
        var destOne = Dir(_rootA, "One");
        var destTwo = Dir(_rootA, "Two");

        var results = await Task.WhenAll(
            _ops.MoveAssetsAsync([one], destOne, _projectId),
            _ops.MoveAssetsAsync([two], destTwo, _projectId));

        Assert.All(results, r => Assert.True(r.Ok, r.Message));
        var undos = await Task.WhenAll(_ops.UndoLastAsync(_projectId), _ops.UndoLastAsync(_projectId));
        Assert.All(undos, u => Assert.True(u.Ok, u.Message));
        Assert.NotEqual(undos[0].BatchId, undos[1].BatchId);
        Assert.True(File.Exists(one.Path));
        Assert.True(File.Exists(two.Path));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task RenameFolder_with_emoji_name_rebases_catalog_paths_intact()
    {
        var emoji = "🎉";
        var created = await _ops.CreateFolderAsync(_rootA.Path, emoji, _projectId);
        Assert.True(created.Ok, created.Message);
        var asset = await AddAssetAsync(_rootA, Path.Combine(emoji, "shot.png"));
        var renamed = await _ops.RenameFolderAsync(created.Path!, "Renamed", _projectId);

        Assert.True(renamed.Ok, renamed.Message);
        var expected = Path.Combine(_rootA.Path, "Renamed", "shot.png");
        Assert.Equal(expected, (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);
        Assert.True(File.Exists(expected));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(asset.Path, (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);
    }

    [Fact]
    public async Task Undo_move_recovers_when_file_already_at_old_path_and_catalog_is_stale()
    {
        var asset = await AddAssetAsync(_rootA, "stale.png");
        var dest = Dir(_rootA, "Out");
        var moved = await _ops.MoveAssetsAsync([asset], dest, _projectId);
        Assert.True(moved.Ok, moved.Message);
        var newPath = Path.Combine(dest, "stale.png");

        // Simulate a prior undo that moved the file back but left the catalog on NewPath
        // (catalog update failed and TryMoveBack also failed).
        File.Move(newPath, asset.Path);
        Assert.True(File.Exists(asset.Path));
        Assert.False(File.Exists(newPath));
        Assert.Equal(newPath, (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(asset.Path, (await _catalog.GetAssetByIdAsync(asset.Id))!.Path);
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Undo_move_into_another_projects_nested_root_is_skipped()
    {
        var asset = await AddAssetAsync(_rootA, Path.Combine("Zone", "nested-undo.png"));
        var dest = Dir(_rootA, "Hold");
        var moved = await _ops.MoveAssetsAsync([asset], dest, _projectId);
        Assert.True(moved.Ok, moved.Message);

        // After the move, another project watches the original parent as a nested root.
        // Undo must refuse writing back into that nested root (same gate as forward ops).
        await AddOtherProjectSourceAsync(Path.Combine(_rootA.Path, "Zone"));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.False(undo.Ok);
        Assert.Contains("another project", undo.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(dest, "nested-undo.png")));
    }

    [Fact]
    public async Task Organize_then_move_share_one_undo_stack_newest_first()
    {
        var asset = await AddAssetAsync(_rootA, "shot.png");
        var organizeDest = Path.Combine(_rootA.Path, "Char", "tag", "shot.png");
        var organized = await _ops.ApplyOrganizeAsync(_projectId,
        [
            new OrganizeApplyItem(asset.Id, asset.FileName, asset.Path, organizeDest)
        ]);
        Assert.True(organized.Ok, organized.Message);
        Assert.True(File.Exists(organizeDest));
        Assert.True(Directory.Exists(Path.Combine(_rootA.Path, "Char", "tag")));
        Assert.Equal(FileOpKind.Organize, (await _ops.PeekUndoAsync(_projectId))!.Kind);

        var hold = Dir(_rootA, "Hold");
        var movedAsset = (await _catalog.GetAssetByIdAsync(asset.Id))!;
        var moved = await _ops.MoveAssetsAsync([movedAsset], hold, _projectId);
        Assert.True(moved.Ok, moved.Message);
        Assert.Equal(FileOpKind.Move, (await _ops.PeekUndoAsync(_projectId))!.Kind);

        var undoMove = await _ops.UndoLastAsync(_projectId);
        Assert.True(undoMove.Ok, undoMove.Message);
        Assert.True(File.Exists(organizeDest));
        Assert.Equal(FileOpKind.Organize, (await _ops.PeekUndoAsync(_projectId))!.Kind);

        var undoOrganize = await _ops.UndoLastAsync(_projectId);
        Assert.True(undoOrganize.Ok, undoOrganize.Message);
        Assert.True(File.Exists(asset.Path));
        Assert.False(Directory.Exists(Path.Combine(_rootA.Path, "Char", "tag")));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Move_absorbing_dialog_new_folder_is_one_undo()
    {
        var asset = await AddAssetAsync(_rootA, "pack.png");
        var created = await _ops.CreateFolderAsync(_rootA.Path, "Pack", _projectId);
        Assert.True(created.Ok, created.Message);
        Assert.Equal(FileOpKind.CreateFolder, (await _ops.PeekUndoAsync(_projectId))!.Kind);

        var moved = await _ops.MoveAssetsAsync(
            [asset],
            created.Path!,
            _projectId,
            absorbCreatedFolder: created.Path);
        Assert.True(moved.Ok, moved.Message);

        var peek = await _ops.PeekUndoAsync(_projectId);
        Assert.NotNull(peek);
        Assert.Equal(FileOpKind.Move, peek!.Kind);
        Assert.Single(peek.Items, i => i.Step == FileOpStep.CreateDir && FolderNames.SamePath(i.NewPath, created.Path!));
        Assert.Single(peek.Items, i => i.Step == FileOpStep.MoveFile);
        // CreateFolder batch was absorbed — one Undo clears the stack.

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.True(File.Exists(asset.Path));
        Assert.False(Directory.Exists(created.Path!));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    /// <summary>
    /// Batch Reject across two parent folders: each Move absorbs the prior batch
    /// so one Undo restores every file (Doktor B1).
    /// </summary>
    [Fact]
    public async Task Move_absorbPriorBatchId_multi_folder_reject_is_one_undo()
    {
        var shots = Dir(_rootA, "shots");
        var refs = Dir(_rootA, "refs");
        var a = await AddAssetAsync(_rootA, Path.Combine("shots", "a.png"), "a");
        var b = await AddAssetAsync(_rootA, Path.Combine("refs", "b.png"), "b");

        var rejectShots = await _ops.CreateFolderAsync(shots, ReviewReject.FolderName, _projectId);
        Assert.True(rejectShots.Ok, rejectShots.Message);
        var moveShots = await _ops.MoveAssetsAsync(
            [a],
            rejectShots.Path!,
            _projectId,
            absorbCreatedFolder: rejectShots.Path);
        Assert.True(moveShots.Ok, moveShots.Message);
        Assert.False(string.IsNullOrWhiteSpace(moveShots.BatchId));

        var rejectRefs = await _ops.CreateFolderAsync(refs, ReviewReject.FolderName, _projectId);
        Assert.True(rejectRefs.Ok, rejectRefs.Message);
        var moveRefs = await _ops.MoveAssetsAsync(
            [b],
            rejectRefs.Path!,
            _projectId,
            absorbCreatedFolder: rejectRefs.Path,
            absorbPriorBatchId: moveShots.BatchId);
        Assert.True(moveRefs.Ok, moveRefs.Message);
        Assert.True(moveRefs.AbsorbedPriorBatch);

        var peek = await _ops.PeekUndoAsync(_projectId);
        Assert.NotNull(peek);
        Assert.Equal(2, peek!.Items.Count(i => i.Step == FileOpStep.MoveFile));
        Assert.Equal(2, peek.Items.Count(i => i.Step == FileOpStep.CreateDir));
        Assert.Equal(BatchReject.JournalSummary(2, 2), peek.Summary);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.True(File.Exists(a.Path));
        Assert.True(File.Exists(b.Path));
        Assert.False(Directory.Exists(rejectShots.Path!));
        Assert.False(Directory.Exists(rejectRefs.Path!));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    /// <summary>
    /// Intervening Move between prior Reject and this Move → do not absorb
    /// (Doktor M2 option a: caller stops the batch; each completed part stays
    /// separately undoable — never multi-ID half-undo).
    /// </summary>
    [Fact]
    public async Task Move_absorbPriorBatchId_skipped_when_newer_not_dest_create()
    {
        var shots = Dir(_rootA, "shots");
        var a = await AddAssetAsync(_rootA, Path.Combine("shots", "a.png"), "a");
        var extra = await AddAssetAsync(_rootA, "extra.png", "x");

        var rejectShots = await _ops.CreateFolderAsync(shots, ReviewReject.FolderName, _projectId);
        Assert.True(rejectShots.Ok, rejectShots.Message);
        var moveShots = await _ops.MoveAssetsAsync(
            [a],
            rejectShots.Path!,
            _projectId,
            absorbCreatedFolder: rejectShots.Path);
        Assert.True(moveShots.Ok, moveShots.Message);

        var other = Dir(_rootA, "Other");
        var intervening = await _ops.MoveAssetsAsync([extra], other, _projectId);
        Assert.True(intervening.Ok, intervening.Message);

        var b = await AddAssetAsync(_rootA, Path.Combine("shots", "b.png"), "b");
        var moveB = await _ops.MoveAssetsAsync(
            [b],
            rejectShots.Path!,
            _projectId,
            absorbPriorBatchId: moveShots.BatchId);
        Assert.True(moveB.Ok, moveB.Message);
        Assert.False(moveB.AbsorbedPriorBatch);

        // Prior reject Move still on the stack under the intervening Move + this Move.
        var undoable = await _catalog.GetUndoableFileOpsAsync(_projectId);
        Assert.Contains(undoable, x => string.Equals(x.Id, moveShots.BatchId, StringComparison.Ordinal));
        Assert.Contains(undoable, x => string.Equals(x.Id, moveB.BatchId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Batch Reject stop path: after folder A is rejected, an interleaved Move
    /// makes absorb unsafe — CanAbsorbPrior is false so the caller interrupts
    /// without moving B; Ctrl+Z undoes only the tip (intervening), then A.
    /// </summary>
    [Fact]
    public async Task BatchReject_interrupt_when_intervening_op_blocks_absorb()
    {
        var shots = Dir(_rootA, "shots");
        var refs = Dir(_rootA, "refs");
        var a = await AddAssetAsync(_rootA, Path.Combine("shots", "a.png"), "a");
        var b = await AddAssetAsync(_rootA, Path.Combine("refs", "b.png"), "b");
        var extra = await AddAssetAsync(_rootA, "extra.png", "x");

        var rejectShots = await _ops.CreateFolderAsync(shots, ReviewReject.FolderName, _projectId);
        Assert.True(rejectShots.Ok, rejectShots.Message);
        var moveA = await _ops.MoveAssetsAsync(
            [a],
            rejectShots.Path!,
            _projectId,
            absorbCreatedFolder: rejectShots.Path);
        Assert.True(moveA.Ok, moveA.Message);
        Assert.False(string.IsNullOrWhiteSpace(moveA.BatchId));

        var other = Dir(_rootA, "Other");
        var intervening = await _ops.MoveAssetsAsync([extra], other, _projectId);
        Assert.True(intervening.Ok, intervening.Message);

        var rejectRefs = Path.Combine(refs, ReviewReject.FolderName);
        var undoable = await _catalog.GetUndoableFileOpsAsync(_projectId);
        Assert.False(BatchReject.CanAbsorbPrior(undoable, moveA.BatchId!, rejectRefs));
        Assert.Equal(
            "Interrupted, 1 rejected so far.",
            BatchReject.InterruptedMessage(movedSoFar: 1));

        // Caller must not Move B. Tip undo reverses only the intervening Move;
        // then A is still separately undoable.
        var undoX = await _ops.UndoLastAsync(_projectId);
        Assert.True(undoX.Ok, undoX.Message);
        Assert.True(File.Exists(extra.Path));
        Assert.False(File.Exists(a.Path)); // still in _rejected
        Assert.True(File.Exists(b.Path)); // never moved

        var undoA = await _ops.UndoLastAsync(_projectId);
        Assert.True(undoA.Ok, undoA.Message);
        Assert.True(File.Exists(a.Path));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    /// <summary>
    /// Absorb folds prior into the new tip; TrackUndoBatchId keeps only the tip
    /// (cancel-between-folders: one Undo restores exactly what moved).
    /// </summary>
    [Fact]
    public async Task TrackUndoBatchId_after_absorb_one_undo_restores_moved()
    {
        var shots = Dir(_rootA, "shots");
        var refs = Dir(_rootA, "refs");
        var a = await AddAssetAsync(_rootA, Path.Combine("shots", "a.png"), "a");
        var b = await AddAssetAsync(_rootA, Path.Combine("refs", "b.png"), "b");

        var rejectShots = await _ops.CreateFolderAsync(shots, ReviewReject.FolderName, _projectId);
        var moveA = await _ops.MoveAssetsAsync(
            [a],
            rejectShots.Path!,
            _projectId,
            absorbCreatedFolder: rejectShots.Path);
        Assert.True(moveA.Ok, moveA.Message);

        var undoBatchIds = new List<string>();
        BatchReject.TrackUndoBatchId(
            undoBatchIds,
            chainBatchId: null,
            newBatchId: moveA.BatchId!,
            absorbedPriorBatch: false);

        var rejectRefs = await _ops.CreateFolderAsync(refs, ReviewReject.FolderName, _projectId);
        var moveB = await _ops.MoveAssetsAsync(
            [b],
            rejectRefs.Path!,
            _projectId,
            absorbCreatedFolder: rejectRefs.Path,
            absorbPriorBatchId: moveA.BatchId);
        Assert.True(moveB.AbsorbedPriorBatch);
        Assert.False(string.IsNullOrWhiteSpace(moveB.BatchId));
        BatchReject.TrackUndoBatchId(
            undoBatchIds,
            chainBatchId: moveA.BatchId,
            newBatchId: moveB.BatchId!,
            absorbedPriorBatch: true);

        // Cancel before a third folder: only the absorbed tip remains.
        Assert.Equal([moveB.BatchId!], undoBatchIds);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.True(File.Exists(a.Path));
        Assert.True(File.Exists(b.Path));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Move_without_absorb_keeps_create_folder_as_separate_undo()
    {
        var asset = await AddAssetAsync(_rootA, "loose.png");
        var created = await _ops.CreateFolderAsync(_rootA.Path, "Loose", _projectId);
        Assert.True(created.Ok, created.Message);

        var moved = await _ops.MoveAssetsAsync([asset], created.Path!, _projectId);
        Assert.True(moved.Ok, moved.Message);
        Assert.Equal(FileOpKind.Move, (await _ops.PeekUndoAsync(_projectId))!.Kind);

        var undoMove = await _ops.UndoLastAsync(_projectId);
        Assert.True(undoMove.Ok, undoMove.Message);
        Assert.True(File.Exists(asset.Path));
        Assert.True(Directory.Exists(created.Path!));
        Assert.Equal(FileOpKind.CreateFolder, (await _ops.PeekUndoAsync(_projectId))!.Kind);

        var undoCreate = await _ops.UndoLastAsync(_projectId);
        Assert.True(undoCreate.Ok, undoCreate.Message);
        Assert.False(Directory.Exists(created.Path!));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Undo_assign_removes_implied_tags_that_assignment_added()
    {
        var asset = await AddAssetAsync(_rootA, "tagged.png");
        var parent = PalaceDb.NewId();
        var added = PalaceDb.NewId();
        var already = PalaceDb.NewId();
        var manual = PalaceDb.NewId();
        await _db.WriteAsync(conn =>
        {
            InsertTag(conn, parent, "Scarf");
            InsertTag(conn, added, "Winter");
            InsertTag(conn, already, "Already");
            InsertTag(conn, manual, "ManualKeep");
            InsertAssetTag(conn, asset.Id, parent, TagSource.Manual);
            InsertAssetTag(conn, asset.Id, added, TagSource.Implied);
            InsertAssetTag(conn, asset.Id, already, TagSource.Implied);
            InsertAssetTag(conn, asset.Id, manual, TagSource.Manual);
        });

        await _catalog.AddFileOpBatchAsync(new FileOpBatch
        {
            ProjectId = _projectId,
            Kind = FileOpKind.TagAssign,
            Summary = "Tagged 1 item with Scarf",
            Items =
            [
                new FileOpItem
                {
                    Step = FileOpStep.AssignTag,
                    AssetId = asset.Id,
                    OldPath = parent,
                    NewPath = "Scarf",
                    OldSourceId = TagSource.Manual.ToString(),
                    NewSourceId = TagAssignUndo.Encode([added, manual])
                }
            ]
        });

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        var left = await TagIdsOn(asset.Id);
        Assert.DoesNotContain(parent, left);
        Assert.DoesNotContain(added, left);
        Assert.Contains(already, left);
        Assert.Contains(manual, left);
    }

    [Fact]
    public async Task Undo_partial_fill_leaves_the_item_that_already_had_the_tag()
    {
        var kept = await AddAssetAsync(_rootA, "had.png");
        var a = await AddAssetAsync(_rootA, "a.png");
        var b = await AddAssetAsync(_rootA, "b.png");
        var c = await AddAssetAsync(_rootA, "c.png");
        var tag = PalaceDb.NewId();
        await _db.WriteAsync(conn =>
        {
            InsertTag(conn, tag, "PrGamma");
            InsertAssetTag(conn, kept.Id, tag, TagSource.Manual);
            InsertAssetTag(conn, a.Id, tag, TagSource.Manual);
            InsertAssetTag(conn, b.Id, tag, TagSource.Manual);
            InsertAssetTag(conn, c.Id, tag, TagSource.Manual);
        });

        var plans = new (string Id, IReadOnlyList<string> Before)[]
        {
            (kept.Id, [tag]),
            (a.Id, []),
            (b.Id, []),
            (c.Id, [])
        };
        var items = new List<FileOpItem>();
        foreach (var (id, before) in plans)
        {
            var change = TagAssignUndo.PlanAssign(before, [tag], tag);
            if (!change.Journal)
            {
                continue;
            }

            items.Add(new FileOpItem
            {
                Step = FileOpStep.AssignTag,
                AssetId = id,
                OldPath = tag,
                NewPath = "PrGamma",
                OldSourceId = TagSource.Manual.ToString(),
                NewSourceId = TagAssignUndo.Encode(change.ImpliedIds, change.TouchAssignedTag)
            });
        }

        Assert.Equal(3, items.Count);
        await _catalog.AddFileOpBatchAsync(new FileOpBatch
        {
            ProjectId = _projectId,
            Kind = FileOpKind.TagAssign,
            Summary = "Tagged 3 items with PrGamma",
            Items = items
        });

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Contains(tag, await TagIdsOn(kept.Id));
        Assert.DoesNotContain(tag, await TagIdsOn(a.Id));
        Assert.DoesNotContain(tag, await TagIdsOn(b.Id));
        Assert.DoesNotContain(tag, await TagIdsOn(c.Id));
    }

    [Fact]
    public async Task Undo_remove_restores_each_assets_own_source()
    {
        var exr = await AddAssetAsync(_rootA, "shot.exr");
        var hdr = await AddAssetAsync(_rootA, "shot.hdr");
        var tag = PalaceDb.NewId();
        await _db.WriteAsync(conn =>
        {
            InsertTag(conn, tag, "PrBeta");
            InsertAssetTag(conn, exr.Id, tag, TagSource.Implied);
            InsertAssetTag(conn, hdr.Id, tag, TagSource.Manual);
        });

        // The merged chip reports Implied for the whole selection.
        var chip = TagSource.Implied;
        var items = new List<FileOpItem>();
        foreach (var assetId in new[] { exr.Id, hdr.Id })
        {
            var before = await SourcesOn(assetId);
            await _catalog.RemoveTagForUndoAsync(assetId, tag);
            var after = await SourcesOn(assetId);
            var change = TagAssignUndo.PlanRemove(before, after, tag);
            Assert.True(change.Journal);
            Assert.True(change.TouchAssignedTag);
            items.Add(new FileOpItem
            {
                Step = FileOpStep.RemoveTag,
                AssetId = assetId,
                OldPath = tag,
                NewPath = "PrBeta",
                OldSourceId = TagAssignUndo.RecordedSource(before, tag, chip).ToString(),
                NewSourceId = TagAssignUndo.Encode(change.ImpliedIds, change.TouchAssignedTag)
            });
        }

        Assert.Equal(TagSource.Implied.ToString(), items[0].OldSourceId);
        Assert.Equal(TagSource.Manual.ToString(), items[1].OldSourceId);
        await _catalog.AddFileOpBatchAsync(new FileOpBatch
        {
            ProjectId = _projectId,
            Kind = FileOpKind.TagRemove,
            Summary = "Removed PrBeta from 2 items",
            Items = items
        });

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.Equal(TagSource.Implied, await SourceOf(exr.Id, tag));
        Assert.Equal(TagSource.Manual, await SourceOf(hdr.Id, tag));
    }

    private static void InsertTag(SqliteConnection conn, string id, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Tag (Id, Name, Priority, Slug, IsStarred)
            VALUES ($id, $name, 0, $slug, 0)
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$slug", name.ToLowerInvariant());
        cmd.ExecuteNonQuery();
    }

    private static void InsertAssetTag(SqliteConnection conn, string assetId, string tagId, TagSource source)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AssetTag (AssetId, TagId, Source)
            VALUES ($asset, $tag, $source)
            """;
        cmd.Parameters.AddWithValue("$asset", assetId);
        cmd.Parameters.AddWithValue("$tag", tagId);
        cmd.Parameters.AddWithValue("$source", source.ToString());
        cmd.ExecuteNonQuery();
    }

    private Task<List<(string Id, TagSource Source)>> SourcesOn(string assetId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TagId, Source FROM AssetTag WHERE AssetId = $asset";
            cmd.Parameters.AddWithValue("$asset", assetId);
            var rows = new List<(string Id, TagSource Source)>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), Enum.Parse<TagSource>(reader.GetString(1))));
            }

            return rows;
        });

    private async Task<TagSource?> SourceOf(string assetId, string tagId)
    {
        foreach (var row in await SourcesOn(assetId))
        {
            if (string.Equals(row.Id, tagId, StringComparison.Ordinal))
            {
                return row.Source;
            }
        }

        return null;
    }

    private Task<List<string>> TagIdsOn(string assetId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TagId FROM AssetTag WHERE AssetId = $asset";
            cmd.Parameters.AddWithValue("$asset", assetId);
            var ids = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }

            return ids;
        });
}
