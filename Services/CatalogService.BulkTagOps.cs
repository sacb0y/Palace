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
            try
            {
                foreach (var (tagId, tagName, source) in tags)
                {
                    foreach (var assetId in assetIds)
                    {
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
                }

                // CreateTag first (lower Seq) so Undo (descending Seq) strips AssignTag, then Tag row.
                var journalItems = new List<FileOpItem>(createItems.Count + items.Count);
                journalItems.AddRange(createItems);
                journalItems.AddRange(items);

                if (journalItems.Count == 0)
                {
                    tx.Commit();
                    var already = tags.Count == 1
                        ? $"{tags[0].TagName} was already on the selection."
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
    /// in <b>one</b> SQLite transaction when migration rows exist. No migration → commit the
    /// edge without a journal row (same as prior FileOp path). Journal failure rolls back.
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

                if (migrated.Count == 0)
                {
                    tx.Commit();
                    return new BulkTagBatchResult
                    {
                        Ok = true,
                        Message = "Alias added.",
                        Summary = "Alias added"
                    };
                }

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
                var summary = $"Aliased tag ({AssetNouns.Count(migrated.Count)} updated)";
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
