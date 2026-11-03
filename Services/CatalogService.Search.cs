using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>Catalog: FTS5 search index and queries.</summary>
public sealed partial class CatalogService
{
    public Task<IReadOnlyList<Asset>> SearchAsync(
        string query,
        string? projectId = null,
        MosaicSort sort = MosaicSort.Name) =>
        _db.ReadAsync(conn =>
        {
            var match = ToFtsQuery(query);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SelectAssetSql + $"""
                 WHERE RowId IN (SELECT rowid FROM AssetFts WHERE AssetFts MATCH $q)
                   AND ($project IS NULL OR SourceFolderId IN (SELECT Id FROM SourceFolder WHERE ProjectId = $project))
                 {MosaicSortOrder.OrderBySql(sort)}
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

    public void RefreshFts(SqliteConnection conn, string assetId, SqliteTransaction? tx = null)
    {
        try
        {
            var asset = GetAssetById(conn, assetId, tx);
            if (asset is null)
            {
                return;
            }

            var tags = string.Join(" ", AssignedTagNamesForFts(conn, assetId, tx));
            DeleteFtsRow(conn, asset.RowId, tx);

            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
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
        // contentless_delete=1 allows a real DELETE (the FTS5 'delete' command is a no-op
        // without that option, which left stale tag tokens after implication sweeps).
        cmd.CommandText = "DELETE FROM AssetFts WHERE rowid = $row";
        cmd.Parameters.AddWithValue("$row", rowId);
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Row was never indexed, or an older contentless table rejected DELETE.
        }
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

    private static IEnumerable<string> AssignedTagNamesForFts(
        SqliteConnection conn,
        string assetId,
        SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
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
