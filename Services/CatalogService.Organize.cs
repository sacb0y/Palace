using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>Catalog: organize rules and undoable organize batches.</summary>
public sealed partial class CatalogService
{
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
}
