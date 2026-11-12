using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>
/// Outcome of a bulk tag assign/remove that mutates AssetTag rows and journals in one SQLite transaction.
/// </summary>
public sealed class BulkTagBatchResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string? Summary { get; init; }
    public string? BatchId { get; init; }

    public static BulkTagBatchResult Fail(string message) => new() { Ok = false, Message = message };
}

/// <summary>
/// Test-only fault injection for bulk tag transaction coverage. Production leaves this null.
/// Throw from the probe to abort before Commit — SQLite rolls back mutates + journal together.
/// </summary>
public sealed class BulkTagFaultProbe
{
    /// <summary>Throw after this many AssetTag writes (1-based). 0 = never.</summary>
    public int FailAfterMutations { get; init; }

    /// <summary>Throw after all mutates, immediately before journal insert.</summary>
    public bool FailBeforeJournal { get; init; }

    /// <summary>
    /// Throw after the FileOpBatch header and this many FileOpItem inserts
    /// (0 = after header only; 1 = header + first item; …). null = never.
    /// Proves a mid-journal abort still rolls back tags <b>and</b> journal rows.
    /// </summary>
    public int? FailAfterJournalItems { get; init; }
}
/// <summary>Catalog: bulk assign/remove + FileOp journal in one write transaction (T7 failure-safety).</summary>
public sealed partial class CatalogService
{
    /// <summary>Test-only. Cleared after each bulk call that observes it.</summary>
    public BulkTagFaultProbe? TestBulkTagFault { get; set; }

    public const string BulkTagAbortedMessage =
        "Couldn't finish tagging — nothing was changed.";

    /// <summary>
    /// Assign many tags × many assets and journal <see cref="FileOpKind.TagAssign"/> in
    /// <b>one</b> SQLite transaction. Mid-loop or journal failure rolls everything back.
    /// Caller must already resolve alias→ideal and drop <see cref="TagSource.Implied"/>.
    /// <paramref name="createdTags"/> are Tag rows inserted for this apply; journaled as
    /// <see cref="FileOpStep.CreateTag"/> before AssignTag so Undo deletes unused rows.
    /// </summary>
    public Task<BulkTagBatchResult> ApplyBulkAssignJournaledAsync(
        string projectId,
        IReadOnlyList<string> assetIds,
        IReadOnlyList<(string TagId, string TagName, TagSource Source)> tags,
        IReadOnlyList<(string TagId, string TagName)>? createdTags = null)
    {
        var rows = new List<(string AssetId, string TagId, string TagName, TagSource Source)>();
        foreach (var (tagId, tagName, source) in tags ?? [])
        {
            foreach (var assetId in assetIds ?? [])
            {
                rows.Add((assetId, tagId, tagName, source));
            }
        }

        return ApplyBulkAssignRowsJournaledAsync(projectId, rows, createdTags);
    }

