using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Models;

namespace Palace.Services;

/// <summary>
/// Catalog side of <see cref="FileOpService"/>: asset relocation after a move / folder rename,
/// and the undo journal (<c>FileOpBatch</c> / <c>FileOpItem</c>). No disk access here.
/// </summary>
public sealed partial class CatalogService
{
    /// <summary>Point an asset row at its new file (and source, when it moved between watched folders).</summary>
    public Task UpdateAssetLocationAsync(string assetId, string newPath, string sourceFolderId) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE Asset SET Path = $path, FileName = $name, SourceFolderId = $source,
                    IsOrphan = 0, OrganizeError = NULL
                WHERE Id = $id
                """;
            cmd.Parameters.AddWithValue("$path", newPath);
            cmd.Parameters.AddWithValue("$name", Path.GetFileName(newPath));
            cmd.Parameters.AddWithValue("$source", sourceFolderId);
            cmd.Parameters.AddWithValue("$id", assetId);
            cmd.ExecuteNonQuery();
            RefreshFts(conn, assetId);
        });

    /// <summary>True when a catalog row (even an orphan) already owns <paramref name="path"/>.</summary>
    public Task<bool> AssetPathExistsAsync(string path) =>
        _db.ReadAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM Asset WHERE Path = $path COLLATE NOCASE LIMIT 1";
            cmd.Parameters.AddWithValue("$path", path);
            return cmd.ExecuteScalar() is not null;
        });

    /// <summary>
    /// After a folder rename on disk: rewrite every row under <paramref name="oldDir"/> to sit
    /// under <paramref name="newDir"/>. Returns the number of rows changed.
    /// </summary>
    public Task<int> RebaseFolderPathsAsync(string oldDir, string newDir) =>
        _db.WriteAsync(conn =>
        {
            var oldTrim = oldDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var newTrim = newDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (oldTrim.Length == 0 || newTrim.Length == 0)
            {
                return 0;
            }

            var ids = new List<string>();
            using (var find = conn.CreateCommand())
            {
                find.CommandText = "SELECT Id FROM Asset WHERE Path LIKE $like ESCAPE '\\'";
                find.Parameters.AddWithValue("$like", EscapeLikePrefix(oldTrim + Path.DirectorySeparatorChar) + "%");
                using var reader = find.ExecuteReader();
                while (reader.Read())
                {
                    ids.Add(reader.GetString(0));
                }
            }

            if (ids.Count == 0)
            {
                return 0;
            }

            using var tx = conn.BeginTransaction();
            foreach (var id in ids)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    UPDATE Asset SET Path = $new || substr(Path, $len + 1), IsOrphan = 0
                    WHERE Id = $id
                    """;
                cmd.Parameters.AddWithValue("$new", newTrim);
                cmd.Parameters.AddWithValue("$len", oldTrim.Length);
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            foreach (var id in ids)
            {
                RefreshFts(conn, id);
            }

            return ids.Count;
        });

    public Task<string> AddFileOpBatchAsync(FileOpBatch batch) =>
        _db.WriteAsync(conn =>
        {
            if (string.IsNullOrEmpty(batch.Id))
            {
                batch.Id = PalaceDb.NewId();
            }

            if (string.IsNullOrEmpty(batch.CreatedAt))
            {
                batch.CreatedAt = PalaceDb.NowIso();
            }

            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO FileOpBatch (Id, ProjectId, Kind, Summary, CreatedAt, IsUndone)
                    VALUES ($id, $project, $kind, $summary, $at, 0)
                    """;
                cmd.Parameters.AddWithValue("$id", batch.Id);
                cmd.Parameters.AddWithValue("$project", batch.ProjectId);
                cmd.Parameters.AddWithValue("$kind", batch.Kind.ToString());
                cmd.Parameters.AddWithValue("$summary", batch.Summary);
                cmd.Parameters.AddWithValue("$at", batch.CreatedAt);
                cmd.ExecuteNonQuery();
            }

            for (var i = 0; i < batch.Items.Count; i++)
            {
                var item = batch.Items[i];
                item.Seq = i;
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO FileOpItem (BatchId, Seq, Step, AssetId, OldPath, NewPath, OldSourceId, NewSourceId)
                    VALUES ($batch, $seq, $step, $asset, $old, $new, $oldSource, $newSource)
                    """;
                cmd.Parameters.AddWithValue("$batch", batch.Id);
                cmd.Parameters.AddWithValue("$seq", i);
                cmd.Parameters.AddWithValue("$step", item.Step.ToString());
                cmd.Parameters.AddWithValue("$asset", (object?)item.AssetId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$old", item.OldPath);
                cmd.Parameters.AddWithValue("$new", item.NewPath);
                cmd.Parameters.AddWithValue("$oldSource", (object?)item.OldSourceId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$newSource", (object?)item.NewSourceId ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return batch.Id;
        });

    /// <summary>Newest not-yet-undone file operation for a project, with its steps in order.</summary>
    public Task<FileOpBatch?> GetLastUndoableFileOpAsync(string projectId) =>
        _db.ReadAsync(conn =>
        {
            FileOpBatch? batch = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT Id, ProjectId, Kind, Summary, CreatedAt, IsUndone FROM FileOpBatch
                    WHERE ProjectId = $project AND IsUndone = 0
                    ORDER BY CreatedAt DESC, rowid DESC
                    LIMIT 1
                    """;
                cmd.Parameters.AddWithValue("$project", projectId);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    return null;
                }

                batch = new FileOpBatch
                {
                    Id = reader.GetString(0),
                    ProjectId = reader.GetString(1),
                    Kind = Enum.TryParse<FileOpKind>(reader.GetString(2), out var kind) ? kind : FileOpKind.Move,
                    Summary = reader.GetString(3),
                    CreatedAt = reader.GetString(4),
                    IsUndone = reader.GetInt32(5) != 0
                };
            }

            batch.Items = ReadFileOpItems(conn, batch.Id);
            return batch;
        });

    public Task MarkFileOpUndoneAsync(string batchId) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE FileOpBatch SET IsUndone = 1 WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", batchId);
            cmd.ExecuteNonQuery();
        });

    private static List<FileOpItem> ReadFileOpItems(SqliteConnection conn, string batchId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Seq, Step, AssetId, OldPath, NewPath, OldSourceId, NewSourceId
            FROM FileOpItem WHERE BatchId = $id ORDER BY Seq
            """;
        cmd.Parameters.AddWithValue("$id", batchId);
        var list = new List<FileOpItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!Enum.TryParse<FileOpStep>(reader.GetString(1), out var step))
            {
                continue;
            }

            list.Add(new FileOpItem
            {
                Seq = reader.GetInt32(0),
                Step = step,
                AssetId = reader.IsDBNull(2) ? null : reader.GetString(2),
                OldPath = reader.GetString(3),
                NewPath = reader.GetString(4),
                OldSourceId = reader.IsDBNull(5) ? null : reader.GetString(5),
                NewSourceId = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return list;
    }
}
