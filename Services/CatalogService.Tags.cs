using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>
/// Test-only fault injection for tag import transaction coverage. Production leaves null.
/// Throw from the probe to abort before Commit — SQLite rolls the whole import back.
/// </summary>
public sealed class TagImportFaultProbe
{
    /// <summary>Throw after this many new Tag inserts (1-based). 0 = never.</summary>
    public int FailAfterCreates { get; init; }
}

/// <summary>
/// Test-only fault injection for CreateTag Undo (<see cref="CatalogService.TryDeleteUnusedCreatedTagAsync"/>).
/// Production leaves null.
/// </summary>
public sealed class TagCreateUndoFaultProbe
{
    /// <summary>Throw instead of deleting an unused CreateTag row.</summary>
    public bool ThrowOnDelete { get; init; }
}

/// <summary>Catalog: the global tag graph (tags, groups / memberships, implications, colors, import / export).</summary>
public sealed partial class CatalogService
{
    public Task<IReadOnlyList<Tag>> GetTagsAsync() =>
        _db.ReadAsync(conn => (IReadOnlyList<Tag>)LoadTags(conn));

    public Task<Tag?> GetTagAsync(string id) =>
        _db.ReadAsync(conn => LoadTags(conn).FirstOrDefault(t => t.Id == id));

    public Task<IReadOnlyList<TagMembership>> GetMembershipsAsync() =>
        _db.ReadAsync(conn => (IReadOnlyList<TagMembership>)LoadMemberships(conn));

    public Task<IReadOnlyList<TagImplication>> GetImplicationsAsync() =>
        _db.ReadAsync(conn => (IReadOnlyList<TagImplication>)LoadImplications(conn));

    public Task<IReadOnlyList<TagAlias>> GetAliasesAsync() =>
        _db.ReadAsync(conn => (IReadOnlyList<TagAlias>)LoadAliases(conn));

    /// <summary>
    /// Maps <paramref name="aliasTagId"/> → ideal (Hydrus sibling). Resolves an ideal that is
    /// itself an alias to its ultimate ideal (or refuses when that collapses to self).
    /// Migrates existing <c>AssetTag</c> rows alias→ideal (deduped) in one transaction; returns
    /// those rows so callers can journal undo (Doktor c).
    /// </summary>
    public Task<AliasAddResult> AddAliasAsync(string aliasTagId, string idealTagId) =>
        _db.WriteAsync(conn =>
        {
            InvalidateGraphCaches();
            IReadOnlyList<AssetTag> migrated = [];
            string resolvedIdeal;
            using (var tx = conn.BeginTransaction())
            {
                resolvedIdeal = ResolveIdealTagId(conn, idealTagId, tx);
                if (!TryAddAlias(conn, aliasTagId, resolvedIdeal, tx, out var inserted))
                {
                    return new AliasAddResult { Ok = false, AliasTagId = aliasTagId, IdealTagId = resolvedIdeal };
                }

                if (inserted)
                {
                    migrated = MigrateAssetTagsAliasToIdeal(conn, aliasTagId, resolvedIdeal, tx);
                }

                tx.Commit();
            }

            foreach (var assetId in migrated.Select(m => m.AssetId).Distinct(StringComparer.Ordinal))
            {
                RefreshFts(conn, assetId);
            }

            return new AliasAddResult
            {
                Ok = true,
                AliasTagId = aliasTagId,
                IdealTagId = resolvedIdeal,
                Migrated = migrated
            };
        });

    /// <summary>Restore AssetTag rows moved by <see cref="AddAliasAsync"/> and drop the edge.</summary>
    public Task UndoAliasAddAsync(string aliasTagId, string idealTagId, IReadOnlyList<AssetTag> migrated) =>
        _db.WriteAsync(conn =>
        {
            InvalidateGraphCaches();
            using (var tx = conn.BeginTransaction())
            {
                UndoAliasAdd(conn, tx, aliasTagId, idealTagId, migrated);
                tx.Commit();
            }

            foreach (var assetId in migrated.Select(m => m.AssetId).Distinct(StringComparer.Ordinal))
            {
                RefreshFts(conn, assetId);
            }
        });

    /// <summary>
    /// Undo "Move it here": drop <paramref name="newIdealId"/>'s edge (restoring migrated rows) and
    /// put <paramref name="oldIdealId"/>'s edge back in the same transaction. False when the old edge
    /// cannot return (old ideal deleted, or it would now cycle); the move is still undone.
    /// </summary>
    public Task<bool> UndoAliasRepointAsync(
        string aliasTagId,
        string newIdealId,
        string oldIdealId,
        IReadOnlyList<AssetTag> migrated) =>
        _db.WriteAsync(conn =>
        {
            InvalidateGraphCaches();
            var restored = false;
            var refresh = new List<string>(migrated.Select(m => m.AssetId));
            using (var tx = conn.BeginTransaction())
            {
                UndoAliasAdd(conn, tx, aliasTagId, newIdealId, migrated);
                restored = TryRestoreAliasEdge(conn, tx, aliasTagId, oldIdealId, refresh);
                tx.Commit();
            }

            foreach (var assetId in refresh.Distinct(StringComparer.Ordinal))
            {
                RefreshFts(conn, assetId);
            }

            return restored;
        });

    /// <summary>Undo of a journaled alias remove: put the edge back (false when it is no longer allowed).</summary>
    public Task<bool> RestoreAliasEdgeAsync(string aliasTagId, string idealTagId) =>
        _db.WriteAsync(conn =>
        {
            InvalidateGraphCaches();
            var refresh = new List<string>();
            bool restored;
            using (var tx = conn.BeginTransaction())
            {
                restored = TryRestoreAliasEdge(conn, tx, aliasTagId, idealTagId, refresh);
                tx.Commit();
            }

            foreach (var assetId in refresh.Distinct(StringComparer.Ordinal))
            {
                RefreshFts(conn, assetId);
            }

            return restored;
        });

