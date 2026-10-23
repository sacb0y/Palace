using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// View-model-level batch Reject loop via <see cref="BatchRejectFlow"/>
/// (same path as LibraryViewModel.RejectSelectedAsync).
/// </summary>
public sealed class BatchRejectFlowTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-reject-flow-" + Guid.NewGuid().ToString("N"));
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
        var path = Path.Combine(_dir, "A");
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

    private string Dir(string relative)
    {
        var path = Path.Combine(_root.Path, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Cancel after folder 1: one Undo restores exactly folder 1; folder 2 stays put.
    /// </summary>
    [Fact]
    public async Task Cancel_after_folder1_one_undo_restores_only_folder1()
    {
        var shots = Dir("shots");
        var refs = Dir("refs");
        var a = await AddAssetAsync(Path.Combine("shots", "a.png"), "a");
        var b = await AddAssetAsync(Path.Combine("refs", "b.png"), "b");

        var groups = new[]
        {
            new BatchRejectFlow.FolderGroup(
                Path.Combine(shots, ReviewReject.FolderName),
                shots,
                [a]),
            new BatchRejectFlow.FolderGroup(
                Path.Combine(refs, ReviewReject.FolderName),
                refs,
                [b])
        };

        using var cts = new CancellationTokenSource();
        var folderIndex = 0;
        var run = await BatchRejectFlow.RunAsync(
            groups,
            async (parent, name) =>
            {
                var created = await _ops.CreateFolderAsync(parent, name, _projectId);
                return new BatchRejectFlow.CreateFolderResult(created.Ok, created.Path, created.Message);
            },
            async (assets, dest, absorb, prior, ct) =>
            {
                var result = await _ops.MoveAssetsAsync(
                    assets,
                    dest,
                    _projectId,
                    cancellationToken: ct,
                    absorbCreatedFolder: absorb,
                    absorbPriorBatchId: prior);
                folderIndex++;
                if (folderIndex == 1)
                {
                    // Cancel before folder 2 — same as busy-bar Cancel mid-Reject.
                    cts.Cancel();
                }

                return new BatchRejectFlow.MoveResult(
                    result.Moves.Count,
                    result.BatchId,
                    result.AbsorbedPriorBatch,
                    result.Message);
            },
            () => _catalog.GetUndoableFileOpsAsync(_projectId),
            cts.Token);

        Assert.True(run.Cancelled);
        Assert.False(run.Interrupted);
        Assert.Equal(1, run.Moved);
        Assert.Single(run.UndoBatchIds);
        Assert.False(File.Exists(a.Path));
        Assert.True(File.Exists(b.Path));

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        Assert.True(File.Exists(a.Path));
        Assert.True(File.Exists(b.Path));
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }

    [Fact]
    public async Task Interrupt_when_intervening_blocks_absorb_leaves_folder1_undoable()
    {
        var shots = Dir("shots");
        var refs = Dir("refs");
        var a = await AddAssetAsync(Path.Combine("shots", "a.png"), "a");
        var b = await AddAssetAsync(Path.Combine("refs", "b.png"), "b");
        var extra = await AddAssetAsync("extra.png", "x");

        var groups = new[]
        {
            new BatchRejectFlow.FolderGroup(
                Path.Combine(shots, ReviewReject.FolderName),
                shots,
                [a]),
            new BatchRejectFlow.FolderGroup(
                Path.Combine(refs, ReviewReject.FolderName),
                refs,
                [b])
        };

        var folderIndex = 0;
        var run = await BatchRejectFlow.RunAsync(
            groups,
            async (parent, name) =>
            {
                var created = await _ops.CreateFolderAsync(parent, name, _projectId);
                return new BatchRejectFlow.CreateFolderResult(created.Ok, created.Path, created.Message);
            },
            async (assets, dest, absorb, prior, ct) =>
            {
                var result = await _ops.MoveAssetsAsync(
                    assets,
                    dest,
                    _projectId,
                    cancellationToken: ct,
                    absorbCreatedFolder: absorb,
                    absorbPriorBatchId: prior);
                folderIndex++;
                if (folderIndex == 1)
                {
                    // Interleave a Move so folder 2 cannot absorb.
                    var other = Dir("Other");
                    await _ops.MoveAssetsAsync([extra], other, _projectId);
                }

                return new BatchRejectFlow.MoveResult(
                    result.Moves.Count,
                    result.BatchId,
                    result.AbsorbedPriorBatch,
                    result.Message);
            },
            () => _catalog.GetUndoableFileOpsAsync(_projectId),
            CancellationToken.None);

        Assert.True(run.Interrupted);
        Assert.Equal(1, run.Moved);
        Assert.Equal(
            BatchReject.InterruptedMessage(1),
            run.LastMessage);
        Assert.True(File.Exists(b.Path)); // never moved
        Assert.False(File.Exists(a.Path));
    }

    /// <summary>
    /// Race: CanAbsorbPrior says safe, but Move returns AbsorbedPriorBatch
    /// false — interrupt, keep both batch ids, never attempt folder 3.
    /// </summary>
    [Fact]
    public async Task Race_folder2_not_absorbed_interrupts_before_folder3()
    {
        var a = new Asset { Id = "a", Path = Path.Combine(_root.Path, "shots", "a.png") };
        var b = new Asset { Id = "b", Path = Path.Combine(_root.Path, "refs", "b.png") };
        var c = new Asset { Id = "c", Path = Path.Combine(_root.Path, "misc", "c.png") };
        var groups = new[]
        {
            new BatchRejectFlow.FolderGroup(
                Path.Combine(_root.Path, "shots", ReviewReject.FolderName),
                Path.Combine(_root.Path, "shots"),
                [a]),
            new BatchRejectFlow.FolderGroup(
                Path.Combine(_root.Path, "refs", ReviewReject.FolderName),
                Path.Combine(_root.Path, "refs"),
                [b]),
            new BatchRejectFlow.FolderGroup(
                Path.Combine(_root.Path, "misc", ReviewReject.FolderName),
                Path.Combine(_root.Path, "misc"),
                [c])
        };

        var moveCalls = 0;
        var run = await BatchRejectFlow.RunAsync(
            groups,
            (_, _) => Task.FromResult(new BatchRejectFlow.CreateFolderResult(true, "unused", "")),
            (_, _, _, prior, _) =>
            {
                moveCalls++;
                return moveCalls switch
                {
                    1 => Task.FromResult(new BatchRejectFlow.MoveResult(
                        1, "batch-1", AbsorbedPriorBatch: false, "moved1")),
                    2 => Task.FromResult(new BatchRejectFlow.MoveResult(
                        1, "batch-2", AbsorbedPriorBatch: false, "moved2")),
                    _ => throw new InvalidOperationException("folder 3 must not be attempted")
                };
            },
            // CanAbsorbPrior before folder 2: prior still tip → safe, then Move lies.
            () => Task.FromResult<IReadOnlyList<FileOpBatch>>(
            [
                new FileOpBatch
                {
                    Id = "batch-1",
                    Kind = FileOpKind.Move,
                    Items =
                    [
                        new FileOpItem { Step = FileOpStep.MoveFile, NewPath = "x" }
                    ]
                }
            ]),
            CancellationToken.None);

        Assert.True(run.Interrupted);
        Assert.Equal(2, run.Moved);
        Assert.Equal(2, moveCalls);
        Assert.Equal(["batch-1", "batch-2"], run.UndoBatchIds);
        Assert.Equal(BatchReject.InterruptedMessage(2), run.LastMessage);
    }
}