    /// <summary>
    /// Sparse assign: each row is one (asset, tag) pair. One
    /// <see cref="FileOpKind.TagAssign"/> journal batch (From-prompt Accept all / multi).
    /// </summary>
    public Task<BulkTagBatchResult> ApplyBulkAssignRowsJournaledAsync(
        string projectId,
        IReadOnlyList<(string AssetId, string TagId, string TagName, TagSource Source)> rows,
        IReadOnlyList<(string TagId, string TagName)>? createdTags = null) =>
        _db.WriteAsync(conn =>
        {
            var probe = TakeBulkTagFault();
            using var tx = conn.BeginTransaction();
            var createItems = new List<FileOpItem>();
            if (createdTags is { Count: > 0 })
            {
                foreach (var (tagId, tagName) in createdTags)
                {
                    if (string.IsNullOrEmpty(tagId))
                    {
                        continue;
                    }

                    createItems.Add(new FileOpItem
                    {
                        Step = FileOpStep.CreateTag,
                        OldPath = tagId,
                        NewPath = tagName
                    });
                }
            }

            var items = new List<FileOpItem>();
            var appliedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mutations = 0;
            var distinctTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var (assetId, tagId, tagName, source) in rows ?? [])
                {
                    if (string.IsNullOrEmpty(assetId) || string.IsNullOrEmpty(tagId))
                    {
                        continue;
                    }

                    if (source == TagSource.Implied)
                    {
                        continue;
                    }

                    distinctTags.Add(tagName);
                    var before = LoadAssignedRefs(conn, assetId, tx);
                    UpsertAssetTag(conn, assetId, tagId, source, tx);
                    mutations++;
                    ThrowIfMutationFault(probe, mutations);
                    RefreshFts(conn, assetId, tx);

                    var after = LoadAssignedRefs(conn, assetId, tx);
                    var change = TagAssignUndo.PlanAssign(before, after, tagId);
                    if (!change.Journal)
                    {
                        continue;
                    }

                    appliedNames.Add(tagName);
                    items.Add(new FileOpItem
                    {
                        Step = FileOpStep.AssignTag,
                        AssetId = assetId,
                        OldPath = tagId,
                        NewPath = tagName,
                        // KeepAssigned: prior applied source for Undo restore.
                        // TouchAssigned: applied source (unused on Undo delete).
                        OldSourceId = change.PriorAssignedSource?.ToString() ?? source.ToString(),
                        NewSourceId = TagAssignUndo.Encode(change.ImpliedIds, change.TouchAssignedTag)
                    });
                }

                // CreateTag first (lower Seq) so Undo (descending Seq) strips AssignTag, then Tag row.
                var journalItems = new List<FileOpItem>(createItems.Count + items.Count);
                journalItems.AddRange(createItems);
                journalItems.AddRange(items);

                if (journalItems.Count == 0)
                {
                    tx.Commit();
                    var already = distinctTags.Count == 1
                        ? $"{distinctTags.First()} was already on the selection."
                        : "Those tags were already on the selection.";
                    return new BulkTagBatchResult { Ok = true, Message = already, Summary = already };
                }

                ThrowIfBeforeJournalFault(probe);
                string summary;
                if (items.Count == 0)
                {
                    summary = createItems.Count == 1
                        ? $"Created tag {createItems[0].NewPath}"
                        : $"Created {createItems.Count} tags";
                }
                else
                {
                    var assetCount = items.Select(i => i.AssetId).Distinct(StringComparer.Ordinal).Count();
                    summary = appliedNames.Count == 1
                        ? $"Tagged {AssetNouns.Count(assetCount)} with {appliedNames.First()}"
                        : $"Tagged {AssetNouns.Count(assetCount)} with {appliedNames.Count} tags";
                }

                var batchId = InsertJournaledBatch(
                    conn, tx, projectId, FileOpKind.TagAssign, summary, journalItems, probe);
                tx.Commit();
                return new BulkTagBatchResult
                {
                    Ok = true,
                    Message = summary + ".",
                    BatchId = batchId,
                    Summary = summary
                };
            }
            catch (BulkTagFaultException)
            {
                return BulkTagBatchResult.Fail(BulkTagAbortedMessage);
            }
        });

    /// <summary>
    /// Tags-page batch create (t2-03b): insert / find-or-nest tags and journal
    /// <see cref="FileOpKind.TagCreate"/> (<see cref="FileOpStep.CreateTag"/> then
    /// <see cref="FileOpStep.AddMembership"/>) in one transaction.
    /// </summary>
    public Task<TagCreateBatchResult> ApplyCreateTagsJournaledAsync(
        string projectId,
        IEnumerable<string> names,
        string? parentId) =>
        _db.WriteAsync(conn =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return new TagCreateBatchResult
                {
                    Ok = false,
                    Message = "Open a project first."
                };
            }

            var probe = TakeBulkTagFault();
            using var tx = conn.BeginTransaction();
            try
            {
                var journal = new List<FileOpItem>();
                var result = CreateTagsCore(conn, names, parentId, tx, journal);
                if (result.Created == 0 && result.Existed == 0)
                {
                    tx.Commit();
                    return new TagCreateBatchResult
                    {
                        Ok = true,
                        Message = "Enter one or more tag names.",
                        Tags = result.Tags,
                        CreatedTagIds = result.CreatedTagIds,
                        GroupName = result.GroupName
                    };
                }

                if (journal.Count == 0)
                {
                    // Existing tags already in the group — nothing to journal.
                    tx.Commit();
                    var existedMsg = TagCreateCopy.Describe(result, parentId is null ? null : " in the group");
                    return new TagCreateBatchResult
                    {
                        Created = result.Created,
                        Existed = result.Existed,
                        MembershipRejected = result.MembershipRejected,
                        Tags = result.Tags,
                        CreatedTagIds = result.CreatedTagIds,
                        GroupName = result.GroupName,
                        Ok = true,
                        Message = existedMsg
                    };
                }

                ThrowIfBeforeJournalFault(probe);
                var createdNames = result.Tags
                    .Where(t => result.CreatedTagIds.Contains(t.Id))
                    .Select(t => t.Name)
                    .ToList();
                // Prefer CreatedInGroup wording when any tag was newly inserted.
                var status = result.Created > 0
                    ? TagStatusBar.CreatedInGroup(
                        result.Created,
                        result.GroupName,
                        createdNames.Count > 0
                            ? createdNames
                            : result.Tags.Select(t => t.Name).ToList())
                    : TagCreateCopy.Describe(result, parentId is null ? null : " in the group");
                var summary = status.StartsWith(TagStatusBar.CheckMark, StringComparison.Ordinal)
                    ? status[TagStatusBar.CheckMark.Length..].TrimStart()
                    : status;
                var batchId = InsertJournaledBatch(
                    conn, tx, projectId, FileOpKind.TagCreate, summary, journal, probe);
                tx.Commit();
                return new TagCreateBatchResult
                {
                    Created = result.Created,
                    Existed = result.Existed,
                    MembershipRejected = result.MembershipRejected,
                    Tags = result.Tags,
                    CreatedTagIds = result.CreatedTagIds,
                    GroupName = result.GroupName,
                    BatchId = batchId,
                    Ok = true,
                    Message = status
                };
            }
            catch (BulkTagFaultException)
            {
                return new TagCreateBatchResult
                {
                    Ok = false,
                    Message = BulkTagAbortedMessage
                };
            }
        });

    /// <summary>
    /// Add tags to a group (drag / Add to group ›). Journals only
    /// <see cref="MembershipAddResult.Added"/> edges under <see cref="FileOpKind.TagMembership"/>.
    /// When <paramref name="createGroupName"/> is set, creates that group first and adds in one batch.
    /// </summary>
    public Task<TagMembershipBatchResult> ApplyAddMembershipJournaledAsync(
        string projectId,
        IReadOnlyList<string> childIds,
        string? parentId,
        string? createGroupName = null) =>
        _db.WriteAsync(conn =>
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return TagMembershipBatchResult.Fail("Open a project first.");
            }

            var ids = childIds
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (ids.Count == 0 && string.IsNullOrWhiteSpace(createGroupName))
            {
                return TagMembershipBatchResult.Fail("Select a tag first.");
            }

            var probe = TakeBulkTagFault();
            using var tx = conn.BeginTransaction();
            try
            {
                var journal = new List<FileOpItem>();
                string? groupId = parentId;
                string? groupName = null;
                string? createdGroupId = null;
                var createdGroup = false;

                if (!string.IsNullOrWhiteSpace(createGroupName))
                {
                    var name = UniqueGroupName(conn, createGroupName.Trim(), tx);
                    var group = InsertTag(conn, name, 0, tx);
                    createdGroupId = group.Id;
                    groupId = group.Id;
                    groupName = group.Name;
                    createdGroup = true;
                    journal.Add(new FileOpItem
                    {
                        Step = FileOpStep.CreateTag,
                        OldPath = group.Id,
                        NewPath = group.Name
                    });
                }
                else if (string.IsNullOrEmpty(groupId))
                {
                    return TagMembershipBatchResult.Fail("Pick a group first.");
                }
                else
                {
                    using var nameCmd = conn.CreateCommand();
                    nameCmd.Transaction = tx;
                    nameCmd.CommandText = "SELECT Name FROM Tag WHERE Id = $id";
                    nameCmd.Parameters.AddWithValue("$id", groupId);
                    groupName = nameCmd.ExecuteScalar() as string;
                    if (groupName is null)
                    {
                        return TagMembershipBatchResult.Fail("That group no longer exists.");
                    }
                }

                var added = 0;
                var already = 0;
                var cycle = 0;
                foreach (var childId in ids)
                {
                    var result = TryAddMembership(conn, groupId!, childId, tx);
                    switch (result)
                    {
                        case MembershipAddResult.Added:
                            added++;
                            journal.Add(new FileOpItem
                            {
                                Step = FileOpStep.AddMembership,
                                OldPath = groupId!,
                                NewPath = childId
                            });
                            // Mid-batch fault → dispose rolls back every Added + CreateTag.
                            ThrowIfMutationFault(probe, added);
                            break;
                        case MembershipAddResult.AlreadyMember:
                            already++;
                            break;
                        case MembershipAddResult.CycleRefuse:
                            cycle++;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(result), result, null);
                    }
                }

                if (journal.Count == 0)
                {
                    tx.Commit();
                    return new TagMembershipBatchResult
                    {
                        Added = 0,
                        AlreadyMember = already,
                        CycleRefuse = cycle,
                        GroupId = groupId,
                        GroupName = groupName,
                        Ok = true,
                        Message = TagMembershipAdd.StatusMessage(0, already, cycle, groupName)
                    };
                }

                if (added > 0 || createdGroup)
                {
                    InvalidateGraphCaches();
                }

                ThrowIfBeforeJournalFault(probe);
                var status = TagMembershipAdd.StatusMessage(
                    added, already, cycle, groupName, createdGroup);
                var summary = TagMembershipAdd.JournalSummary(added, groupName, createdGroup);
                var batchId = InsertJournaledBatch(
                    conn, tx, projectId, FileOpKind.TagMembership, summary, journal, probe);
                tx.Commit();
                return new TagMembershipBatchResult
                {
                    Added = added,
                    AlreadyMember = already,
                    CycleRefuse = cycle,
                    GroupId = groupId,
                    GroupName = groupName,
                    CreatedGroupId = createdGroupId,
                    BatchId = batchId,
                    Ok = true,
                    Message = status
                };
            }
            catch (BulkTagFaultException)
            {
                return TagMembershipBatchResult.Fail(BulkTagAbortedMessage);
            }
        });

    private static string UniqueGroupName(SqliteConnection conn, string preferred, SqliteTransaction tx)
    {
        var name = string.IsNullOrWhiteSpace(preferred) ? "New group" : preferred.Trim();
        if (FindTagByName(conn, name, tx) is null)
        {
            return name;
        }

        var n = 2;
        while (true)
        {
            var candidate = $"{name} ({n++})";
            if (FindTagByName(conn, candidate, tx) is null)
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Remove tags across assets and journal <see cref="FileOpKind.TagRemove"/> in one transaction.
    /// <paramref name="sourceFallback"/> is used when an asset lacks the row (same as prior FileOp path).
    /// Implied rows are swept without journaling (never rematerialized on Undo).
    /// </summary>
    public Task<BulkTagBatchResult> ApplyBulkRemoveJournaledAsync(
        string projectId,
        IReadOnlyList<(string AssetId, TagSource SourceFallback)> rows,
        IReadOnlyList<(string TagId, string TagName)> tags) =>
        _db.WriteAsync(conn =>
        {
            var probe = TakeBulkTagFault();
            using var tx = conn.BeginTransaction();
            var items = new List<FileOpItem>();
            var removedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mutations = 0;
            try
            {
                foreach (var (tagId, tagName) in tags)
                {
                    foreach (var (assetId, sourceFallback) in rows)
                    {
                        var before = LoadAssignedRefs(conn, assetId, tx);
                        var recorded = TagAssignUndo.RecordedSource(before, tagId, sourceFallback);
                        if (recorded == TagSource.Implied)
                        {
                            DeleteAssetTag(conn, assetId, tagId, tx);
                            mutations++;
                            ThrowIfMutationFault(probe, mutations);
                            RefreshFts(conn, assetId, tx);
                            continue;
                        }

                        DeleteAssetTag(conn, assetId, tagId, tx);
                        mutations++;
                        ThrowIfMutationFault(probe, mutations);
                        RefreshFts(conn, assetId, tx);

                        var after = LoadAssignedRefs(conn, assetId, tx);
                        var change = TagAssignUndo.PlanRemove(before, after, tagId);
                        if (!change.Journal)
                        {
                            continue;
                        }

                        removedNames.Add(tagName);
                        items.Add(new FileOpItem
                        {
                            Step = FileOpStep.RemoveTag,
                            AssetId = assetId,
                            OldPath = tagId,
                            NewPath = tagName,
                            OldSourceId = recorded.ToString(),
                            NewSourceId = TagAssignUndo.Encode(change.ImpliedIds, change.TouchAssignedTag)
                        });
                    }
                }

                if (items.Count == 0)
                {
                    tx.Commit();
                    var missing = tags.Count == 1
                        ? $"{tags[0].TagName} was not on the selection."
                        : "Those tags were not on the selection.";
                    return new BulkTagBatchResult { Ok = true, Message = missing, Summary = missing };
                }

                ThrowIfBeforeJournalFault(probe);
                var assetCount = items.Select(i => i.AssetId).Distinct(StringComparer.Ordinal).Count();
                var summary = removedNames.Count == 1
                    ? $"Removed {removedNames.First()} from {AssetNouns.Count(assetCount)}"
                    : $"Removed {removedNames.Count} tags from {AssetNouns.Count(assetCount)}";
                var batchId = InsertJournaledBatch(
                    conn, tx, projectId, FileOpKind.TagRemove, summary, items, probe);
                tx.Commit();
                return new BulkTagBatchResult
                {
                    Ok = true,
                    Message = summary + ".",
                    BatchId = batchId,
                    Summary = summary
                };
            }
            catch (BulkTagFaultException)
            {
                return BulkTagBatchResult.Fail(BulkTagAbortedMessage);
            }
        });

    /// <summary>
    /// Snapshot + delete a tag and journal <see cref="FileOpKind.TagDelete"/> in
    /// <b>one</b> SQLite transaction. Journal failure rolls the delete back (no compensate).
    /// </summary>
    public Task<BulkTagBatchResult> ApplyDeleteTagJournaledAsync(string projectId, string tagId) =>
        _db.WriteAsync(conn =>
        {
            if (string.IsNullOrEmpty(tagId))
            {
                return BulkTagBatchResult.Fail("That tag is gone.");
            }

            var probe = TakeBulkTagFault();
            InvalidateGraphCaches();
            using var tx = conn.BeginTransaction();
            try
            {
                var snapshot = ReadTagSnapshot(conn, tagId, tx);
                if (snapshot is null)
                {
                    return BulkTagBatchResult.Fail("That tag is gone.");
                }

                var name = snapshot.Tag.Name;
                var assetIds = AssetsHavingAnyTag(conn, [tagId], tx);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM Tag WHERE Id = $id";
                    cmd.Parameters.AddWithValue("$id", tagId);
                    cmd.ExecuteNonQuery();
                }

                ThrowIfMutationFault(probe, mutations: 1);
                foreach (var assetId in assetIds)
                {
                    RefreshFts(conn, assetId, tx);
                }

                ThrowIfBeforeJournalFault(probe);
                var items = new List<FileOpItem>
                {
                    new()
                    {
                        Step = FileOpStep.DeleteTag,
                        OldPath = tagId,
                        NewPath = SerializeTagSnapshot(snapshot)
                    }
                };
                var summary = $"Deleted {name}";
                var batchId = InsertJournaledBatch(
                    conn, tx, projectId, FileOpKind.TagDelete, summary, items, probe);
                tx.Commit();

                var impact = AssetNouns.Count(snapshot.Assignments.Select(a => a.AssetId).Distinct().Count());
                return new BulkTagBatchResult
                {
                    Ok = true,
                    Message = $"Deleted “{name}” ({impact}).",
                    BatchId = batchId,
                    Summary = summary
                };
            }
            catch (BulkTagFaultException)
            {
                return BulkTagBatchResult.Fail(BulkTagAbortedMessage);
            }
        });

    /// <summary>
    /// Add alias→ideal (with AssetTag migrate) and journal <see cref="FileOpKind.TagAliasAdd"/>
    /// in <b>one</b> SQLite transaction whenever the edge is new (Undo drops it and restores any
    /// migrated rows). An edge that already existed commits without a journal row.
    /// Journal failure rolls back.
    /// </summary>
    public Task<BulkTagBatchResult> ApplyAddAliasJournaledAsync(
        string projectId,
        string aliasTagId,
        string idealTagId) =>
        _db.WriteAsync(conn =>
        {
            if (string.IsNullOrEmpty(aliasTagId) || string.IsNullOrEmpty(idealTagId))
            {
                return BulkTagBatchResult.Fail("Pick two tags.");
            }

            var probe = TakeBulkTagFault();
            InvalidateGraphCaches();
            using var tx = conn.BeginTransaction();
            try
            {
                var resolvedIdeal = ResolveIdealTagId(conn, idealTagId, tx);
                if (!TryAddAlias(conn, aliasTagId, resolvedIdeal, tx, out var inserted))
                {
                    return BulkTagBatchResult.Fail("That alias is not allowed.");
                }

                IReadOnlyList<AssetTag> migrated = [];
                if (inserted)
                {
                    migrated = MigrateAssetTagsAliasToIdeal(conn, aliasTagId, resolvedIdeal, tx);
                    ThrowIfMutationFault(probe, mutations: 1);
                    foreach (var assetId in migrated.Select(m => m.AssetId).Distinct(StringComparer.Ordinal))
                    {
                        RefreshFts(conn, assetId, tx);
                    }
                }

                if (!inserted)
                {
                    tx.Commit();
                    return new BulkTagBatchResult
                    {
                        Ok = true,
                        Message = "Alias added.",
                        Summary = "Alias added"
                    };
                }

                // Journal even when no AssetTag rows moved so Tags Undo can drop the edge (al-01).
                ThrowIfBeforeJournalFault(probe);
                var payload = SerializeAliasMigration(migrated);
                var items = new List<FileOpItem>
                {
                    new()
                    {
                        Step = FileOpStep.AddAlias,
                        OldPath = aliasTagId,
                        NewPath = resolvedIdeal,
                        NewSourceId = payload
                    }
                };
                var summary = migrated.Count == 0
                    ? "Aliased tag"
                    : $"Aliased tag ({AssetNouns.Count(migrated.Count)} updated)";
                var batchId = InsertJournaledBatch(
                    conn, tx, projectId, FileOpKind.TagAliasAdd, summary, items, probe);
                tx.Commit();
                return new BulkTagBatchResult
                {
                    Ok = true,
                    Message = summary + ".",
                    BatchId = batchId,
                    Summary = summary
                };
            }
            catch (BulkTagFaultException)
            {
                return BulkTagBatchResult.Fail(BulkTagAbortedMessage);
            }
        });

    /// <summary>
    /// Alias manager "add" for names that are not tags yet: insert each Tag row and its alias edge in
    /// <b>one</b> transaction and journal them as one <see cref="FileOpKind.TagAliasAdd"/> batch
    /// (<see cref="FileOpStep.CreateTag"/> then <see cref="FileOpStep.AddAlias"/> per name), so one Undo
    /// removes the edges and the unused tags. Names that already match a tag are skipped.
    /// </summary>
    public Task<BulkTagBatchResult> ApplyAddNewAliasesJournaledAsync(
        string projectId,
        string idealTagId,
        IReadOnlyList<string> names) =>
        _db.WriteAsync(conn =>
        {
            if (string.IsNullOrEmpty(idealTagId))
            {
                return BulkTagBatchResult.Fail("Pick a tag first.");
            }

            var probe = TakeBulkTagFault();
            InvalidateGraphCaches();
            using var tx = conn.BeginTransaction();
            try
            {
                var resolvedIdeal = ResolveIdealTagId(conn, idealTagId, tx);
                var idealName = TagNameOf(conn, resolvedIdeal, tx);
                var items = new List<FileOpItem>();
                var added = new List<string>();
                foreach (var raw in TagNameList.Split(string.Join('\n', names)))
                {
                    if (FindTagByName(conn, raw, tx) is not null)
                    {
                        continue;
                    }

                    var tag = InsertTag(conn, raw, 0, tx);
                    if (!TryAddAlias(conn, tag.Id, resolvedIdeal, tx, out _))
                    {
                        return BulkTagBatchResult.Fail("That alias is not allowed.");
                    }

                    items.Add(new FileOpItem { Step = FileOpStep.CreateTag, OldPath = tag.Id, NewPath = tag.Name });
                    items.Add(new FileOpItem
                    {
                        Step = FileOpStep.AddAlias,
                        OldPath = tag.Id,
                        NewPath = resolvedIdeal,
                        NewSourceId = SerializeAliasMigration([])
                    });
                    added.Add(tag.Name);
                }

                if (added.Count == 0)
                {
                    return BulkTagBatchResult.Fail("Those names already match a tag.");
                }

                ThrowIfMutationFault(probe, mutations: 1);
                ThrowIfBeforeJournalFault(probe);
                var summary = added.Count == 1
                    ? $"Added alias {added[0]} to {idealName}"
                    : $"Added {added.Count} aliases to {idealName}";
                var batchId = InsertJournaledBatch(
                    conn, tx, projectId, FileOpKind.TagAliasAdd, summary, items, probe);
                tx.Commit();
                return new BulkTagBatchResult
                {
                    Ok = true,
                    Message = summary + ".",
                    BatchId = batchId,
                    Summary = summary
                };
            }
            catch (BulkTagFaultException)
            {
                return BulkTagBatchResult.Fail(BulkTagAbortedMessage);
            }
        });

    /// <summary>Delete one alias edge and journal <see cref="FileOpKind.TagAliasRemove"/>; Undo re-inserts it.</summary>
    public Task<BulkTagBatchResult> ApplyRemoveAliasJournaledAsync(string projectId, string aliasTagId) =>
        _db.WriteAsync(conn =>
        {
            if (string.IsNullOrEmpty(aliasTagId))
            {
                return BulkTagBatchResult.Fail("Pick an alias first.");
            }

            var probe = TakeBulkTagFault();
            InvalidateGraphCaches();
            using var tx = conn.BeginTransaction();
            try
            {
                if (!LoadAliasMap(conn, tx).TryGetValue(aliasTagId, out var idealId))
                {
                    return BulkTagBatchResult.Fail("That alias is already gone.");
                }

                var aliasName = TagNameOf(conn, aliasTagId, tx);
                using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM TagAlias WHERE AliasTagId = $a";
                    del.Parameters.AddWithValue("$a", aliasTagId);
                    del.ExecuteNonQuery();
                }

                ThrowIfMutationFault(probe, mutations: 1);
                ThrowIfBeforeJournalFault(probe);
                var summary = $"Removed alias {aliasName}";
                var batchId = InsertJournaledBatch(
                    conn,
                    tx,
                    projectId,
                    FileOpKind.TagAliasRemove,
                    summary,
                    [new FileOpItem { Step = FileOpStep.RemoveAlias, OldPath = aliasTagId, NewPath = idealId }],
                    probe);
                tx.Commit();
                return new BulkTagBatchResult
                {
                    Ok = true,
                    Message = summary + ".",
                    BatchId = batchId,
                    Summary = summary
                };
            }
            catch (BulkTagFaultException)
            {
                return BulkTagBatchResult.Fail(BulkTagAbortedMessage);
            }
        });

    /// <summary>
    /// "Move it here": point an existing alias at <paramref name="newIdealId"/> and journal
    /// <see cref="FileOpKind.TagAliasRepoint"/> as one Undo step.
    /// </summary>
    public Task<BulkTagBatchResult> ApplyRepointAliasJournaledAsync(
        string projectId,
        string aliasTagId,
        string newIdealId) =>
        _db.WriteAsync(conn =>
        {
            if (string.IsNullOrEmpty(aliasTagId) || string.IsNullOrEmpty(newIdealId))
            {
                return BulkTagBatchResult.Fail("Pick two tags.");
            }

            var probe = TakeBulkTagFault();
            InvalidateGraphCaches();
            using var tx = conn.BeginTransaction();
            try
            {
                if (!LoadAliasMap(conn, tx).TryGetValue(aliasTagId, out var oldIdeal))
                {
                    return BulkTagBatchResult.Fail("That alias is already gone.");
                }

                var resolvedNew = ResolveIdealTagId(conn, newIdealId, tx);
                if (string.Equals(oldIdeal, resolvedNew, StringComparison.Ordinal))
                {
                    return BulkTagBatchResult.Fail("That alias already points there.");
                }

                using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM TagAlias WHERE AliasTagId = $a";
                    del.Parameters.AddWithValue("$a", aliasTagId);
                    del.ExecuteNonQuery();
                }

                if (!TryAddAlias(conn, aliasTagId, resolvedNew, tx, out _))
                {
                    return BulkTagBatchResult.Fail("That alias is not allowed.");
                }

                var migrated = MigrateAssetTagsAliasToIdeal(conn, aliasTagId, resolvedNew, tx);
                ThrowIfMutationFault(probe, mutations: 1);
                foreach (var assetId in migrated.Select(m => m.AssetId).Distinct(StringComparer.Ordinal))
                {
                    RefreshFts(conn, assetId, tx);
                }

                ThrowIfBeforeJournalFault(probe);
                var summary = $"Moved alias {TagNameOf(conn, aliasTagId, tx)} to {TagNameOf(conn, resolvedNew, tx)}";
                var batchId = InsertJournaledBatch(
                    conn,
                    tx,
                    projectId,
                    FileOpKind.TagAliasRepoint,
                    summary,
                    [
                        new FileOpItem
                        {
                            Step = FileOpStep.RepointAlias,
                            OldPath = aliasTagId,
                            NewPath = resolvedNew,
                            OldSourceId = oldIdeal,
                            NewSourceId = SerializeAliasMigration(migrated)
                        }
                    ],
                    probe);
                tx.Commit();
                return new BulkTagBatchResult
                {
                    Ok = true,
                    Message = summary + ".",
                    BatchId = batchId,
                    Summary = summary
                };
            }
            catch (BulkTagFaultException)
            {
                return BulkTagBatchResult.Fail(BulkTagAbortedMessage);
            }
        });

    private BulkTagFaultProbe? TakeBulkTagFault()
    {
        var probe = TestBulkTagFault;
        TestBulkTagFault = null;
        return probe;
    }

    private static void ThrowIfMutationFault(BulkTagFaultProbe? probe, int mutations)
    {
        if (probe is not null && probe.FailAfterMutations > 0 && mutations >= probe.FailAfterMutations)
        {
            throw new BulkTagFaultException($"bulk-tag fault after mutation {mutations}");
        }
    }

    private static void ThrowIfBeforeJournalFault(BulkTagFaultProbe? probe)
    {
        if (probe is not null && probe.FailBeforeJournal)
        {
            throw new BulkTagFaultException("bulk-tag fault before journal");
        }
    }

    private static string InsertJournaledBatch(
        SqliteConnection conn,
        SqliteTransaction tx,
        string projectId,
        FileOpKind kind,
        string summary,
        List<FileOpItem> items,
        BulkTagFaultProbe? probe)
    {
        Action<int>? afterProgress = null;
        if (probe?.FailAfterJournalItems is int failAfter)
        {
            afterProgress = written =>
            {
                if (written >= failAfter)
                {
                    throw new BulkTagFaultException(
                        $"bulk-tag fault after journal progress items={written}");
                }
            };
        }

        return InsertFileOpBatch(
            conn,
            tx,
            new FileOpBatch
            {
                ProjectId = projectId,
                Kind = kind,
                Summary = summary,
                Items = items
            },
            afterProgress);
    }

    private sealed class BulkTagFaultException : Exception
    {
        public BulkTagFaultException(string message) : base(message)
        {
        }
    }
}