    private static void UndoAliasAdd(
        SqliteConnection conn,
        SqliteTransaction tx,
        string aliasTagId,
        string idealTagId,
        IReadOnlyList<AssetTag> migrated)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM TagAlias WHERE AliasTagId = $a AND IdealTagId = $i";
            del.Parameters.AddWithValue("$a", aliasTagId);
            del.Parameters.AddWithValue("$i", idealTagId);
            del.ExecuteNonQuery();
        }

        foreach (var row in migrated)
        {
            // Put the assignment back on the alias.
            UpsertAssetTag(conn, row.AssetId, aliasTagId, row.Source, tx);
            // Drop the ideal copy when it still matches the migrated source (no other applied).
            using var check = conn.CreateCommand();
            check.Transaction = tx;
            check.CommandText = "SELECT Source FROM AssetTag WHERE AssetId = $a AND TagId = $t";
            check.Parameters.AddWithValue("$a", row.AssetId);
            check.Parameters.AddWithValue("$t", idealTagId);
            var idealSource = check.ExecuteScalar() as string;
            if (string.Equals(idealSource, row.Source.ToString(), StringComparison.Ordinal))
            {
                DeleteAssetTag(conn, row.AssetId, idealTagId, tx);
            }
        }
    }

    /// <summary>
    /// Re-insert alias→ideal when both tags still exist and <see cref="TagAliasResolve.CanAdd"/> allows it;
    /// assignments the alias row gained while the edge was gone move to the ideal.
    /// </summary>
    private static bool TryRestoreAliasEdge(
        SqliteConnection conn,
        SqliteTransaction tx,
        string aliasTagId,
        string idealTagId,
        List<string> refreshAssetIds)
    {
        if (!TagExists(conn, aliasTagId, tx) || !TagExists(conn, idealTagId, tx))
        {
            return false;
        }

        if (!TryAddAlias(conn, aliasTagId, idealTagId, tx, out var inserted))
        {
            return false;
        }

        if (inserted)
        {
            refreshAssetIds.AddRange(
                MigrateAssetTagsAliasToIdeal(conn, aliasTagId, idealTagId, tx).Select(m => m.AssetId));
        }

        return true;
    }

    private static bool TagExists(SqliteConnection conn, string tagId, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM Tag WHERE Id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", tagId);
        return cmd.ExecuteScalar() is not null;
    }

    private static string TagNameOf(SqliteConnection conn, string tagId, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Name FROM Tag WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", tagId);
        return cmd.ExecuteScalar() as string ?? "tag";
    }

    public Task RemoveAliasAsync(string aliasTagId) =>
        _db.WriteAsync(conn =>
        {
            InvalidateGraphCaches();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM TagAlias WHERE AliasTagId = $a";
            cmd.Parameters.AddWithValue("$a", aliasTagId);
            cmd.ExecuteNonQuery();
        });

    /// <summary>Resolve alias → ideal transitively (before implications).</summary>
    public Task<string> ResolveIdealTagIdAsync(string tagId) =>
        _db.ReadAsync(conn => ResolveIdealTagId(conn, tagId));

    /// <summary>Tag after alias→ideal resolve, or null when <paramref name="tagId"/> is unknown.</summary>
    public Task<Tag?> GetIdealTagAsync(string tagId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Priority, Slug, Color, IsStarred FROM Tag WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", tagId);
            var list = ReadTags(cmd);
            return list.Count == 0 ? null : ResolveIdealTag(conn, list[0]);
        });

    /// <summary>Export the global tag catalog to portable JSON (Sacb0y).</summary>
    public Task<string> ExportTagsJsonAsync() =>
        _db.ReadAsync(conn =>
        {
            var tags = LoadTags(conn)
                .Select(t => new TagPortableTagSource
                {
                    Id = t.Id,
                    Name = t.Name,
                    Priority = t.Priority,
                    Color = t.Color,
                    IsStarred = t.IsStarred
                })
                .ToList();
            var memberships = LoadMemberships(conn)
                .Select(m => new TagPortableEdge { FromId = m.ParentId, ToId = m.ChildId })
                .ToList();
            var implications = LoadImplications(conn)
                .Select(i => new TagPortableEdge { FromId = i.TagId, ToId = i.ImpliedTagId })
                .ToList();
            var aliases = LoadAliases(conn)
                .Select(a => new TagPortableEdge { FromId = a.AliasTagId, ToId = a.IdealTagId })
                .ToList();
            return TagPortable.Serialize(TagPortable.FromCatalog(tags, memberships, implications, aliases));
        });

    /// <summary>Test-only. Cleared after each import that observes it.</summary>
    public TagImportFaultProbe? TestTagImportFault { get; set; }

    /// <summary>Test-only. Cleared after CreateTag Undo observes it.</summary>
    public TagCreateUndoFaultProbe? TestTagCreateUndoFault { get; set; }

    private TagImportFaultProbe? TakeTagImportFault()
    {
        var probe = TestTagImportFault;
        TestTagImportFault = null;
        return probe;
    }

    private TagCreateUndoFaultProbe? TakeTagCreateUndoFault()
    {
        var probe = TestTagCreateUndoFault;
        TestTagCreateUndoFault = null;
        return probe;
    }

    /// <summary>
    /// Merge a portable tag JSON into the global catalog. Upsert by name;
    /// does not delete unrelated tags. Per-edge cycle/rebind refusal for
    /// parents, implicits, and aliases (counters on success). Mutates run in
    /// <b>one</b> SQLite transaction after a pre-import DB backup — a mid-import
    /// exception rolls everything back.
    /// </summary>
    public Task<TagMergePlan> ImportTagsJsonAsync(string json) =>
        _db.WriteAsync(conn =>
        {
            // Clear first so a no-op (or early failure before backup) cannot
            // surface a previous import's backup path in status.
            _db.ClearLastTagImportBackup();
            InvalidateGraphCaches();
            var document = TagPortable.Parse(json);
            var existing = LoadTags(conn)
                .Select(t => new TagPortableTagSource
                {
                    Id = t.Id,
                    Name = t.Name,
                    Priority = t.Priority,
                    Color = t.Color,
                    IsStarred = t.IsStarred
                })
                .ToList();
            var memberships = LoadMemberships(conn)
                .Select(m => new TagPortableEdge { FromId = m.ParentId, ToId = m.ChildId })
                .ToList();
            var implications = LoadImplications(conn)
                .Select(i => new TagPortableEdge { FromId = i.TagId, ToId = i.ImpliedTagId })
                .ToList();
            var aliases = LoadAliases(conn)
                .Select(a => new TagPortableEdge { FromId = a.AliasTagId, ToId = a.IdealTagId })
                .ToList();
            var plan = TagPortable.PlanMerge(existing, memberships, implications, aliases, document);

            // Derive from PlanMerge output only — do not re-list change kinds here.
            if (!plan.HasWork)
            {
                // No-op: leave LastTagImportBackupDir cleared (not a prior path).
                return plan;
            }

            // Restore point before any mutate (same backup API as T5 implied purge).
            _db.BackupBeforeTagImport();

            var probe = TakeTagImportFault();
            using var tx = conn.BeginTransaction();
            // Apply creates first so name→id resolves to real SQLite ids.
            // Names are unique ignoring case and padding (TagNames); keys are trimmed so a
            // stored " cat " still matches the trimmed edge names PlanMerge emits.
            var idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in existing)
            {
                idByName.TryAdd(TagNames.Normalize(t.Name), t.Id);
            }

            var createsDone = 0;
            foreach (var create in plan.Creates)
            {
                // e.g. " cat " in the file vs "cat" here: reuse instead of creating a duplicate.
                if (TagNames.FindId(conn, create.Name, tx: tx) is { } existingId)
                {
                    idByName[TagNames.Normalize(create.Name)] = existingId;
                    continue;
                }

                var tag = InsertTag(conn, create.Name, create.Priority, tx);
                ApplyTagFields(conn, tag.Id, create.Priority, create.Color, create.Starred, tx);
                idByName[TagNames.Normalize(create.Name)] = tag.Id;
                createsDone++;
                if (probe is { FailAfterCreates: > 0 } && createsDone >= probe.FailAfterCreates)
                {
                    throw new InvalidOperationException("Tag import fault probe: FailAfterCreates");
                }
            }

            foreach (var update in plan.Updates)
            {
                ApplyTagFields(conn, update.Id, update.Priority, update.Color, update.Starred, tx);
            }

            foreach (var edge in plan.Memberships)
            {
                if (!idByName.TryGetValue(TagNames.Normalize(edge.ParentName), out var parentId)
                    || !idByName.TryGetValue(TagNames.Normalize(edge.ChildName), out var childId))
                {
                    continue;
                }

                TryAddMembership(conn, parentId, childId, tx);
            }

            foreach (var edge in plan.Implications)
            {
                // ParentName = tag, ChildName = implied (see TagPortable).
                if (!idByName.TryGetValue(TagNames.Normalize(edge.ParentName), out var tagId)
                    || !idByName.TryGetValue(TagNames.Normalize(edge.ChildName), out var impliedId))
                {
                    continue;
                }

                TryAddImplication(conn, tagId, impliedId, tx);
            }

            // ParentName = alias, ChildName = ideal. Resolve ideal through current map
            // so same-file chains land on the ultimate ideal; migrate AssetTag on insert.
            var ftsAssets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var edge in plan.Aliases)
            {
                if (!idByName.TryGetValue(TagNames.Normalize(edge.ParentName), out var aliasId)
                    || !idByName.TryGetValue(TagNames.Normalize(edge.ChildName), out var idealId))
                {
                    continue;
                }

                var resolvedIdeal = ResolveIdealTagId(conn, idealId, tx);
                if (!TryAddAlias(conn, aliasId, resolvedIdeal, tx, out var inserted))
                {
                    continue;
                }

                if (inserted)
                {
                    foreach (var row in MigrateAssetTagsAliasToIdeal(conn, aliasId, resolvedIdeal, tx))
                    {
                        ftsAssets.Add(row.AssetId);
                    }
                }
            }

            foreach (var assetId in ftsAssets)
            {
                RefreshFts(conn, assetId, tx);
            }

            tx.Commit();
            return plan;
        });

    private static void ApplyTagFields(
        SqliteConnection conn,
        string id,
        int priority,
        string? color,
        bool starred,
        SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE Tag SET Priority = $p, Color = $c, IsStarred = $s WHERE Id = $id
            """;
        cmd.Parameters.AddWithValue("$p", priority);
        cmd.Parameters.AddWithValue("$c", (object?)TagInk.Normalize(color) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", starred ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public Task<IReadOnlyList<Tag>> GetImpliedTagsAsync(string tagId) =>
        _db.ReadAsync(conn =>
        {
            var tags = LoadTags(conn).ToDictionary(t => t.Id);
            return (IReadOnlyList<Tag>)LoadImplications(conn)
                .Where(i => i.TagId == tagId && tags.ContainsKey(i.ImpliedTagId))
                .Select(i => tags[i.ImpliedTagId])
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        });

    /// <summary>
    /// Adds an implication edge only (T5: implied tags are not materialized on AssetTag).
    /// Returns false when the edge would cycle.
    /// </summary>
    public Task<bool> AddImplicationAsync(string tagId, string impliedTagId) =>
        _db.WriteAsync(conn =>
        {
            var ok = TryAddImplication(conn, tagId, impliedTagId);
            if (ok)
            {
                InvalidateGraphCaches();
            }

            return ok;
        });

    /// <summary>
    /// Removes an implication edge only (T5: no Implied AssetTag sweep).
    /// </summary>
    public Task RemoveImplicationAsync(string tagId, string impliedTagId) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM TagImplication WHERE TagId = $t AND ImpliedTagId = $i";
            cmd.Parameters.AddWithValue("$t", tagId);
            cmd.Parameters.AddWithValue("$i", impliedTagId);
            cmd.ExecuteNonQuery();
            InvalidateGraphCaches();
        });

    public Task<Tag?> FindTagByNameAsync(string name) =>
        _db.ReadAsync(conn => FindTagByName(conn, name));

    /// <summary>Creates a tag; throws <see cref="DuplicateTagNameException"/> if the name is taken (any case).</summary>
    public Task<Tag> CreateTagAsync(string name, int priority = 0) =>
        _db.WriteAsync(conn =>
        {
            var tag = InsertTag(conn, name, priority);
            InvalidateGraphCaches();
            return tag;
        });

    /// <summary>
    /// Returns the existing tag with this name (any case), or creates it, in one write.
    /// Alias names resolve to their ideal (v1 rewrite-on-assign / create path).
    /// </summary>
    public async Task<Tag> GetOrCreateTagAsync(string name)
    {
        var (tag, _) = await GetOrCreateTagResultAsync(name).ConfigureAwait(false);
        return tag;
    }

    /// <summary>
    /// Like <see cref="GetOrCreateTagAsync"/> but reports whether a new Tag row was inserted
    /// (for TagAssign CreateTag journaling). Alias hits return the ideal with Created=false.
    /// </summary>
    public Task<(Tag Tag, bool Created)> GetOrCreateTagResultAsync(string name) =>
        _db.WriteAsync(conn =>
        {
            var existing = FindTagByName(conn, name);
            if (existing is not null)
            {
                return (ResolveIdealTag(conn, existing), false);
            }

            var tag = InsertTag(conn, name, 0);
            InvalidateGraphCaches();
            return (ResolveIdealTag(conn, tag), true);
        });

    /// <summary>How many AssetTag rows reference this tag (any source).</summary>
    public Task<int> CountAssetTagsAsync(string tagId) =>
        _db.ReadAsync(conn => CountAssetTags(conn, tagId));

    /// <summary>Catalog-wide AssetTag counts keyed by TagId (t2-02 suggestion use counts).</summary>
    public Task<IReadOnlyDictionary<string, int>> GetAssetTagCountsAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TagId, COUNT(*) FROM AssetTag GROUP BY TagId";
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                map[reader.GetString(0)] = reader.GetInt32(1);
            }

            return (IReadOnlyDictionary<string, int>)map;
        });

    /// <summary>
    /// Project-scoped AssetTag pairs for Library ▸ Tags panel file counts (t8-01).
    /// </summary>
    public Task<IReadOnlyList<(string AssetId, string TagId)>> GetProjectAssetTagPairsAsync(string? projectId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
            cmd.CommandText = """
                SELECT at.AssetId, at.TagId
                  FROM AssetTag at
                  JOIN Asset a ON a.Id = at.AssetId
                 WHERE ($project IS NULL OR a.SourceFolderId IN (
                         SELECT Id FROM SourceFolder WHERE ProjectId = $project))
                """;
            var rows = new List<(string, string)>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1)));
            }

            return (IReadOnlyList<(string AssetId, string TagId)>)rows;
        });

    public Task<Tag> CreateChildTagAsync(string parentId, string name, int priority = 0) =>
        _db.WriteAsync(conn =>
        {
            var tag = InsertTag(conn, name, priority);
            TryAddMembership(conn, parentId, tag.Id);
            InvalidateGraphCaches();
            return tag;
        });

    public Task<TagCreateBatchResult> CreateTagsAsync(IEnumerable<string> names, string? parentId = null) =>
        _db.WriteAsync(conn => CreateTagsCore(conn, names, parentId, tx: null));

    public Task SetTagDescriptionAsync(string id, string? description) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Tag SET Description = $d WHERE Id = $id";
            cmd.Parameters.AddWithValue(
                "$d",
                string.IsNullOrWhiteSpace(description) ? DBNull.Value : description.Trim());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });

    private TagCreateBatchResult CreateTagsCore(
        SqliteConnection conn,
        IEnumerable<string> names,
        string? parentId,
        SqliteTransaction? tx,
        List<FileOpItem>? journal = null)
    {
        var created = 0;
        var existed = 0;
        var rejected = 0;
        var tags = new List<Tag>();
        var createdIds = new List<string>();
        var membershipAdded = 0;
        foreach (var raw in TagNameList.Split(string.Join('\n', names)))
        {
            var existing = FindTagByName(conn, raw, tx);
            Tag tag;
            if (existing is null)
            {
                tag = InsertTag(conn, raw, 0, tx);
                created++;
                createdIds.Add(tag.Id);
                journal?.Add(new FileOpItem
                {
                    Step = FileOpStep.CreateTag,
                    OldPath = tag.Id,
                    NewPath = tag.Name
                });
            }
            else
            {
                // Existing alias names resolve to ideal before membership / return.
                tag = ResolveIdealTag(conn, existing, tx);
                existed++;
            }

            if (parentId is not null)
            {
                var addResult = TryAddMembership(conn, parentId, tag.Id, tx);
                if (addResult == MembershipAddResult.CycleRefuse)
                {
                    rejected++;
                }
                else if (addResult == MembershipAddResult.Added)
                {
                    membershipAdded++;
                    journal?.Add(new FileOpItem
                    {
                        Step = FileOpStep.AddMembership,
                        OldPath = parentId,
                        NewPath = tag.Id
                    });
                }
            }

            tags.Add(tag);
        }

        if (created > 0 || membershipAdded > 0)
        {
            InvalidateGraphCaches();
        }

        string? groupName = null;
        if (parentId is not null)
        {
            using var parentCmd = conn.CreateCommand();
            if (tx is not null)
            {
                parentCmd.Transaction = tx;
            }

            parentCmd.CommandText = "SELECT Name FROM Tag WHERE Id = $id";
            parentCmd.Parameters.AddWithValue("$id", parentId);
            groupName = parentCmd.ExecuteScalar() as string;
        }

        return new TagCreateBatchResult
        {
            Created = created,
            Existed = existed,
            MembershipRejected = rejected,
            Tags = tags,
            CreatedTagIds = createdIds,
            GroupName = groupName
        };
    }

    private static bool MembershipExists(
        SqliteConnection conn,
        string parentId,
        string childId,
        SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT 1 FROM TagMembership WHERE ParentId = $p AND ChildId = $c LIMIT 1";
        cmd.Parameters.AddWithValue("$p", parentId);
        cmd.Parameters.AddWithValue("$c", childId);
        return cmd.ExecuteScalar() is not null;
    }

    public Task SetTagStarredAsync(string id, bool starred) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Tag SET IsStarred = $s WHERE Id = $id";
            cmd.Parameters.AddWithValue("$s", starred ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });

    public Task DeleteTagAsync(string id) =>
        _db.WriteAsync(conn =>
        {
            InvalidateGraphCaches();
            var assetIds = AssetsHavingAnyTag(conn, [id]);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Tag WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            foreach (var assetId in assetIds)
            {
                RefreshFts(conn, assetId);
            }
        });

    public Task<bool> MoveTagAmongSiblingsAsync(string tagId, string? parentId, int delta) =>
        _db.WriteAsync(conn =>
        {
            var tags = LoadTags(conn);
            var memberships = LoadMemberships(conn);
            var siblings = TagSiblings.Of(tags, memberships, parentId)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var from = siblings.FindIndex(t => t.Id == tagId);
            var to = from + delta;
            if (from < 0 || to < 0 || to >= siblings.Count)
            {
                return false;
            }

            (siblings[from], siblings[to]) = (siblings[to], siblings[from]);
            for (var i = 0; i < siblings.Count; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Tag SET Priority = $p WHERE Id = $id";
                cmd.Parameters.AddWithValue("$p", siblings.Count - i);
                cmd.Parameters.AddWithValue("$id", siblings[i].Id);
                cmd.ExecuteNonQuery();
            }

            return true;
        });

    /// <summary>
    /// Renames a tag. Returns false (and changes nothing) when another tag already has the name,
    /// ignoring case; changing only the case of a tag's own name is allowed.
    /// </summary>
    public Task<bool> RenameTagAsync(string id, string name) =>
        _db.WriteAsync(conn =>
        {
            var normalized = TagNames.Normalize(name);
            if (normalized.Length == 0 || TagNames.FindId(conn, normalized, excludeId: id) is not null)
            {
                return false;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Tag SET Name = $name, Slug = $slug WHERE Id = $id";
            cmd.Parameters.AddWithValue("$name", normalized);
            cmd.Parameters.AddWithValue("$slug", PathSafe.Slug(normalized));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            RefreshFtsForTag(conn, id);
            InvalidateGraphCaches();
            return true;
        });

    public Task SetTagPriorityAsync(string id, int priority) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Tag SET Priority = $p WHERE Id = $id";
            cmd.Parameters.AddWithValue("$p", priority);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });

    public Task SetTagColorAsync(string id, string? color) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Tag SET Color = $c WHERE Id = $id";
            cmd.Parameters.AddWithValue("$c", (object?)TagInk.Normalize(color) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });

    public string? EffectiveColor(Tag tag, IReadOnlyList<Tag> tags, IReadOnlyList<TagMembership> memberships) =>
        MapEffectiveColors(tags, memberships).GetValueOrDefault(tag.Id);

    public static IReadOnlyDictionary<string, string?> MapEffectiveColors(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships)
    {
        var byId = tags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var parents = new Dictionary<string, List<Tag>>(StringComparer.Ordinal);
        // Parents that have at least one child — GROUPS rows; face palette applies only here.
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in memberships)
        {
            if (!byId.TryGetValue(edge.ParentId, out var parent) || !byId.ContainsKey(edge.ChildId))
            {
                continue;
            }

            groupIds.Add(edge.ParentId);

            if (!parents.TryGetValue(edge.ChildId, out var list))
            {
                list = [];
                parents[edge.ChildId] = list;
            }

            list.Add(parent);
        }

        var cache = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            ResolveEffectiveColor(tag.Id, byId, parents, groupIds, cache, []);
        }

        return cache;
    }

    public static string DescribeTagColor(
        Tag tag,
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships)
    {
        // Face palette fills are display-only — describe from stored colours only.
        if (!string.IsNullOrWhiteSpace(tag.Color) && TagInk.Normalize(tag.Color) is not null)
        {
            return "Custom";
        }

        var ancestor = FindColorAncestorName(tag.Id, tags.ToDictionary(t => t.Id, StringComparer.Ordinal), memberships);
        return ancestor is null ? "No color" : $"Inherited from {ancestor}";
    }

    public Task<MembershipAddResult> AddMembershipAsync(string parentId, string childId) =>
        _db.WriteAsync(conn =>
        {
            var result = TryAddMembership(conn, parentId, childId);
            if (result == MembershipAddResult.Added)
            {
                InvalidateGraphCaches();
            }

            return result;
        });

    public Task RemoveMembershipAsync(string parentId, string childId) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM TagMembership WHERE ParentId = $p AND ChildId = $c";
            cmd.Parameters.AddWithValue("$p", parentId);
            cmd.Parameters.AddWithValue("$c", childId);
            cmd.ExecuteNonQuery();
            InvalidateGraphCaches();
        });

    public Task<IReadOnlyList<Tag>> GetParentsAsync(string tagId) =>
        _db.ReadAsync(conn => (IReadOnlyList<Tag>)ParentsOf(conn, tagId));

    public Task<IReadOnlyList<TagPath>> GetPathsToTagAsync(string tagId) =>
        _db.ReadAsync(conn => (IReadOnlyList<TagPath>)BuildPaths(conn, tagId));

    public Task<IReadOnlyList<string>> GetDescendantTagIdsAsync(string tagId) =>
        _db.ReadAsync(conn => (IReadOnlyList<string>)DescendantsOf(conn, tagId));

    private static List<Tag> ReadTags(SqliteCommand cmd)
    {
        var list = new List<Tag>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Tag
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Priority = reader.GetInt32(2),
                Slug = reader.GetString(3),
                Color = reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetString(4) : null,
                IsStarred = reader.FieldCount > 5 && !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
                Description = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetString(6) : null
            });
        }

        return list;
    }

    private static Tag? FindTagByName(SqliteConnection conn, string name, SqliteTransaction? tx = null)
    {
        var id = TagNames.FindId(conn, name, tx: tx);
        if (id is null)
        {
            return null;
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Id, Name, Priority, Slug, Color, IsStarred, Description FROM Tag WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        var list = ReadTags(cmd);
        return list.Count > 0 ? list[0] : null;
    }

    private static List<Tag> LoadTags(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Priority, Slug, Color, IsStarred, Description FROM Tag ORDER BY Priority DESC, Name COLLATE NOCASE";
        return ReadTags(cmd);
    }

    private static List<TagMembership> LoadMemberships(SqliteConnection conn, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT ParentId, ChildId FROM TagMembership";
        var list = new List<TagMembership>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TagMembership { ParentId = reader.GetString(0), ChildId = reader.GetString(1) });
        }

        return list;
    }

    private static List<TagImplication> LoadImplications(SqliteConnection conn, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT TagId, ImpliedTagId FROM TagImplication";
        var list = new List<TagImplication>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TagImplication { TagId = reader.GetString(0), ImpliedTagId = reader.GetString(1) });
        }

        return list;
    }

    private static List<TagAlias> LoadAliases(SqliteConnection conn, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT AliasTagId, IdealTagId FROM TagAlias";
        var list = new List<TagAlias>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TagAlias { AliasTagId = reader.GetString(0), IdealTagId = reader.GetString(1) });
        }

        return list;
    }

    private Dictionary<string, string> LoadAliasMapCached(SqliteConnection conn)
    {
        lock (_graphCacheLock)
        {
            if (_aliasMapCache is not null)
            {
                return _aliasMapCache;
            }

            var map = LoadAliasMap(conn);
            _aliasMapCache = map;
            return map;
        }
    }

    /// <summary>ImpliedTagId → tags that imply it (cached, thread-safe).</summary>
    private Dictionary<string, List<string>> LoadImplyingIdsCached(SqliteConnection conn)
    {
        lock (_graphCacheLock)
        {
            if (_implyingIdsCache is not null)
            {
                return _implyingIdsCache;
            }

            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var edge in LoadImplications(conn))
            {
                if (!map.TryGetValue(edge.ImpliedTagId, out var list))
                {
                    list = [];
                    map[edge.ImpliedTagId] = list;
                }

                list.Add(edge.TagId);
            }

            _implyingIdsCache = map;
            return map;
        }
    }

    /// <summary>Cached Tag Id↔Name maps for FTS query expansion (thread-safe).</summary>
    private (Dictionary<string, string> NameById, Dictionary<string, string> IdByName) LoadTagNamesCached(
        SqliteConnection conn)
    {
        lock (_graphCacheLock)
        {
            if (_tagNameByIdCache is not null && _tagIdByNameCache is not null)
            {
                return (_tagNameByIdCache, _tagIdByNameCache);
            }

            var nameById = new Dictionary<string, string>(StringComparer.Ordinal);
            var idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, Name FROM Tag";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetString(0);
                    var name = reader.GetString(1);
                    nameById[id] = name;
                    idByName.TryAdd(TagNames.Normalize(name), id);
                }
            }

            _tagNameByIdCache = nameById;
            _tagIdByNameCache = idByName;
            return (nameById, idByName);
        }
    }

    private static Dictionary<string, string> LoadAliasMap(SqliteConnection conn, SqliteTransaction? tx = null)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var edge in LoadAliases(conn, tx))
        {
            map[edge.AliasTagId] = edge.IdealTagId;
        }

        return map;
    }

    /// <summary>Alias → ideal transitively. Call before implication fan-out.</summary>
    private static string ResolveIdealTagId(
        SqliteConnection conn,
        string tagId,
        SqliteTransaction? tx = null) =>
        TagAliasResolve.Ideal(tagId, LoadAliasMap(conn, tx));

    private static Tag ResolveIdealTag(SqliteConnection conn, Tag tag, SqliteTransaction? tx = null)
    {
        var idealId = ResolveIdealTagId(conn, tag.Id, tx);
        if (string.Equals(idealId, tag.Id, StringComparison.Ordinal))
        {
            return tag;
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Id, Name, Priority, Slug, Color, IsStarred FROM Tag WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", idealId);
        var list = ReadTags(cmd);
        return list.Count > 0 ? list[0] : tag;
    }

    /// <summary>
    /// Inserts the alias edge when allowed. <paramref name="inserted"/> is false for an
    /// idempotent same-edge no-op (no AssetTag migration needed).
    /// </summary>
    private static bool TryAddAlias(
        SqliteConnection conn,
        string aliasTagId,
        string idealTagId,
        SqliteTransaction? tx,
        out bool inserted)
    {
        inserted = false;
        var map = LoadAliasMap(conn, tx);
        if (!TagAliasResolve.CanAdd(aliasTagId, idealTagId, map))
        {
            return false;
        }

        if (map.TryGetValue(aliasTagId, out var existing)
            && string.Equals(existing, idealTagId, StringComparison.Ordinal))
        {
            return true;
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO TagAlias (AliasTagId, IdealTagId) VALUES ($a, $i)";
            cmd.Parameters.AddWithValue("$a", aliasTagId);
            cmd.Parameters.AddWithValue("$i", idealTagId);
            cmd.ExecuteNonQuery();
        }

        // Flatten: any edge that pointed at the new alias now points at its ultimate ideal.
        using (var flatten = conn.CreateCommand())
        {
            flatten.Transaction = tx;
            flatten.CommandText = "UPDATE TagAlias SET IdealTagId = $i WHERE IdealTagId = $a";
            flatten.Parameters.AddWithValue("$i", idealTagId);
            flatten.Parameters.AddWithValue("$a", aliasTagId);
            flatten.ExecuteNonQuery();
        }

        inserted = true;
        return true;
    }

    /// <summary>
    /// Rewrite AssetTag rows from alias→ideal in one pass: applied sources win over Implied;
    /// duplicate alias rows are dropped. Returns the pre-migrate alias rows for undo journaling.
    /// </summary>
    private static List<AssetTag> MigrateAssetTagsAliasToIdeal(
        SqliteConnection conn,
        string aliasTagId,
        string idealTagId,
        SqliteTransaction tx)
    {
        var migrated = new List<AssetTag>();
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT AssetId, TagId, Source FROM AssetTag WHERE TagId = $alias";
            read.Parameters.AddWithValue("$alias", aliasTagId);
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                migrated.Add(new AssetTag
                {
                    AssetId = reader.GetString(0),
                    TagId = reader.GetString(1),
                    Source = Enum.TryParse<TagSource>(reader.GetString(2), out var source)
                        ? source
                        : TagSource.Manual
                });
            }
        }

        if (migrated.Count == 0)
        {
            return migrated;
        }

        // Ideal was Implied but alias was applied → keep applied Source on ideal.
        using (var upgrade = conn.CreateCommand())
        {
            upgrade.Transaction = tx;
            upgrade.CommandText = """
                UPDATE AssetTag SET Source = (
                    SELECT l.Source FROM AssetTag l
                    WHERE l.AssetId = AssetTag.AssetId AND l.TagId = $alias)
                WHERE TagId = $ideal AND Source = 'Implied'
                  AND EXISTS (
                    SELECT 1 FROM AssetTag l
                    WHERE l.AssetId = AssetTag.AssetId AND l.TagId = $alias AND l.Source <> 'Implied')
                """;
            upgrade.Parameters.AddWithValue("$alias", aliasTagId);
            upgrade.Parameters.AddWithValue("$ideal", idealTagId);
            upgrade.ExecuteNonQuery();
        }

        using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT OR IGNORE INTO AssetTag (AssetId, TagId, Source)
                SELECT AssetId, $ideal, Source FROM AssetTag WHERE TagId = $alias
                """;
            insert.Parameters.AddWithValue("$alias", aliasTagId);
            insert.Parameters.AddWithValue("$ideal", idealTagId);
            insert.ExecuteNonQuery();
        }

        using (var delete = conn.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM AssetTag WHERE TagId = $alias";
            delete.Parameters.AddWithValue("$alias", aliasTagId);
            delete.ExecuteNonQuery();
        }

        return migrated;
    }

    private static bool TryAddImplication(
        SqliteConnection conn,
        string tagId,
        string impliedTagId,
        SqliteTransaction? tx = null)
    {
        if (string.Equals(tagId, impliedTagId, StringComparison.Ordinal)
            || WouldImplicationCycle(conn, tagId, impliedTagId, tx))
        {
            return false;
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO TagImplication (TagId, ImpliedTagId) VALUES ($t, $i)";
        cmd.Parameters.AddWithValue("$t", tagId);
        cmd.Parameters.AddWithValue("$i", impliedTagId);
        cmd.ExecuteNonQuery();
        return true;
    }

    private static bool WouldImplicationCycle(
        SqliteConnection conn,
        string tagId,
        string impliedTagId,
        SqliteTransaction? tx = null) =>
        TransitiveImplied(conn, impliedTagId, tx).Contains(tagId);

    private static List<string> TransitiveImplied(
        SqliteConnection conn,
        string tagId,
        SqliteTransaction? tx = null)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in LoadImplications(conn, tx))
        {
            if (!children.TryGetValue(edge.TagId, out var list))
            {
                list = [];
                children[edge.TagId] = list;
            }

            list.Add(edge.ImpliedTagId);
        }

        var found = new List<string>();
        var stack = new Stack<string>();
        stack.Push(tagId);
        var seen = new HashSet<string>(StringComparer.Ordinal) { tagId };
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!children.TryGetValue(current, out var kids))
            {
                continue;
            }

            foreach (var kid in kids)
            {
                if (seen.Add(kid))
                {
                    found.Add(kid);
                    stack.Push(kid);
                }
            }
        }

        return found;
    }

    private static string? ResolveEffectiveColor(
        string tagId,
        IReadOnlyDictionary<string, Tag> tags,
        IReadOnlyDictionary<string, List<Tag>> parents,
        HashSet<string> groupIds,
        Dictionary<string, string?> cache,
        HashSet<string> visiting)
    {
        if (cache.TryGetValue(tagId, out var cached))
        {
            return cached;
        }

        if (!visiting.Add(tagId) || !tags.TryGetValue(tagId, out var tag))
        {
            return null;
        }

        // Readable own colour wins; near-black stored colours fall through to inherit / face.
        if (TagInk.Normalize(tag.Color) is { } own && !TagInk.IsTooDarkForFace(own))
        {
            cache[tagId] = own;
            visiting.Remove(tagId);
            return own;
        }

        if (parents.TryGetValue(tagId, out var list) && list.Count > 0)
        {
            var winner = list
                .OrderByDescending(p => p.Priority)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .First();
            var inherited = ResolveEffectiveColor(winner.Id, tags, parents, groupIds, cache, visiting);
            cache[tagId] = inherited;
            visiting.Remove(tagId);
            return inherited;
        }

        // Only GROUPS (tags with children) get the mockup face palette. Ungrouped leaves stay neutral.
        if (groupIds.Contains(tagId))
        {
            var face = TagInk.FaceColor(tag.Color, string.IsNullOrWhiteSpace(tag.Id) ? tag.Name : tag.Id);
            cache[tagId] = face;
            visiting.Remove(tagId);
            return face;
        }

        cache[tagId] = null;
        visiting.Remove(tagId);
        return null;
    }

    private static string? FindColorAncestorName(
        string tagId,
        IReadOnlyDictionary<string, Tag> tags,
        IReadOnlyList<TagMembership> memberships)
    {
        var parents = new Dictionary<string, List<Tag>>(StringComparer.Ordinal);
        foreach (var edge in memberships)
        {
            if (!tags.TryGetValue(edge.ParentId, out var parent))
            {
                continue;
            }

            if (!parents.TryGetValue(edge.ChildId, out var list))
            {
                list = [];
                parents[edge.ChildId] = list;
            }

            list.Add(parent);
        }

        var current = tagId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Add(current) && tags.TryGetValue(current, out var tag))
        {
            if (current != tagId && TagInk.Normalize(tag.Color) is not null)
            {
                return tag.Name;
            }

            if (!parents.TryGetValue(current, out var list) || list.Count == 0)
            {
                return null;
            }

            current = list
                .OrderByDescending(p => p.Priority)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .First().Id;
        }

        return null;
    }

    private static Tag InsertTag(
        SqliteConnection conn,
        string name,
        int priority,
        SqliteTransaction? tx = null)
    {
        var normalized = TagNames.Normalize(name);
        if (TagNames.FindId(conn, normalized, tx: tx) is not null)
        {
            throw new DuplicateTagNameException(normalized);
        }

        var tag = new Tag
        {
            Id = PalaceDb.NewId(),
            Name = normalized,
            Priority = priority,
            Slug = PathSafe.Slug(normalized)
        };
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Tag (Id, Name, Priority, Slug) VALUES ($id, $name, $priority, $slug)";
        cmd.Parameters.AddWithValue("$id", tag.Id);
        cmd.Parameters.AddWithValue("$name", tag.Name);
        cmd.Parameters.AddWithValue("$priority", tag.Priority);
        cmd.Parameters.AddWithValue("$slug", tag.Slug);
        cmd.ExecuteNonQuery();
        return tag;
    }

    /// <summary>
    /// Redo of <see cref="FileOpStep.CreateTag"/>: restore a Tag row with the journaled id.
    /// No-op when the id already exists. Refuses a name clash on a different id.
    /// </summary>
    public Task EnsureCreatedTagAsync(string tagId, string tagName) =>
        _db.WriteAsync(conn =>
        {
            if (string.IsNullOrEmpty(tagId) || string.IsNullOrWhiteSpace(tagName))
            {
                return;
            }

            using (var exists = conn.CreateCommand())
            {
                exists.CommandText = "SELECT 1 FROM Tag WHERE Id = $id LIMIT 1";
                exists.Parameters.AddWithValue("$id", tagId);
                if (exists.ExecuteScalar() is not null)
                {
                    return;
                }
            }

            var name = TagNames.Normalize(tagName);
            if (TagNames.FindId(conn, name, excludeId: tagId) is not null)
            {
                throw new DuplicateTagNameException(name);
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Tag (Id, Name, Priority, Slug) VALUES ($id, $name, 0, $slug)";
            cmd.Parameters.AddWithValue("$id", tagId);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$slug", PathSafe.Slug(name));
            cmd.ExecuteNonQuery();
        });

    private static MembershipAddResult TryAddMembership(
        SqliteConnection conn,
        string parentId,
        string childId,
        SqliteTransaction? tx = null)
    {
        if (string.IsNullOrEmpty(parentId)
            || string.IsNullOrEmpty(childId)
            || string.Equals(parentId, childId, StringComparison.Ordinal)
            || WouldCycle(conn, parentId, childId, tx))
        {
            return MembershipAddResult.CycleRefuse;
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO TagMembership (ParentId, ChildId) VALUES ($p, $c)";
        cmd.Parameters.AddWithValue("$p", parentId);
        cmd.Parameters.AddWithValue("$c", childId);
        var changes = cmd.ExecuteNonQuery();
        return changes > 0 ? MembershipAddResult.Added : MembershipAddResult.AlreadyMember;
    }

    private static bool WouldCycle(
        SqliteConnection conn,
        string parentId,
        string childId,
        SqliteTransaction? tx = null) =>
        DescendantsOf(conn, childId, tx).Contains(parentId);

    private static List<string> DescendantsOf(
        SqliteConnection conn,
        string tagId,
        SqliteTransaction? tx = null)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in LoadMemberships(conn, tx))
        {
            if (!children.TryGetValue(edge.ParentId, out var list))
            {
                list = [];
                children[edge.ParentId] = list;
            }

            list.Add(edge.ChildId);
        }

        var found = new List<string>();
        var stack = new Stack<string>();
        stack.Push(tagId);
        var seen = new HashSet<string>(StringComparer.Ordinal) { tagId };
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!children.TryGetValue(current, out var kids))
            {
                continue;
            }

            foreach (var kid in kids)
            {
                if (seen.Add(kid))
                {
                    found.Add(kid);
                    stack.Push(kid);
                }
            }
        }

        return found;
    }

    private static List<Tag> ParentsOf(SqliteConnection conn, string tagId)
    {
        var tags = LoadTags(conn).ToDictionary(t => t.Id);
        return LoadMemberships(conn)
            .Where(m => m.ChildId == tagId && tags.ContainsKey(m.ParentId))
            .Select(m => tags[m.ParentId])
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<TagPath> BuildPaths(SqliteConnection conn, string tagId)
    {
        var tags = LoadTags(conn).ToDictionary(t => t.Id);
        if (!tags.ContainsKey(tagId))
        {
            return [];
        }

        var parents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in LoadMemberships(conn))
        {
            if (!parents.TryGetValue(edge.ChildId, out var list))
            {
                list = [];
                parents[edge.ChildId] = list;
            }

            list.Add(edge.ParentId);
        }

        var results = new List<TagPath>();
        WalkUp(tagId, [], parents, tags, results);
        return results;
    }

    private static void WalkUp(
        string id,
        List<Tag> acc,
        Dictionary<string, List<string>> parents,
        Dictionary<string, Tag> tags,
        List<TagPath> results)
    {
        acc.Add(tags[id]);
        if (!parents.TryGetValue(id, out var ups) || ups.Count == 0)
        {
            var nodes = acc.ToList();
            nodes.Reverse();
            results.Add(new TagPath { Nodes = nodes });
            acc.RemoveAt(acc.Count - 1);
            return;
        }

        foreach (var parent in ups)
        {
            if (acc.Any(t => t.Id == parent) || !tags.ContainsKey(parent))
            {
                continue;
            }

            WalkUp(parent, acc, parents, tags, results);
        }

        acc.RemoveAt(acc.Count - 1);
    }
}
