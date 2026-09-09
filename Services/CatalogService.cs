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

    public Task<IReadOnlyList<SourceFolder>> GetSourceFoldersAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Path, AccessToken, AutoOrganizeNewFiles, FolderTemplate, FileTemplate,
                       DestinationPolicy, DestinationPath, ProjectId
                FROM SourceFolder
                ORDER BY Path
                """;
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
            using (var fts = conn.CreateCommand())
            {
                fts.Transaction = tx;
                fts.CommandText = """
                    DELETE FROM AssetFts WHERE rowid IN (
                        SELECT RowId FROM Asset WHERE SourceFolderId = $id
                    );
                    """;
                fts.Parameters.AddWithValue("$id", id);
                fts.ExecuteNonQuery();
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

    public Task<IReadOnlyList<Asset>> GetAssetsAsync(string? folderPrefix = null, string? sourceId = null) =>
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
                ORDER BY FileName COLLATE NOCASE
                """;
            cmd.Parameters.AddWithValue("$source", (object?)sourceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$prefix", (object?)folderPrefix ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$like", folderPrefix is null ? DBNull.Value : folderPrefix.TrimEnd('\\') + @"\%");
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

    public Task<IReadOnlyList<TagFacet>> GetFacetsAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, ParentId, Priority, Slug FROM TagFacet ORDER BY Priority DESC, Name";
            var list = new List<TagFacet>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new TagFacet
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    ParentId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Priority = reader.GetInt32(3),
                    Slug = reader.GetString(4)
                });
            }

            return (IReadOnlyList<TagFacet>)list;
        });

    public Task<IReadOnlyList<Tag>> GetTagsAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, FacetId, ParentId, Priority, Slug FROM Tag ORDER BY Priority DESC, Name";
            return (IReadOnlyList<Tag>)ReadTags(cmd);
        });

    public Task<Tag?> FindTagByNameAsync(string name) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, FacetId, ParentId, Priority, Slug FROM Tag
                WHERE Name = $name COLLATE NOCASE
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$name", name);
            var list = ReadTags(cmd);
            return list.Count > 0 ? list[0] : null;
        });

    public Task<Tag> CreateTagAsync(string name, string facetId, string? parentId = null, int priority = 0) =>
        _db.WriteAsync(conn =>
        {
            var tag = new Tag
            {
                Id = PalaceDb.NewId(),
                Name = name.Trim(),
                FacetId = facetId,
                ParentId = parentId,
                Priority = priority,
                Slug = PathSafe.Slug(name)
            };
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Tag (Id, Name, FacetId, ParentId, Priority, Slug)
                VALUES ($id, $name, $facet, $parent, $priority, $slug)
                """;
            cmd.Parameters.AddWithValue("$id", tag.Id);
            cmd.Parameters.AddWithValue("$name", tag.Name);
            cmd.Parameters.AddWithValue("$facet", tag.FacetId);
            cmd.Parameters.AddWithValue("$parent", (object?)tag.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$priority", tag.Priority);
            cmd.Parameters.AddWithValue("$slug", tag.Slug);
            cmd.ExecuteNonQuery();
            return tag;
        });

    public Task AssignTagAsync(string assetId, string tagId, TagSource source) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO AssetTag (AssetId, TagId, Source)
                VALUES ($asset, $tag, $source)
                ON CONFLICT(AssetId, TagId) DO UPDATE SET Source = excluded.Source
                """;
            cmd.Parameters.AddWithValue("$asset", assetId);
            cmd.Parameters.AddWithValue("$tag", tagId);
            cmd.Parameters.AddWithValue("$source", source.ToString());
            cmd.ExecuteNonQuery();
            RefreshFts(conn, assetId);
        });

    public Task RemoveTagAsync(string assetId, string tagId) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM AssetTag WHERE AssetId = $asset AND TagId = $tag";
            cmd.Parameters.AddWithValue("$asset", assetId);
            cmd.Parameters.AddWithValue("$tag", tagId);
            cmd.ExecuteNonQuery();
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

    public Task<IReadOnlyList<Room>> GetRoomsAsync() =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Kind, SortOrder FROM Collection WHERE Kind = 'Room' ORDER BY SortOrder, Name";
            var list = new List<Room>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Room
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Kind = reader.GetString(2),
                    SortOrder = reader.GetInt32(3)
                });
            }

            return (IReadOnlyList<Room>)list;
        });

    public Task<Room> CreateRoomAsync(string name) =>
        _db.WriteAsync(conn =>
        {
            var room = new Room { Id = PalaceDb.NewId(), Name = name.Trim(), Kind = "Room", SortOrder = 0 };
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Collection (Id, Name, Kind, SortOrder) VALUES ($id, $name, 'Room', $sort)";
            cmd.Parameters.AddWithValue("$id", room.Id);
            cmd.Parameters.AddWithValue("$name", room.Name);
            cmd.Parameters.AddWithValue("$sort", room.SortOrder);
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

    public Task<IReadOnlyList<Asset>> SearchAsync(string query) =>
        _db.ReadAsync(conn =>
        {
            var match = ToFtsQuery(query);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SelectAssetSql + """
                 WHERE RowId IN (SELECT rowid FROM AssetFts WHERE AssetFts MATCH $q)
                 ORDER BY FileName COLLATE NOCASE
                """;
            cmd.Parameters.AddWithValue("$q", match);
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
        var asset = GetAssetById(conn, assetId);
        if (asset is null)
        {
            return;
        }

        var tags = string.Join(" ", GetAssignedTags(conn, assetId).Select(t => t.TagName));
        using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM AssetFts WHERE rowid = $row";
        del.Parameters.AddWithValue("$row", asset.RowId);
        del.ExecuteNonQuery();

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
            SELECT t.Id, t.Name, t.FacetId, f.Name, f.Priority, t.Priority, at.Source, t.Slug
            FROM AssetTag at
            JOIN Tag t ON t.Id = at.TagId
            JOIN TagFacet f ON f.Id = t.FacetId
            WHERE at.AssetId = $id
            ORDER BY f.Priority DESC, t.Priority DESC, t.Name
            """;
        cmd.Parameters.AddWithValue("$id", assetId);
        var list = new List<AssignedTag>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AssignedTag
            {
                TagId = reader.GetString(0),
                TagName = reader.GetString(1),
                FacetId = reader.GetString(2),
                FacetName = reader.GetString(3),
                FacetPriority = reader.GetInt32(4),
                TagPriority = reader.GetInt32(5),
                Source = Enum.Parse<TagSource>(reader.GetString(6)),
                Slug = reader.GetString(7)
            });
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
                FacetId = reader.GetString(2),
                ParentId = reader.IsDBNull(3) ? null : reader.GetString(3),
                Priority = reader.GetInt32(4),
                Slug = reader.GetString(5)
            });
        }

        return list;
    }

    private static string ToFtsQuery(string query)
    {
        var parts = query.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return "\"\"";
        }

        return string.Join(" AND ", parts.Select(p =>
        {
            var clean = p.Replace("\"", "").Replace("*", "");
            return string.IsNullOrWhiteSpace(clean) ? "\"\"" : $"{clean}*";
        }));
    }
}
