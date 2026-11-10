using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Delete rejected: scope, network skip, recycle vs permanent, undo restore
/// (files + catalog rows), partial failure, emptied <c>_rejected</c> cleanup.
/// Synthetic temp data only.
/// </summary>
public sealed class DeleteRejectedTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-delrej-" + Guid.NewGuid().ToString("N"));
    private readonly string _trashRoot;
    private PalaceDb _db = null!;
    private CatalogService _catalog = null!;
    private FileOpService _ops = null!;
    private FolderFileTrash _trash = null!;
    private string _projectId = "";
    private SourceFolder _root = null!;

    public DeleteRejectedTests()
    {
        _trashRoot = Path.Combine(_dir, "trash");
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _db = new PalaceDb(Path.Combine(_dir, "catalog", "palace.db"));
        _catalog = new CatalogService(_db);
        _projectId = (await _catalog.GetProjectsAsync())[0].Id;
        var path = Path.Combine(_dir, "Lib");
        Directory.CreateDirectory(path);
        _root = new SourceFolder { Id = PalaceDb.NewId(), Path = path, ProjectId = _projectId };
        await _catalog.UpsertSourceFolderAsync(_root);
        _trash = new FolderFileTrash(_trashRoot);
        _ops = new FileOpService(_catalog, () => new Nop(), _trash);
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

    private async Task<Asset> AddRejectedAsync(string relativeUnderParent, string content = "px", long? size = null)
    {
        // relativeUnderParent is under Lib, e.g. "shots/_rejected/a.jpg"
        var full = Path.Combine(_root.Path, relativeUnderParent);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content);
        var asset = new Asset
        {
            Id = PalaceDb.NewId(),
            SourceFolderId = _root.Id,
            Path = full,
            FileName = Path.GetFileName(full),
            Kind = AssetKind.Image,
            DateAdded = PalaceDb.NowIso(),
            FileSize = size ?? content.Length
        };
        await _catalog.UpsertAssetAsync(asset, "");
        return (await _catalog.GetAssetByIdAsync(asset.Id))!;
    }

    private async Task<Asset> AddOutsideRejectedAsync(string relative)
    {
        var full = Path.Combine(_root.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, "keep");
        var asset = new Asset
        {
            Id = PalaceDb.NewId(),
            SourceFolderId = _root.Id,
            Path = full,
            FileName = Path.GetFileName(full),
            Kind = AssetKind.Image,
            DateAdded = PalaceDb.NowIso(),
            FileSize = 4
        };
        await _catalog.UpsertAssetAsync(asset, "");
        return (await _catalog.GetAssetByIdAsync(asset.Id))!;
    }

    [Fact]
    public void OnlyRejected_drops_files_outside_rejected()
    {
        var ok = new Asset { Path = Path.Combine("a", "_rejected", "x.jpg") };
        var no = new Asset { Path = Path.Combine("a", "x.jpg") };
        var list = DeleteRejected.OnlyRejected([ok, no]);
        Assert.Single(list);
        Assert.Same(ok, list[0]);
    }

    [Fact]
    public void InScope_filters_by_view_prefix()
    {
        var shots = new Asset { Path = Path.Combine(_dir, "Lib", "shots", "_rejected", "a.jpg") };
        var refs = new Asset { Path = Path.Combine(_dir, "Lib", "refs", "_rejected", "b.jpg") };
        var view = Path.Combine(_dir, "Lib", "shots");
        var scoped = DeleteRejected.InScope([shots, refs], view);
        Assert.Single(scoped);
        Assert.Same(shots, scoped[0]);
    }

    [Fact]
    public void InScope_source_root_keeps_count_aligned_with_mosaic()
    {
        // Two watched roots: All-rejected for Art must not inflate to whole-project.
        var artRoot = Path.Combine(_dir, "Art");
        var photoRoot = Path.Combine(_dir, "Photos");
        var artA = new Asset { Path = Path.Combine(artRoot, "_rejected", "a.jpg"), FileSize = 1 };
        var artB = new Asset { Path = Path.Combine(artRoot, "sub", "_rejected", "b.jpg"), FileSize = 2 };
        var photo = new Asset { Path = Path.Combine(photoRoot, "_rejected", "c.jpg"), FileSize = 4 };
        var all = new[] { artA, artB, photo };

        var mosaic = DeleteRejected.InScope(all, artRoot);
        Assert.Equal(2, mosaic.Count);
        Assert.Equal(3, DeleteRejected.TotalBytes(mosaic));
        Assert.DoesNotContain(mosaic, a => a.Path == photo.Path);

        // Whole-library is still available as a dialog scope, but This-view matches mosaic.
        Assert.Equal(3, DeleteRejected.OnlyRejected(all).Count);
    }

    [Fact]
    public void Actionable_recycle_skips_network_permanent_includes()
    {
        var local = new Asset { Path = Path.Combine("C:", "lib", "_rejected", "a.jpg"), FileSize = 10 };
        var net = new Asset { Path = @"\\nas\share\_rejected\b.jpg", FileSize = 20 };
        static bool IsNet(string p) => p.StartsWith(@"\\", StringComparison.Ordinal);

        var recycle = DeleteRejected.Actionable([local, net], DeleteRejectedDestination.RecycleBin, out var skipped, IsNet);
        Assert.Single(recycle);
        Assert.Single(skipped);
        Assert.Equal(net.Path, skipped[0].Path);

        var permanent = DeleteRejected.Actionable([local, net], DeleteRejectedDestination.Permanent, out var none, IsNet);
        Assert.Equal(2, permanent.Count);
        Assert.Empty(none);
    }

    [Fact]
    public void DialogActionableTotals_this_view_recycles_only_local_in_scope()
    {
        // Whole library: 10 local + 75 network. This view (Art): 5 local + 2 network.
        DeleteRejected.DialogActionableTotals(
            count: 7,
            bytes: 700,
            networkInScope: 2,
            networkBytesInScope: 200,
            destination: DeleteRejectedDestination.RecycleBin,
            out var viewActionable,
            out var viewBytes,
            out var viewNetworkShown);
        Assert.Equal(5, viewActionable);
        Assert.Equal(500, viewBytes);
        Assert.Equal(2, viewNetworkShown);
        Assert.Equal("Move 5 to Recycle Bin", DeleteRejected.ConfirmPrimaryLabel(DeleteRejectedDestination.RecycleBin, viewActionable));

        DeleteRejected.DialogActionableTotals(
            count: 85,
            bytes: 8500,
            networkInScope: 75,
            networkBytesInScope: 7500,
            destination: DeleteRejectedDestination.RecycleBin,
            out var libActionable,
            out _,
            out var libNetworkShown);
        Assert.Equal(10, libActionable);
        Assert.Equal(75, libNetworkShown);

        // Permanent ignores network skip for both scopes.
        DeleteRejected.DialogActionableTotals(
            7, 700, 2, 200, DeleteRejectedDestination.Permanent,
            out var permanentCount, out var permanentBytes, out var permanentNetwork);
        Assert.Equal(7, permanentCount);
        Assert.Equal(700, permanentBytes);
        Assert.Equal(0, permanentNetwork);
    }

    [Fact]
    public void Retry_keeps_last_destination()
    {
        // Retry must not force Recycle Bin after a permanent partial failure.
        Assert.Equal(
            DeleteRejectedDestination.Permanent,
            DeleteRejected.RetryDestination(DeleteRejectedDestination.Permanent));
        Assert.Equal(
            DeleteRejectedDestination.RecycleBin,
            DeleteRejected.RetryDestination(DeleteRejectedDestination.RecycleBin));
    }

    [Fact]
    public void NetworkSkipNote_and_status_copy()
    {
        Assert.Contains("skipped and left in _rejected", DeleteRejected.NetworkSkipNote(75));
        Assert.Contains("75 skipped (network)", DeleteRejected.StatusRecycleSuccess(37, 1_800_000_000, 75));
        Assert.Equal("Deleted 2 files permanently (100 B)", DeleteRejected.StatusPermanentSuccess(2, 100));
        Assert.Equal("35 of 37 moved to Recycle Bin · 2 in use", DeleteRejected.StatusRecyclePartial(35, 37, 2));
        Assert.Equal("Move 37 to Recycle Bin", DeleteRejected.ConfirmPrimaryLabel(DeleteRejectedDestination.RecycleBin, 37));
        Assert.Equal("Delete 112 permanently", DeleteRejected.ConfirmPrimaryLabel(DeleteRejectedDestination.Permanent, 112));
    }

    [Fact]
    public async Task Recycle_removes_files_and_catalog_and_empties_rejected_folder()
    {
        var a = await AddRejectedAsync(Path.Combine("shots", "_rejected", "a.jpg"));
        var outside = await AddOutsideRejectedAsync(Path.Combine("shots", "keep.jpg"));

        var result = await _ops.DeleteRejectedAsync([a, outside], _projectId, DeleteRejectedDestination.RecycleBin);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(1, result.Succeeded);
        Assert.False(File.Exists(a.Path));
        Assert.True(File.Exists(outside.Path));
        Assert.Null(await _catalog.GetAssetByIdAsync(a.Id));
        Assert.NotNull(await _catalog.GetAssetByIdAsync(outside.Id));
        Assert.False(Directory.Exists(Path.Combine(_root.Path, "shots", "_rejected")));
        Assert.NotNull(result.BatchId);
        Assert.True(result.CanUndo);
    }

    [Fact]
    public async Task Recycle_undo_restores_file_catalog_tags_and_rejected_folder()
    {
        var a = await AddRejectedAsync(Path.Combine("shots", "_rejected", "tagged.jpg"), "hello");
        var tag = await _catalog.GetOrCreateTagAsync("landscape");
        await _catalog.AssignTagAsync(a.Id, tag.Id, TagSource.Manual);

        var result = await _ops.DeleteRejectedAsync([a], _projectId, DeleteRejectedDestination.RecycleBin);
        Assert.True(result.Ok, result.Message);
        Assert.Null(await _catalog.GetAssetByIdAsync(a.Id));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.True(File.Exists(a.Path));
        var restored = await _catalog.GetAssetByIdAsync(a.Id);
        Assert.NotNull(restored);
        Assert.Equal(a.Path, restored!.Path);
        var tags = await _catalog.GetAssignedTagsAsync(a.Id);
        Assert.Contains(tags, t => t.TagId == tag.Id);
        Assert.True(Directory.Exists(Path.Combine(_root.Path, "shots", "_rejected")));
    }

    [Fact]
    public async Task Permanent_deletes_without_undo_journal()
    {
        var a = await AddRejectedAsync(Path.Combine("shots", "_rejected", "gone.jpg"));
        var result = await _ops.DeleteRejectedAsync([a], _projectId, DeleteRejectedDestination.Permanent);
        Assert.True(result.Ok, result.Message);
        Assert.False(File.Exists(a.Path));
        Assert.Null(result.BatchId);
        Assert.False(result.CanUndo);
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Network_share_skipped_on_recycle_left_in_rejected()
    {
        var local = await AddRejectedAsync(Path.Combine("shots", "_rejected", "local.jpg"));
        var fakeNet = await AddRejectedAsync(Path.Combine("nas", "_rejected", "remote.jpg"));

        var result = await _ops.DeleteRejectedAsync(
            [local, fakeNet],
            _projectId,
            DeleteRejectedDestination.RecycleBin,
            isNetwork: p => p.Contains($"{Path.DirectorySeparatorChar}nas{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        Assert.True(result.Ok, result.Message);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.NetworkSkipped);
        Assert.False(File.Exists(local.Path));
        Assert.True(File.Exists(fakeNet.Path));
        Assert.NotNull(await _catalog.GetAssetByIdAsync(fakeNet.Id));
        Assert.Contains("skipped (network)", result.Message);
    }

    [Fact]
    public async Task Partial_failure_keeps_locked_file_and_journals_successes()
    {
        var ok = await AddRejectedAsync(Path.Combine("shots", "_rejected", "ok.jpg"));
        var locked = await AddRejectedAsync(Path.Combine("shots", "_rejected", "locked.jpg"), "lockme");

        // Linux FileShare locks do not block File.Move — inject a trash that fails one path.
        var selective = new SelectiveTrash(_trash, failIfName: "locked.jpg");
        var ops = new FileOpService(_catalog, () => new Nop(), selective);

        var result = await ops.DeleteRejectedAsync([ok, locked], _projectId, DeleteRejectedDestination.RecycleBin);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Single(result.Failures);
        Assert.Equal(locked.Id, result.Failures[0].AssetId);
        Assert.False(File.Exists(ok.Path));
        Assert.True(File.Exists(locked.Path));
        Assert.NotNull(await _catalog.GetAssetByIdAsync(locked.Id));
        Assert.True(Directory.Exists(Path.Combine(_root.Path, "shots", "_rejected")));
        Assert.Contains("of 2 moved", result.Message);
        Assert.NotNull(result.BatchId);

        var undo = await ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.True(File.Exists(ok.Path));
    }

    private sealed class SelectiveTrash : IFileTrash
    {
        private readonly IFileTrash _inner;
        private readonly string _failIfName;

        public SelectiveTrash(IFileTrash inner, string failIfName)
        {
            _inner = inner;
            _failIfName = failIfName;
        }

        public string Recycle(string path)
        {
            if (string.Equals(Path.GetFileName(path), _failIfName, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("In use by another app");
            }

            return _inner.Recycle(path);
        }

        public bool TryRestore(string token, string originalPath) => _inner.TryRestore(token, originalPath);

        public void PermanentDelete(string path) => _inner.PermanentDelete(path);
    }

    [Fact]
    public async Task Never_deletes_outside_rejected_even_if_passed()
    {
        var keep = await AddOutsideRejectedAsync(Path.Combine("shots", "safe.jpg"));
        var result = await _ops.DeleteRejectedAsync([keep], _projectId, DeleteRejectedDestination.Permanent);
        Assert.False(result.Ok);
        Assert.True(File.Exists(keep.Path));
        Assert.NotNull(await _catalog.GetAssetByIdAsync(keep.Id));
    }

    [Fact]
    public async Task Scope_this_view_vs_whole_library()
    {
        var shots = await AddRejectedAsync(Path.Combine("shots", "_rejected", "a.jpg"));
        var refs = await AddRejectedAsync(Path.Combine("refs", "_rejected", "b.jpg"));
        var all = await _catalog.GetAssetsAsync(projectId: _projectId);
        var view = DeleteRejected.InScope(all, Path.Combine(_root.Path, "shots"));
        Assert.Single(view);

        var result = await _ops.DeleteRejectedAsync(view, _projectId, DeleteRejectedDestination.RecycleBin);
        Assert.Equal(1, result.Succeeded);
        Assert.Null(await _catalog.GetAssetByIdAsync(shots.Id));
        Assert.NotNull(await _catalog.GetAssetByIdAsync(refs.Id));
    }

    [Fact]
    public void ShouldBackup_at_threshold()
    {
        Assert.False(DeleteRejected.ShouldBackup(DeleteRejected.BackupThreshold - 1));
        Assert.True(DeleteRejected.ShouldBackup(DeleteRejected.BackupThreshold));
    }

    [Fact]
    public void IsNetworkShare_unc()
    {
        Assert.True(NetworkPaths.IsNetworkShare(@"\\server\share\file.jpg"));
        Assert.False(NetworkPaths.IsNetworkShare(Path.Combine(_dir, "local.jpg")));
    }
}
