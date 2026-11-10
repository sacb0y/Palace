using System.Text.Json;
using Microsoft.Data.Sqlite;
using Palace.Models;

namespace Palace.Services;

/// <summary>
/// Snapshot / restore for undoable Delete rejected (Recycle). Mirrors
/// <c>CatalogService.TagUndo</c> JSON style so the FileOp journal can hold the row.
/// </summary>
public sealed partial class CatalogService
{
    private static readonly JsonSerializerOptions AssetSnapshotJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public Task<AssetDeleteSnapshot?> SnapshotAssetAsync(string assetId) =>
        _db.ReadAsync(conn => ReadAssetSnapshot(conn, assetId));

    public Task RestoreAssetSnapshotAsync(AssetDeleteSnapshot snapshot) =>
        _db.WriteAsync(conn => RestoreAssetSnapshot(conn, snapshot));

    public static string SerializeAssetSnapshot(AssetDeleteSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, AssetSnapshotJson);

    public static AssetDeleteSnapshot? DeserializeAssetSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AssetDeleteSnapshot>(json, AssetSnapshotJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Snapshot the catalog before a large Delete rejected run (same backup API as
    /// tag import). Returns the backup folder, or null when the threshold is not met.
    /// </summary>
    public Task<string?> BackupBeforeDeleteRejectedAsync(int actionableCount) =>
        _db.WriteAsync(conn =>
        {
            _ = conn;
            return Helpers.DeleteRejected.ShouldBackup(actionableCount)
                ? _db.BackupBeforeDeleteRejected()
                : null;
        });

    private static AssetDeleteSnapshot? ReadAssetSnapshot(SqliteConnection conn, string assetId)
    {
        var asset = GetAssetById(conn, assetId);
        if (asset is null)
        {
            return null;
        }

        var snapshot = new AssetDeleteSnapshot { Asset = asset };
        foreach (var (tagId, source) in LoadAssignedRefs(conn, assetId))
        {
            snapshot.Assignments.Add(new AssetTag
            {
                AssetId = assetId,
                TagId = tagId,
                Source = source
            });
        }

        return snapshot;
    }

    private void RestoreAssetSnapshot(SqliteConnection conn, AssetDeleteSnapshot snapshot)
    {
        if (snapshot.Asset is null || string.IsNullOrWhiteSpace(snapshot.Asset.Id))
        {
            return;
        }

        // Prefer Id conflict restore: deleted rows are gone, so INSERT keeps Id.
        UpsertAsset(conn, snapshot.Asset, tagsText: "");

        foreach (var assignment in snapshot.Assignments)
        {
            if (string.IsNullOrWhiteSpace(assignment.TagId))
            {
                continue;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO AssetTag (AssetId, TagId, Source)
                VALUES ($asset, $tag, $source)
                ON CONFLICT(AssetId, TagId) DO UPDATE SET Source = excluded.Source
                """;
            cmd.Parameters.AddWithValue("$asset", snapshot.Asset.Id);
            cmd.Parameters.AddWithValue("$tag", assignment.TagId);
            cmd.Parameters.AddWithValue("$source", assignment.Source.ToString());
            cmd.ExecuteNonQuery();
        }

        RefreshFts(conn, snapshot.Asset.Id);
    }
}
