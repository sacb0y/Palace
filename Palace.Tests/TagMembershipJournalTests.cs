using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Drag / Add to group journals Added-only; Undo keeps pre-existing memberships.
/// </summary>
public sealed class TagMembershipJournalTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-tag-mem-" + Guid.NewGuid().ToString("N"));
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
    public async Task AddMembership_ResultEnum_DistinguishesAlreadyAndCycle()
    {
        var hair = await _catalog.CreateTagAsync("Hair");
        var bangs = await _catalog.CreateTagAsync("bangs");
        Assert.Equal(MembershipAddResult.Added, await _catalog.AddMembershipAsync(hair.Id, bangs.Id));
        Assert.Equal(MembershipAddResult.AlreadyMember, await _catalog.AddMembershipAsync(hair.Id, bangs.Id));
        Assert.Equal(MembershipAddResult.CycleRefuse, await _catalog.AddMembershipAsync(hair.Id, hair.Id));
        Assert.Equal(MembershipAddResult.CycleRefuse, await _catalog.AddMembershipAsync(bangs.Id, hair.Id));
    }

    [Fact]
    public async Task MixedDrop_JournalsAddedOnly_UndoKeepsPreexisting()
    {
        var hair = await _catalog.CreateTagAsync("Hair");
        var already = await _catalog.CreateTagAsync("already");
        var fresh = await _catalog.CreateTagAsync("fresh");
        Assert.Equal(MembershipAddResult.Added, await _catalog.AddMembershipAsync(hair.Id, already.Id));

        var result = await _ops.AddTagsToGroupAsync([already.Id, fresh.Id], hair.Id, _projectId);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.AlreadyMember);
        Assert.Equal(0, result.CycleRefuse);
        Assert.NotNull(result.BatchId);
        Assert.Contains("Added 1 tag to Hair", result.Message);
        Assert.Contains("1 already in group", result.Message);

        var peek = await _ops.PeekUndoAsync(_projectId);
        Assert.NotNull(peek);
        Assert.Equal(FileOpKind.TagMembership, peek!.Kind);
        Assert.Single(peek.Items);
        Assert.Equal(FileOpStep.AddMembership, peek.Items[0].Step);
        Assert.Equal(fresh.Id, peek.Items[0].NewPath);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);

        var parentsAlready = await _catalog.GetParentsAsync(already.Id);
        Assert.Contains(parentsAlready, p => p.Id == hair.Id);
        var parentsFresh = await _catalog.GetParentsAsync(fresh.Id);
        Assert.DoesNotContain(parentsFresh, p => p.Id == hair.Id);
    }

    [Fact]
    public async Task NewGroupAndAdd_OneUndoableStep()
    {
        var knight = await _catalog.CreateTagAsync("knight");
        var result = await _ops.CreateGroupAndAddTagsAsync([knight.Id], _projectId, "New group");
        Assert.True(result.Ok, result.Message);
        Assert.Equal(1, result.Added);
        Assert.NotNull(result.CreatedGroupId);
        Assert.NotNull(result.BatchId);
        Assert.Contains("Created", result.Message);

        var peek = await _ops.PeekUndoAsync(_projectId);
        Assert.NotNull(peek);
        Assert.Equal(FileOpKind.TagMembership, peek!.Kind);
        Assert.Contains(peek.Items, i => i.Step == FileOpStep.CreateTag);
        Assert.Contains(peek.Items, i => i.Step == FileOpStep.AddMembership && i.NewPath == knight.Id);

        var undo = await _ops.UndoLastAsync(_projectId);
        Assert.True(undo.Ok, undo.Message);
        var parents = await _catalog.GetParentsAsync(knight.Id);
        Assert.Empty(parents);
        var tags = await _catalog.GetTagsAsync();
        Assert.DoesNotContain(tags, t => t.Id == result.CreatedGroupId);
    }

    /// <summary>
    /// Mid-batch membership fault (after 2nd Added) rolls back every edge and journals nothing
    /// — same probe pattern as TagT2 / TagBulkFailureSafetyTests.
    /// </summary>
    [Fact]
    public async Task MidBatchMembershipFault_RollsBackAllAndJournalsNothing()
    {
        var hair = await _catalog.CreateTagAsync("Hair");
        var a = await _catalog.CreateTagAsync("alpha");
        var b = await _catalog.CreateTagAsync("beta");
        var c = await _catalog.CreateTagAsync("gamma");

        // First Added succeeds inside the tx; second faults → full rollback.
        _catalog.TestBulkTagFault = new BulkTagFaultProbe { FailAfterMutations = 2 };
        var result = await _ops.AddTagsToGroupAsync([a.Id, b.Id, c.Id], hair.Id, _projectId);

        Assert.False(result.Ok);
        Assert.Equal(CatalogService.BulkTagAbortedMessage, result.Message);
        Assert.Null(result.BatchId);
        Assert.DoesNotContain(await _catalog.GetParentsAsync(a.Id), p => p.Id == hair.Id);
        Assert.DoesNotContain(await _catalog.GetParentsAsync(b.Id), p => p.Id == hair.Id);
        Assert.DoesNotContain(await _catalog.GetParentsAsync(c.Id), p => p.Id == hair.Id);
        Assert.Null(await _ops.PeekUndoAsync(_projectId));
    }
}
