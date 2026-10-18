using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>Catalog: asset rows (query, upsert, move, delete, orphan, notes / rating).</summary>
public sealed partial class CatalogService
{
    public Task<IReadOnlyList<Asset>> GetAssetsAsync(
        string? folderPrefix = null,
        string? sourceId = null,
        string? projectId = null,
        MosaicSort sort = MosaicSort.Name) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SelectAssetSql + $"""
                WHERE ($source IS NULL OR SourceFolderId = $source)
                  AND ($prefix IS NULL OR Path LIKE $like)
                  AND ($project IS NULL OR SourceFolderId IN (SELECT Id FROM SourceFolder WHERE ProjectId = $project))
                {MosaicSortOrder.OrderBySql(sort)}
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

    public Task<Asset?> GetAssetByCloudItemIdAsync(string sourceFolderId, string cloudItemId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SelectAssetSql + " WHERE SourceFolderId = $source AND CloudItemId = $cloud LIMIT 1";
            cmd.Parameters.AddWithValue("$source", sourceFolderId);
            cmd.Parameters.AddWithValue("$cloud", cloudItemId);
            var list = ReadAssets(cmd);
            return list.Count > 0 ? list[0] : null;
        });

    public Task<IReadOnlyList<string>> GetIndexedCloudItemIdsAsync(string sourceId) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CloudItemId FROM Asset WHERE SourceFolderId = $id AND CloudItemId IS NOT NULL AND CloudItemId != ''";
            cmd.Parameters.AddWithValue("$id", sourceId);
            var list = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(reader.GetString(0));
            }

            return (IReadOnlyList<string>)list;
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
            foreach (var chunk in Helpers.MosaicDelete.ChunkIds(ids))
            {
                var names = new List<string>(chunk.Count);
                using var rows = conn.CreateCommand();
                rows.Transaction = tx;
                for (var i = 0; i < chunk.Count; i++)
                {
                    var p = $"$id{i}";
                    names.Add(p);
                    rows.Parameters.AddWithValue(p, chunk[i]);
                }

                var inList = string.Join(",", names);
                rows.CommandText = $"SELECT RowId FROM Asset WHERE Id IN ({inList})";
                // Materialize first — shared connection cannot DeleteFtsRow while the reader is open.
                var ftsRows = new List<long>(chunk.Count);
                using (var reader = rows.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ftsRows.Add(reader.GetInt64(0));
                    }
                }

                foreach (var rowId in ftsRows)
                {
                    DeleteFtsRow(conn, rowId, tx);
                }

                using (var batch = conn.CreateCommand())
                {
                    batch.Transaction = tx;
                    batch.CommandText = $"DELETE FROM OrganizeBatchItem WHERE AssetId IN ({inList})";
                    for (var i = 0; i < chunk.Count; i++)
                    {
                        batch.Parameters.AddWithValue(names[i], chunk[i]);
                    }

                    batch.ExecuteNonQuery();
                }

                using (var ai = conn.CreateCommand())
                {
                    ai.Transaction = tx;
                    ai.CommandText = $"DELETE FROM AiJob WHERE AssetId IN ({inList})";
                    for (var i = 0; i < chunk.Count; i++)
                    {
                        ai.Parameters.AddWithValue(names[i], chunk[i]);
                    }

                    ai.ExecuteNonQuery();
                }

                using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = $"DELETE FROM Asset WHERE Id IN ({inList})";
                    for (var i = 0; i < chunk.Count; i++)
                    {
                        del.Parameters.AddWithValue(names[i], chunk[i]);
                    }

                    del.ExecuteNonQuery();
                }
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

    /// <summary>
    /// Mark catalog rows under a deleted directory prefix as orphan (watcher folder delete).
    /// Does not delete rows — user Recycle delete uses <see cref="DeleteAssetsAsync"/>.
    /// </summary>
    public Task MarkOrphansUnderPrefixAsync(string sourceId, string directoryPath) =>
        _db.WriteAsync(conn =>
        {
            if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            var trimmed = directoryPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0)
            {
                return;
            }

            var prefix = trimmed + Path.DirectorySeparatorChar;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE Asset SET IsOrphan = 1
                WHERE SourceFolderId = $source
                  AND (Path = $exact OR Path LIKE $like ESCAPE '\')
                """;
            cmd.Parameters.AddWithValue("$source", sourceId);
            cmd.Parameters.AddWithValue("$exact", trimmed);
            cmd.Parameters.AddWithValue("$like", EscapeLikePrefix(prefix) + "%");
            cmd.ExecuteNonQuery();
        });

    private static string EscapeLikePrefix(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

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

    internal void UpsertAsset(SqliteConnection conn, Asset asset, string tagsText)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Asset (Id, SourceFolderId, Path, FileName, ContentHash, Kind, Width, Height, DurationMs,
                               IsOrphan, Model, Seed, Prompt, NegativePrompt, MetadataJson, OrganizeError, Rating, Notes,
                               DateAdded, DateModified, FileSize, IsOnlineOnly, CloudItemId, IsHdr, IndexVersion)
            VALUES ($id, $source, $path, $name, $hash, $kind, $w, $h, $dur, $orphan, $model, $seed, $prompt, $neg,
                    $json, $err, $rating, $notes, $added, $mod, $size, $online, $cloud, $hdr, $iv)
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
                FileSize = excluded.FileSize,
                IsOnlineOnly = excluded.IsOnlineOnly,
                CloudItemId = excluded.CloudItemId,
                IsHdr = excluded.IsHdr,
                IndexVersion = excluded.IndexVersion
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
        cmd.Parameters.AddWithValue("$online", asset.IsOnlineOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("$cloud", (object?)asset.CloudItemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hdr", asset.IsHdr ? 1 : 0);
        cmd.Parameters.AddWithValue("$iv", asset.IndexVersion);
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

    private const string SelectAssetSql = """
        SELECT RowId, Id, SourceFolderId, Path, FileName, ContentHash, Kind, Width, Height, DurationMs,
               IsOrphan, Model, Seed, Prompt, NegativePrompt, MetadataJson, OrganizeError, Rating, Notes,
               DateAdded, DateModified, FileSize, IsOnlineOnly, CloudItemId, IsHdr, IndexVersion
        FROM Asset
        """ + "\n";

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
                FileSize = reader.IsDBNull(21) ? null : reader.GetInt64(21),
                IsOnlineOnly = reader.FieldCount > 22 && !reader.IsDBNull(22) && reader.GetInt32(22) != 0,
                CloudItemId = reader.FieldCount > 23 && !reader.IsDBNull(23) ? reader.GetString(23) : null,
                IsHdr = reader.FieldCount > 24 && !reader.IsDBNull(24) && reader.GetInt32(24) != 0,
                IndexVersion = reader.FieldCount > 25 && !reader.IsDBNull(25) ? reader.GetInt32(25) : 0
            });
        }

        return list;
    }
}
