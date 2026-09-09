using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

public sealed class CatalogService
{
    private readonly PalaceDb _db;

    public CatalogService(PalaceDb db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<Project>> GetProjectsAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name FROM Project ORDER BY Name COLLATE NOCASE";
            var list = new List<Project>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Project { Id = reader.GetString(0), Name = reader.GetString(1) });
            }

            return (IReadOnlyList<Project>)list;
        });

    public Task<Project> CreateProjectAsync(string name) =>
        _db.WriteAsync(conn =>
        {
            var project = new Project { Id = PalaceDb.NewId(), Name = name.Trim() };
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Project (Id, Name) VALUES ($id, $name)";
            cmd.Parameters.AddWithValue("$id", project.Id);
            cmd.Parameters.AddWithValue("$name", project.Name);
            cmd.ExecuteNonQuery();
            return project;
        });

    public Task RenameProjectAsync(string id, string name) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Project SET Name = $name WHERE Id = $id";
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });

    public Task<IReadOnlyList<SourceFolder>> GetSourceFoldersAsync(string? projectId = null) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Path, AccessToken, AutoOrganizeNewFiles, FolderTemplate, FileTemplate,
                       DestinationPolicy, DestinationPath, ProjectId
                FROM SourceFolder
                WHERE ($project IS NULL OR ProjectId = $project)
                ORDER BY Path
                """;
            cmd.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
            return (IReadOnlyList<SourceFolder>)ReadSources(cmd);
        });

    public Task<SourceFolder?> GetSourceFolderAsync(string id) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Path, AccessToken, AutoOrganizeNewFiles, FolderTemplate, FileTemplate,
                       DestinationPolicy, DestinationPath, ProjectId
                FROM SourceFolder WHERE Id = $id
                """;
            cmd.Parameters.AddWithValue("$id", id);
            var list = ReadSources(cmd);
            return list.Count > 0 ? list[0] : null;
        });

    public Task<SourceFolder?> FindSourceByPathAsync(string path) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Path, AccessToken, AutoOrganizeNewFiles, FolderTemplate, FileTemplate,
                       DestinationPolicy, DestinationPath, ProjectId
                FROM SourceFolder
                """;
            foreach (var source in ReadSources(cmd))
            {
                if (PathSafe.IsUnderRoot(path, source.Path))
                {
                    return source;
                }
            }

            return (SourceFolder?)null;
        });

    public Task UpsertSourceFolderAsync(SourceFolder folder) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO SourceFolder (Id, Path, AccessToken, AutoOrganizeNewFiles, FolderTemplate, FileTemplate,
                                          DestinationPolicy, DestinationPath, ProjectId)
                VALUES ($id, $path, $token, $auto, $folder, $file, $policy, $dest, $project)
                ON CONFLICT(Id) DO UPDATE SET
                    Path = excluded.Path,
                    AccessToken = excluded.AccessToken,
                    AutoOrganizeNewFiles = excluded.AutoOrganizeNewFiles,
                    FolderTemplate = excluded.FolderTemplate,
                    FileTemplate = excluded.FileTemplate,
                    DestinationPolicy = excluded.DestinationPolicy,
                    DestinationPath = excluded.DestinationPath,
                    ProjectId = excluded.ProjectId;
                """;
            cmd.Parameters.AddWithValue("$id", folder.Id);
            cmd.Parameters.AddWithValue("$path", folder.Path);
            cmd.Parameters.AddWithValue("$token", (object?)folder.AccessToken ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$auto", folder.AutoOrganizeNewFiles ? 1 : 0);
            cmd.Parameters.AddWithValue("$folder", (object?)folder.FolderTemplate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$file", (object?)folder.FileTemplate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$policy", folder.DestinationPolicy.ToString());
            cmd.Parameters.AddWithValue("$dest", (object?)folder.DestinationPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$project", (object?)folder.ProjectId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        });

    public Task DeleteSourceFolderAsync(string id) =>
        _db.WriteAsync(conn =>
        {
            using var tx = conn.BeginTransaction();
            using (var rows = conn.CreateCommand())
            {
                rows.Transaction = tx;
                rows.CommandText = "SELECT RowId FROM Asset WHERE SourceFolderId = $id";
                rows.Parameters.AddWithValue("$id", id);
                using var reader = rows.ExecuteReader();
                var rowIds = new List<long>();
                while (reader.Read())
                {
                    rowIds.Add(reader.GetInt64(0));
                }

                reader.Close();
                foreach (var rowId in rowIds)
                {
                    DeleteFtsRow(conn, rowId, tx);
                }
            }

            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM SourceFolder WHERE Id = $id";
                del.Parameters.AddWithValue("$id", id);
                del.ExecuteNonQuery();
            }

            tx.Commit();
        });

    public Task<IReadOnlyList<Asset>> GetAssetsAsync(string? folderPrefix = null, string? sourceId = null, string? projectId = null) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT RowId, Id, SourceFolderId, Path, FileName, ContentHash, Kind, Width, Height, DurationMs,
                       IsOrphan, Model, Seed, Prompt, NegativePrompt, MetadataJson, OrganizeError, Rating, Notes,
                       DateAdded, DateModified, FileSize
                FROM Asset
                WHERE ($source IS NULL OR SourceFolderId = $source)
                  AND ($prefix IS NULL OR Path LIKE $like)
                  AND ($project IS NULL OR SourceFolderId IN (SELECT Id FROM SourceFolder WHERE ProjectId = $project))
                ORDER BY FileName COLLATE NOCASE
                """;
            cmd.Parameters.AddWithValue("$source", (object?)sourceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$prefix", (object?)folderPrefix ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$like", folderPrefix is null ? DBNull.Value : folderPrefix.TrimEnd('\\') + @"\%");
            cmd.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
            return (IReadOnlyList<Asset>)ReadAssets(cmd);
        });

    public Task<Asset?> GetAssetByIdAsync(string id) =>
        _db.ReadAsync(conn => GetAssetById(conn, id));

    public Task<Asset?> GetAssetByPathAsync(string path) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SelectAssetSql + " WHERE Path = $path";
            cmd.Parameters.AddWithValue("$path", path);
            var list = ReadAssets(cmd);
            return list.Count > 0 ? list[0] : null;
        });

    public Task<IReadOnlyList<Asset>> GetAssetsByIdsAsync(IEnumerable<string> ids) =>
        _db.ReadAsync(conn =>
        {
            var idList = ids.ToList();
            if (idList.Count == 0)
            {
                return (IReadOnlyList<Asset>)[];
            }

            using var cmd = conn.CreateCommand();
            var names = new List<string>();
            for (var i = 0; i < idList.Count; i++)
            {
                var p = $"$id{i}";
                names.Add(p);
                cmd.Parameters.AddWithValue(p, idList[i]);
            }

            cmd.CommandText = SelectAssetSql + $" WHERE Id IN ({string.Join(",", names)})";
            return ReadAssets(cmd);
        });

    public Task UpsertAssetAsync(Asset asset, string tagsText) =>
        _db.WriteAsync(conn => UpsertAsset(conn, asset, tagsText));

    public Task UpdateAssetPathAsync(string assetId, string newPath) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE Asset SET Path = $path, FileName = $name, IsOrphan = 0, OrganizeError = NULL
                WHERE Id = $id
                """;
            cmd.Parameters.AddWithValue("$path", newPath);
            cmd.Parameters.AddWithValue("$name", Path.GetFileName(newPath));
            cmd.Parameters.AddWithValue("$id", assetId);
            cmd.ExecuteNonQuery();
            RefreshFts(conn, assetId);
        });

    public Task DeleteAssetsAsync(IEnumerable<string> assetIds) =>
        _db.WriteAsync(conn =>
        {
            var ids = assetIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0)
            {
                return;
            }

            using var tx = conn.BeginTransaction();
            foreach (var id in ids)
            {
                using var row = conn.CreateCommand();
                row.Transaction = tx;
                row.CommandText = "SELECT RowId FROM Asset WHERE Id = $id";
                row.Parameters.AddWithValue("$id", id);
                var rowId = row.ExecuteScalar();
                if (rowId is not null and not DBNull)
                {
                    DeleteFtsRow(conn, Convert.ToInt64(rowId), tx);
                }

                using var batch = conn.CreateCommand();
                batch.Transaction = tx;
                batch.CommandText = "DELETE FROM OrganizeBatchItem WHERE AssetId = $id";
                batch.Parameters.AddWithValue("$id", id);
                batch.ExecuteNonQuery();

                using var ai = conn.CreateCommand();
                ai.Transaction = tx;
                ai.CommandText = "DELETE FROM AiJob WHERE AssetId = $id";
                ai.Parameters.AddWithValue("$id", id);
                ai.ExecuteNonQuery();

                using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = "DELETE FROM Asset WHERE Id = $id";
                del.Parameters.AddWithValue("$id", id);
                del.ExecuteNonQuery();
            }

            tx.Commit();
        });

    public Task MarkOrphanAsync(string assetId, bool orphan) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Asset SET IsOrphan = $orphan WHERE Id = $id";
            cmd.Parameters.AddWithValue("$orphan", orphan ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", assetId);
            cmd.ExecuteNonQuery();
        });

    public Task SetOrganizeErrorAsync(string assetId, string? error) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Asset SET OrganizeError = $err WHERE Id = $id";
            cmd.Parameters.AddWithValue("$err", (object?)error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", assetId);
            cmd.ExecuteNonQuery();
        });

    public Task UpdateNotesAndRatingAsync(string assetId, string? notes, int? rating) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Asset SET Notes = $notes, Rating = $rating WHERE Id = $id";
            cmd.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rating", (object?)rating ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", assetId);
            cmd.ExecuteNonQuery();
            RefreshFts(conn, assetId);
        });

    public Task<IReadOnlyList<string>> GetIndexedPathsAsync(string sourceId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Path FROM Asset WHERE SourceFolderId = $id";
            cmd.Parameters.AddWithValue("$id", sourceId);
            var list = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(reader.GetString(0));
            }

            return (IReadOnlyList<string>)list;
        });

    public Task<IReadOnlyList<Tag>> GetTagsAsync() =>
        _db.ReadAsync(conn => (IReadOnlyList<Tag>)LoadTags(conn));

    public Task<Tag?> GetTagAsync(string id) =>
        _db.ReadAsync(conn => LoadTags(conn).FirstOrDefault(t => t.Id == id));

    public Task<IReadOnlyList<TagMembership>> GetMembershipsAsync() =>
        _db.ReadAsync(conn => (IReadOnlyList<TagMembership>)LoadMemberships(conn));

    public Task<IReadOnlyList<TagImplication>> GetImplicationsAsync() =>
        _db.ReadAsync(conn => (IReadOnlyList<TagImplication>)LoadImplications(conn));

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

    public Task<bool> AddImplicationAsync(string tagId, string impliedTagId) =>
        _db.WriteAsync(conn => TryAddImplication(conn, tagId, impliedTagId));

    public Task RemoveImplicationAsync(string tagId, string impliedTagId) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM TagImplication WHERE TagId = $t AND ImpliedTagId = $i";
            cmd.Parameters.AddWithValue("$t", tagId);
            cmd.Parameters.AddWithValue("$i", impliedTagId);
            cmd.ExecuteNonQuery();
        });

    public Task<Tag?> FindTagByNameAsync(string name) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, Priority, Slug, Color FROM Tag
                WHERE Name = $name COLLATE NOCASE
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$name", name);
            var list = ReadTags(cmd);
            return list.Count > 0 ? list[0] : null;
        });

    public Task<Tag> CreateTagAsync(string name, int priority = 0) =>
        _db.WriteAsync(conn => InsertTag(conn, name, priority));

    public Task<Tag> CreateChildTagAsync(string parentId, string name, int priority = 0) =>
        _db.WriteAsync(conn =>
        {
            var tag = InsertTag(conn, name, priority);
            TryAddMembership(conn, parentId, tag.Id);
            return tag;
        });

    public Task RenameTagAsync(string id, string name) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Tag SET Name = $name, Slug = $slug WHERE Id = $id";
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$slug", PathSafe.Slug(name));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            RefreshFtsForTag(conn, id);
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
            cmd.Parameters.AddWithValue("$c", (object?)TagColor.Normalize(color) ?? DBNull.Value);
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
        foreach (var edge in memberships)
        {
            if (!byId.TryGetValue(edge.ParentId, out var parent))
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

        var cache = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            ResolveEffectiveColor(tag.Id, byId, parents, cache, []);
        }

        return cache;
    }

    public static string DescribeTagColor(
        Tag tag,
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagMembership> memberships)
    {
        if (!string.IsNullOrWhiteSpace(tag.Color) && TagColor.Normalize(tag.Color) is not null)
        {
            return "Custom";
        }

        var colors = MapEffectiveColors(tags, memberships);
        if (colors.GetValueOrDefault(tag.Id) is null)
        {
            return "No color";
        }

        var ancestor = FindColorAncestorName(tag.Id, tags.ToDictionary(t => t.Id, StringComparer.Ordinal), memberships);
        return ancestor is null ? "Inherited" : $"Inherited from {ancestor}";
    }

    public async Task<int> BackfillImplicationAddedAsync(string sourceTagId, CancellationToken cancellationToken = default)
    {
        var assetIds = await _db.ReadAsync(conn =>
        {
            var sources = TagsThatImply(conn, sourceTagId);
            sources.Add(sourceTagId);
            return AssetsHavingAnyTag(conn, sources);
        }).ConfigureAwait(false);

        var updated = 0;
        foreach (var chunk in assetIds.Chunk(75))
        {
            cancellationToken.ThrowIfCancellationRequested();
            updated += await _db.WriteAsync(conn => ApplyMissingImplied(conn, chunk)).ConfigureAwait(false);
        }

        return updated;
    }

    public async Task<int> BackfillImplicationRemovedAsync(string impliedTagId, CancellationToken cancellationToken = default)
    {
        var assetIds = await _db.ReadAsync(conn => AssetsHavingAnyTag(conn, [impliedTagId])).ConfigureAwait(false);
        var updated = 0;
        foreach (var chunk in assetIds.Chunk(75))
        {
            cancellationToken.ThrowIfCancellationRequested();
            updated += await _db.WriteAsync(conn =>
            {
                var count = 0;
                foreach (var assetId in chunk)
                {
                    var before = LoadAssignedRefs(conn, assetId);
                    SweepUnjustifiedImplied(conn, assetId);
                    var after = LoadAssignedRefs(conn, assetId);
                    if (before.Count != after.Count)
                    {
                        RefreshFts(conn, assetId);
                        count++;
                    }
                }

                return count;
            }).ConfigureAwait(false);
        }

        return updated;
    }

    public Task<bool> AddMembershipAsync(string parentId, string childId) =>
        _db.WriteAsync(conn => TryAddMembership(conn, parentId, childId));

    public Task RemoveMembershipAsync(string parentId, string childId) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM TagMembership WHERE ParentId = $p AND ChildId = $c";
            cmd.Parameters.AddWithValue("$p", parentId);
            cmd.Parameters.AddWithValue("$c", childId);
            cmd.ExecuteNonQuery();
        });

    public Task<IReadOnlyList<Tag>> GetParentsAsync(string tagId) =>
        _db.ReadAsync(conn => (IReadOnlyList<Tag>)ParentsOf(conn, tagId));

    public Task<IReadOnlyList<TagPath>> GetPathsToTagAsync(string tagId) =>
        _db.ReadAsync(conn => (IReadOnlyList<TagPath>)BuildPaths(conn, tagId));

    public Task<IReadOnlyList<string>> GetDescendantTagIdsAsync(string tagId) =>
        _db.ReadAsync(conn => (IReadOnlyList<string>)DescendantsOf(conn, tagId));

    public Task<int> CountAssetsForTagsAsync(IEnumerable<string> tagIds) =>
        _db.ReadAsync(conn =>
        {
            var ids = tagIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                return 0;
            }

            using var cmd = conn.CreateCommand();
            var names = new List<string>();
            for (var i = 0; i < ids.Count; i++)
            {
                var p = $"$t{i}";
                names.Add(p);
                cmd.Parameters.AddWithValue(p, ids[i]);
            }

            cmd.CommandText = $"SELECT COUNT(DISTINCT AssetId) FROM AssetTag WHERE TagId IN ({string.Join(",", names)})";
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

    public Task<IReadOnlyList<Asset>> GetAssetsForTagsAsync(IEnumerable<string> tagIds, string? projectId = null) =>
        _db.ReadAsync(conn =>
        {
            var ids = tagIds.Distinct().ToList();
            if (ids.Count == 0)
            {
                return (IReadOnlyList<Asset>)[];
            }

            using var cmd = conn.CreateCommand();
            var names = new List<string>();
            for (var i = 0; i < ids.Count; i++)
            {
                var p = $"$t{i}";
                names.Add(p);
                cmd.Parameters.AddWithValue(p, ids[i]);
            }

            cmd.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
            cmd.CommandText = SelectAssetSql + $"""
                 WHERE Id IN (SELECT DISTINCT AssetId FROM AssetTag WHERE TagId IN ({string.Join(",", names)}))
                   AND ($project IS NULL OR SourceFolderId IN (SELECT Id FROM SourceFolder WHERE ProjectId = $project))
                 ORDER BY FileName COLLATE NOCASE
                """;
            return ReadAssets(cmd);
        });

    public Task AssignTagAsync(string assetId, string tagId, TagSource source) =>
        _db.WriteAsync(conn =>
        {
            UpsertAssetTag(conn, assetId, tagId, source);
            foreach (var impliedId in TransitiveImplied(conn, tagId))
            {
                UpsertAssetTag(conn, assetId, impliedId, TagSource.Implied);
            }

            RefreshFts(conn, assetId);
        });

    public Task RemoveTagAsync(string assetId, string tagId) =>
        _db.WriteAsync(conn =>
        {
            // Explicit remove wins: we delete the requested tag and any Implied-only
            // tags that are no longer justified by a remaining non-Implied assignment.
            // Direct (Manual/Prompt/AI) assignments are never stripped just because an
            // implying tag went away. We do not re-apply implications here, so removing
            // an Implied tag while its source tag stays assigned keeps that removal.
            DeleteAssetTag(conn, assetId, tagId);
            SweepUnjustifiedImplied(conn, assetId);
            RefreshFts(conn, assetId);
        });

    public Task<IReadOnlyList<AssignedTag>> GetAssignedTagsAsync(string assetId) =>
        _db.ReadAsync(conn => GetAssignedTags(conn, assetId));

    public Task<OrganizeRule?> GetDefaultRuleAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, FolderTemplate, FileTemplate, FacetIdsJson FROM OrganizeRule LIMIT 1";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return (OrganizeRule?)null;
            }

            return new OrganizeRule
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                FolderTemplate = reader.GetString(2),
                FileTemplate = reader.GetString(3),
                FacetIdsJson = reader.IsDBNull(4) ? null : reader.GetString(4)
            };
        });

    public Task<string> CreateOrganizeBatchAsync(IReadOnlyList<OrganizeBatchItem> items) =>
        _db.WriteAsync(conn =>
        {
            var batchId = PalaceDb.NewId();
            using var batch = conn.CreateCommand();
            batch.CommandText = "INSERT INTO OrganizeBatch (Id, CreatedAt, IsUndone) VALUES ($id, $at, 0)";
            batch.Parameters.AddWithValue("$id", batchId);
            batch.Parameters.AddWithValue("$at", PalaceDb.NowIso());
            batch.ExecuteNonQuery();

            foreach (var item in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO OrganizeBatchItem (Id, BatchId, AssetId, OldPath, NewPath, Status, Error)
                    VALUES ($id, $batch, $asset, $old, $new, $status, $err)
                    """;
                cmd.Parameters.AddWithValue("$id", PalaceDb.NewId());
                cmd.Parameters.AddWithValue("$batch", batchId);
                cmd.Parameters.AddWithValue("$asset", item.AssetId);
                cmd.Parameters.AddWithValue("$old", item.OldPath);
                cmd.Parameters.AddWithValue("$new", item.NewPath);
                cmd.Parameters.AddWithValue("$status", item.Status.ToString());
                cmd.Parameters.AddWithValue("$err", (object?)item.Error ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            return batchId;
        });

    public Task<OrganizeBatch?> GetLastUndoableBatchAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CreatedAt, IsUndone FROM OrganizeBatch
                WHERE IsUndone = 0
                ORDER BY CreatedAt DESC
                LIMIT 1
                """;
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return (OrganizeBatch?)null;
            }

            return new OrganizeBatch
            {
                Id = reader.GetString(0),
                CreatedAt = reader.GetString(1),
                IsUndone = reader.GetInt32(2) != 0
            };
        });

    public Task<IReadOnlyList<OrganizeBatchItem>> GetBatchItemsAsync(string batchId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, BatchId, AssetId, OldPath, NewPath, Status, Error
                FROM OrganizeBatchItem WHERE BatchId = $id
                """;
            cmd.Parameters.AddWithValue("$id", batchId);
            var list = new List<OrganizeBatchItem>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new OrganizeBatchItem
                {
                    Id = reader.GetString(0),
                    BatchId = reader.GetString(1),
                    AssetId = reader.GetString(2),
                    OldPath = reader.GetString(3),
                    NewPath = reader.GetString(4),
                    Status = Enum.Parse<OrganizeItemStatus>(reader.GetString(5)),
                    Error = reader.IsDBNull(6) ? null : reader.GetString(6)
                });
            }

            return (IReadOnlyList<OrganizeBatchItem>)list;
        });

    public Task MarkBatchUndoneAsync(string batchId) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE OrganizeBatch SET IsUndone = 1 WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", batchId);
            cmd.ExecuteNonQuery();
            using var items = conn.CreateCommand();
            items.CommandText = "UPDATE OrganizeBatchItem SET Status = 'Undone' WHERE BatchId = $id AND Status = 'Applied'";
            items.Parameters.AddWithValue("$id", batchId);
            items.ExecuteNonQuery();
        });

    public Task<IReadOnlyList<Room>> GetRoomsAsync(string? projectId = null) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, Kind, SortOrder, ProjectId
                FROM Collection
                WHERE Kind = 'Room' AND ($project IS NULL OR ProjectId = $project)
                ORDER BY SortOrder, Name
                """;
            cmd.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
            var list = new List<Room>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Room
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Kind = reader.GetString(2),
                    SortOrder = reader.GetInt32(3),
                    ProjectId = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }

            return (IReadOnlyList<Room>)list;
        });

    public Task<Room> CreateRoomAsync(string name, string projectId) =>
        _db.WriteAsync(conn =>
        {
            var room = new Room
            {
                Id = PalaceDb.NewId(),
                Name = name.Trim(),
                Kind = "Room",
                SortOrder = 0,
                ProjectId = projectId
            };
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Collection (Id, Name, Kind, SortOrder, ProjectId) VALUES ($id, $name, 'Room', $sort, $project)";
            cmd.Parameters.AddWithValue("$id", room.Id);
            cmd.Parameters.AddWithValue("$name", room.Name);
            cmd.Parameters.AddWithValue("$sort", room.SortOrder);
            cmd.Parameters.AddWithValue("$project", projectId);
            cmd.ExecuteNonQuery();
            return room;
        });

    public Task RenameRoomAsync(string id, string name) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Collection SET Name = $name WHERE Id = $id";
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });

    public Task DeleteRoomAsync(string id) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Collection WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });

    public Task AddToRoomAsync(string roomId, string assetId, string section, int sortOrder) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO CollectionItem (CollectionId, AssetId, Section, SortOrder)
                VALUES ($room, $asset, $section, $sort)
                ON CONFLICT(CollectionId, AssetId) DO UPDATE SET Section = excluded.Section
                """;
            cmd.Parameters.AddWithValue("$room", roomId);
            cmd.Parameters.AddWithValue("$asset", assetId);
            cmd.Parameters.AddWithValue("$section", section);
            cmd.Parameters.AddWithValue("$sort", sortOrder);
            cmd.ExecuteNonQuery();
        });

    public Task RemoveFromRoomAsync(string roomId, string assetId) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CollectionItem WHERE CollectionId = $room AND AssetId = $asset";
            cmd.Parameters.AddWithValue("$room", roomId);
            cmd.Parameters.AddWithValue("$asset", assetId);
            cmd.ExecuteNonQuery();
        });

    public Task<IReadOnlyList<RoomItem>> GetRoomItemsAsync(string roomId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT CollectionId, AssetId, Section, SortOrder
                FROM CollectionItem WHERE CollectionId = $id
                ORDER BY Section, SortOrder
                """;
            cmd.Parameters.AddWithValue("$id", roomId);
            var list = new List<RoomItem>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new RoomItem
                {
                    CollectionId = reader.GetString(0),
                    AssetId = reader.GetString(1),
                    Section = reader.IsDBNull(2) ? "Pins" : reader.GetString(2),
                    SortOrder = reader.GetInt32(3)
                });
            }

            return (IReadOnlyList<RoomItem>)list;
        });

    public Task SaveRoomOrderAsync(string roomId, IReadOnlyList<RoomItem> items) =>
        _db.WriteAsync(conn =>
        {
            foreach (var item in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    UPDATE CollectionItem SET Section = $section, SortOrder = $sort
                    WHERE CollectionId = $room AND AssetId = $asset
                    """;
                cmd.Parameters.AddWithValue("$section", item.Section ?? "Pins");
                cmd.Parameters.AddWithValue("$sort", item.SortOrder);
                cmd.Parameters.AddWithValue("$room", roomId);
                cmd.Parameters.AddWithValue("$asset", item.AssetId);
                cmd.ExecuteNonQuery();
            }
        });

    public Task<IReadOnlyList<Asset>> SearchAsync(string query, string? projectId = null) =>
        _db.ReadAsync(conn =>
        {
            var match = ToFtsQuery(query);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SelectAssetSql + """
                 WHERE RowId IN (SELECT rowid FROM AssetFts WHERE AssetFts MATCH $q)
                   AND ($project IS NULL OR SourceFolderId IN (SELECT Id FROM SourceFolder WHERE ProjectId = $project))
                 ORDER BY FileName COLLATE NOCASE
                """;
            cmd.Parameters.AddWithValue("$q", match);
            cmd.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
            try
            {
                return ReadAssets(cmd);
            }
            catch (SqliteException)
            {
                return (IReadOnlyList<Asset>)[];
            }
        });

    public void RefreshFts(SqliteConnection conn, string assetId)
    {
        try
        {
            var asset = GetAssetById(conn, assetId);
            if (asset is null)
            {
                return;
            }

            var tags = string.Join(" ", AssignedTagNamesForFts(conn, assetId));
            DeleteFtsRow(conn, asset.RowId);

            using var ins = conn.CreateCommand();
            ins.CommandText = """
                INSERT INTO AssetFts (rowid, FileName, Path, Tags, Prompt, Model, Notes)
                VALUES ($row, $name, $path, $tags, $prompt, $model, $notes)
                """;
            ins.Parameters.AddWithValue("$row", asset.RowId);
            ins.Parameters.AddWithValue("$name", asset.FileName);
            ins.Parameters.AddWithValue("$path", asset.Path);
            ins.Parameters.AddWithValue("$tags", tags);
            ins.Parameters.AddWithValue("$prompt", asset.Prompt ?? "");
            ins.Parameters.AddWithValue("$model", asset.Model ?? "");
            ins.Parameters.AddWithValue("$notes", asset.Notes ?? "");
            ins.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Tag writes must succeed even if the contentless FTS index is stale.
        }
    }

    private static void DeleteFtsRow(SqliteConnection conn, long rowId, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // content='' FTS5 tables reject DELETE; tombstone with the 'delete' command.
        cmd.CommandText = """
            INSERT INTO AssetFts(AssetFts, rowid, FileName, Path, Tags, Prompt, Model, Notes)
            VALUES('delete', $row, '', '', '', '', '', '')
            """;
        cmd.Parameters.AddWithValue("$row", rowId);
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Row was never indexed.
        }
    }

    internal void UpsertAsset(SqliteConnection conn, Asset asset, string tagsText)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Asset (Id, SourceFolderId, Path, FileName, ContentHash, Kind, Width, Height, DurationMs,
                               IsOrphan, Model, Seed, Prompt, NegativePrompt, MetadataJson, OrganizeError, Rating, Notes,
                               DateAdded, DateModified, FileSize)
            VALUES ($id, $source, $path, $name, $hash, $kind, $w, $h, $dur, $orphan, $model, $seed, $prompt, $neg,
                    $json, $err, $rating, $notes, $added, $mod, $size)
            ON CONFLICT(Path) DO UPDATE SET
                SourceFolderId = excluded.SourceFolderId,
                FileName = excluded.FileName,
                ContentHash = excluded.ContentHash,
                Kind = excluded.Kind,
                Width = excluded.Width,
                Height = excluded.Height,
                DurationMs = excluded.DurationMs,
                IsOrphan = 0,
                Model = excluded.Model,
                Seed = excluded.Seed,
                Prompt = excluded.Prompt,
                NegativePrompt = excluded.NegativePrompt,
                MetadataJson = excluded.MetadataJson,
                DateModified = excluded.DateModified,
                FileSize = excluded.FileSize
            RETURNING RowId, Id;
            """;
        cmd.Parameters.AddWithValue("$id", asset.Id);
        cmd.Parameters.AddWithValue("$source", asset.SourceFolderId);
        cmd.Parameters.AddWithValue("$path", asset.Path);
        cmd.Parameters.AddWithValue("$name", asset.FileName);
        cmd.Parameters.AddWithValue("$hash", (object?)asset.ContentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", asset.Kind.ToString());
        cmd.Parameters.AddWithValue("$w", (object?)asset.Width ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$h", (object?)asset.Height ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dur", (object?)asset.DurationMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$orphan", asset.IsOrphan ? 1 : 0);
        cmd.Parameters.AddWithValue("$model", (object?)asset.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$seed", (object?)asset.Seed ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$prompt", (object?)asset.Prompt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$neg", (object?)asset.NegativePrompt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$json", (object?)asset.MetadataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$err", (object?)asset.OrganizeError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rating", (object?)asset.Rating ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)asset.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$added", asset.DateAdded);
        cmd.Parameters.AddWithValue("$mod", (object?)asset.DateModified ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$size", (object?)asset.FileSize ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            asset.RowId = reader.GetInt64(0);
            asset.Id = reader.GetString(1);
        }

        reader.Close();
        RefreshFts(conn, asset.Id);
    }

    internal static Asset? GetAssetById(SqliteConnection conn, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectAssetSql + " WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        var list = ReadAssets(cmd);
        return list.Count > 0 ? list[0] : null;
    }

    internal static IReadOnlyList<AssignedTag> GetAssignedTags(SqliteConnection conn, string assetId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.Id, t.Name, t.Priority, at.Source, t.Slug
            FROM AssetTag at
            JOIN Tag t ON t.Id = at.TagId
            WHERE at.AssetId = $id
            ORDER BY t.Priority DESC, t.Name
            """;
        cmd.Parameters.AddWithValue("$id", assetId);
        var list = new List<AssignedTag>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                list.Add(new AssignedTag
                {
                    TagId = reader.GetString(0),
                    TagName = reader.GetString(1),
                    TagPriority = reader.GetInt32(2),
                    Source = Enum.Parse<TagSource>(reader.GetString(3)),
                    Slug = reader.IsDBNull(4) ? "" : reader.GetString(4)
                });
            }
        }

        var tags = LoadTags(conn);
        var memberships = LoadMemberships(conn);
        var colors = MapEffectiveColors(tags, memberships);
        var byId = tags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        foreach (var tag in list)
        {
            tag.ParentNames = memberships
                .Where(m => m.ChildId == tag.TagId && byId.ContainsKey(m.ParentId))
                .Select(m => byId[m.ParentId].Name)
                .ToList();
            tag.EffectiveColor = colors.GetValueOrDefault(tag.TagId);
        }

        return list;
    }

    private const string SelectAssetSql = """
        SELECT RowId, Id, SourceFolderId, Path, FileName, ContentHash, Kind, Width, Height, DurationMs,
               IsOrphan, Model, Seed, Prompt, NegativePrompt, MetadataJson, OrganizeError, Rating, Notes,
               DateAdded, DateModified, FileSize
        FROM Asset
        """;

    private static List<SourceFolder> ReadSources(SqliteCommand cmd)
    {
        var list = new List<SourceFolder>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SourceFolder
            {
                Id = reader.GetString(0),
                Path = reader.GetString(1),
                AccessToken = reader.IsDBNull(2) ? null : reader.GetString(2),
                AutoOrganizeNewFiles = reader.GetInt32(3) != 0,
                FolderTemplate = reader.IsDBNull(4) ? null : reader.GetString(4),
                FileTemplate = reader.IsDBNull(5) ? null : reader.GetString(5),
                DestinationPolicy = Enum.TryParse<DestinationPolicy>(reader.GetString(6), out var p) ? p : DestinationPolicy.InSource,
                DestinationPath = reader.IsDBNull(7) ? null : reader.GetString(7),
                ProjectId = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return list;
    }

    private static List<Asset> ReadAssets(SqliteCommand cmd)
    {
        var list = new List<Asset>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Asset
            {
                RowId = reader.GetInt64(0),
                Id = reader.GetString(1),
                SourceFolderId = reader.GetString(2),
                Path = reader.GetString(3),
                FileName = reader.GetString(4),
                ContentHash = reader.IsDBNull(5) ? null : reader.GetString(5),
                Kind = Enum.TryParse<AssetKind>(reader.GetString(6), out var kind) ? kind : AssetKind.Other,
                Width = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Height = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                DurationMs = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                IsOrphan = reader.GetInt32(10) != 0,
                Model = reader.IsDBNull(11) ? null : reader.GetString(11),
                Seed = reader.IsDBNull(12) ? null : reader.GetString(12),
                Prompt = reader.IsDBNull(13) ? null : reader.GetString(13),
                NegativePrompt = reader.IsDBNull(14) ? null : reader.GetString(14),
                MetadataJson = reader.IsDBNull(15) ? null : reader.GetString(15),
                OrganizeError = reader.IsDBNull(16) ? null : reader.GetString(16),
                Rating = reader.IsDBNull(17) ? null : reader.GetInt32(17),
                Notes = reader.IsDBNull(18) ? null : reader.GetString(18),
                DateAdded = reader.GetString(19),
                DateModified = reader.IsDBNull(20) ? null : reader.GetString(20),
                FileSize = reader.IsDBNull(21) ? null : reader.GetInt64(21)
            });
        }

        return list;
    }

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
                Color = reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetString(4) : null
            });
        }

        return list;
    }

    private static List<Tag> LoadTags(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Priority, Slug, Color FROM Tag ORDER BY Priority DESC, Name COLLATE NOCASE";
        return ReadTags(cmd);
    }

    private static List<TagMembership> LoadMemberships(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ParentId, ChildId FROM TagMembership";
        var list = new List<TagMembership>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TagMembership { ParentId = reader.GetString(0), ChildId = reader.GetString(1) });
        }

        return list;
    }

    private static List<TagImplication> LoadImplications(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TagId, ImpliedTagId FROM TagImplication";
        var list = new List<TagImplication>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TagImplication { TagId = reader.GetString(0), ImpliedTagId = reader.GetString(1) });
        }

        return list;
    }

    private static void UpsertAssetTag(SqliteConnection conn, string assetId, string tagId, TagSource source)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AssetTag (AssetId, TagId, Source)
            VALUES ($asset, $tag, $source)
            ON CONFLICT(AssetId, TagId) DO UPDATE SET
                Source = CASE
                    WHEN excluded.Source = 'Implied' THEN AssetTag.Source
                    ELSE excluded.Source
                END
            """;
        cmd.Parameters.AddWithValue("$asset", assetId);
        cmd.Parameters.AddWithValue("$tag", tagId);
        cmd.Parameters.AddWithValue("$source", source.ToString());
        cmd.ExecuteNonQuery();
    }

    private static void DeleteAssetTag(SqliteConnection conn, string assetId, string tagId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM AssetTag WHERE AssetId = $asset AND TagId = $tag";
        cmd.Parameters.AddWithValue("$asset", assetId);
        cmd.Parameters.AddWithValue("$tag", tagId);
        cmd.ExecuteNonQuery();
    }

    private static bool TryAddImplication(SqliteConnection conn, string tagId, string impliedTagId)
    {
        if (string.Equals(tagId, impliedTagId, StringComparison.Ordinal) || WouldImplicationCycle(conn, tagId, impliedTagId))
        {
            return false;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO TagImplication (TagId, ImpliedTagId) VALUES ($t, $i)";
        cmd.Parameters.AddWithValue("$t", tagId);
        cmd.Parameters.AddWithValue("$i", impliedTagId);
        cmd.ExecuteNonQuery();
        return true;
    }

    private static bool WouldImplicationCycle(SqliteConnection conn, string tagId, string impliedTagId) =>
        TransitiveImplied(conn, impliedTagId).Contains(tagId);

    private static List<string> TransitiveImplied(SqliteConnection conn, string tagId)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in LoadImplications(conn))
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

        if (TagColor.Normalize(tag.Color) is { } own)
        {
            cache[tagId] = own;
            visiting.Remove(tagId);
            return own;
        }

        if (!parents.TryGetValue(tagId, out var list) || list.Count == 0)
        {
            cache[tagId] = null;
            visiting.Remove(tagId);
            return null;
        }

        var winner = list
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .First();
        var inherited = ResolveEffectiveColor(winner.Id, tags, parents, cache, visiting);
        cache[tagId] = inherited;
        visiting.Remove(tagId);
        return inherited;
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
            if (current != tagId && TagColor.Normalize(tag.Color) is not null)
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

    private static List<string> TagsThatImply(SqliteConnection conn, string targetId)
    {
        var sources = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in LoadImplications(conn))
        {
            if (!sources.TryGetValue(edge.ImpliedTagId, out var list))
            {
                list = [];
                sources[edge.ImpliedTagId] = list;
            }

            list.Add(edge.TagId);
        }

        var found = new List<string>();
        var stack = new Stack<string>();
        stack.Push(targetId);
        var seen = new HashSet<string>(StringComparer.Ordinal) { targetId };
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!sources.TryGetValue(current, out var parents))
            {
                continue;
            }

            foreach (var parent in parents)
            {
                if (seen.Add(parent))
                {
                    found.Add(parent);
                    stack.Push(parent);
                }
            }
        }

        return found;
    }

    private static List<string> AssetsHavingAnyTag(SqliteConnection conn, IReadOnlyList<string> tagIds)
    {
        var ids = tagIds.Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        using var cmd = conn.CreateCommand();
        var names = new List<string>();
        for (var i = 0; i < ids.Count; i++)
        {
            var p = $"$t{i}";
            names.Add(p);
            cmd.Parameters.AddWithValue(p, ids[i]);
        }

        cmd.CommandText = $"SELECT DISTINCT AssetId FROM AssetTag WHERE TagId IN ({string.Join(",", names)})";
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }

        return list;
    }

    private int ApplyMissingImplied(SqliteConnection conn, IReadOnlyList<string> assetIds)
    {
        var updated = 0;
        foreach (var assetId in assetIds)
        {
            var assigned = LoadAssignedRefs(conn, assetId);
            var have = assigned.Select(t => t.TagId).ToHashSet(StringComparer.Ordinal);
            var changed = false;
            foreach (var tag in assigned)
            {
                foreach (var impliedId in TransitiveImplied(conn, tag.TagId))
                {
                    if (!have.Add(impliedId))
                    {
                        continue;
                    }

                    UpsertAssetTag(conn, assetId, impliedId, TagSource.Implied);
                    changed = true;
                }
            }

            if (changed)
            {
                RefreshFts(conn, assetId);
                updated++;
            }
        }

        return updated;
    }

    private static List<(string TagId, TagSource Source)> LoadAssignedRefs(SqliteConnection conn, string assetId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TagId, Source FROM AssetTag WHERE AssetId = $id";
        cmd.Parameters.AddWithValue("$id", assetId);
        var list = new List<(string, TagSource)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add((reader.GetString(0), Enum.Parse<TagSource>(reader.GetString(1))));
        }

        return list;
    }

    private static void SweepUnjustifiedImplied(SqliteConnection conn, string assetId)
    {
        var remaining = LoadAssignedRefs(conn, assetId);
        var justified = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assigned in remaining.Where(t => t.Source != TagSource.Implied))
        {
            foreach (var impliedId in TransitiveImplied(conn, assigned.TagId))
            {
                justified.Add(impliedId);
            }
        }

        foreach (var orphan in remaining.Where(t => t.Source == TagSource.Implied && !justified.Contains(t.TagId)))
        {
            DeleteAssetTag(conn, assetId, orphan.TagId);
        }
    }

    private static Tag InsertTag(SqliteConnection conn, string name, int priority)
    {
        var tag = new Tag
        {
            Id = PalaceDb.NewId(),
            Name = name.Trim(),
            Priority = priority,
            Slug = PathSafe.Slug(name)
        };
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Tag (Id, Name, Priority, Slug) VALUES ($id, $name, $priority, $slug)";
        cmd.Parameters.AddWithValue("$id", tag.Id);
        cmd.Parameters.AddWithValue("$name", tag.Name);
        cmd.Parameters.AddWithValue("$priority", tag.Priority);
        cmd.Parameters.AddWithValue("$slug", tag.Slug);
        cmd.ExecuteNonQuery();
        return tag;
    }

    private static bool TryAddMembership(SqliteConnection conn, string parentId, string childId)
    {
        if (string.Equals(parentId, childId, StringComparison.Ordinal) || WouldCycle(conn, parentId, childId))
        {
            return false;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO TagMembership (ParentId, ChildId) VALUES ($p, $c)";
        cmd.Parameters.AddWithValue("$p", parentId);
        cmd.Parameters.AddWithValue("$c", childId);
        cmd.ExecuteNonQuery();
        return true;
    }

    private static bool WouldCycle(SqliteConnection conn, string parentId, string childId) =>
        DescendantsOf(conn, childId).Contains(parentId);

    private static List<string> DescendantsOf(SqliteConnection conn, string tagId)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in LoadMemberships(conn))
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

    private void RefreshFtsForTag(SqliteConnection conn, string tagId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT AssetId FROM AssetTag WHERE TagId = $id";
        cmd.Parameters.AddWithValue("$id", tagId);
        var ids = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
        }

        foreach (var assetId in ids)
        {
            RefreshFts(conn, assetId);
        }
    }

    private static IEnumerable<string> AssignedTagNamesForFts(SqliteConnection conn, string assetId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT t.Name FROM AssetTag at JOIN Tag t ON t.Id = at.TagId WHERE at.AssetId = $id";
        cmd.Parameters.AddWithValue("$id", assetId);
        var names = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                names.Add(reader.GetString(0));
            }
        }

        return names;
    }

    private static string ToFtsQuery(string query)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in query)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        if (parts.Count == 0)
        {
            return "\"\"";
        }

        return string.Join(" AND ", parts.Select(p =>
        {
            var clean = p.Replace("\"", "");
            return string.IsNullOrWhiteSpace(clean) ? "\"\"" : $"{clean}*";
        }));
    }
}
