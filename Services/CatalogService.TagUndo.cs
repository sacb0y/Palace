using System.Text.Json;
using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>
/// Snapshot / restore for undoable tag delete (shared FileOp journal).
/// Self-contained SQL so Palace.Tests can compile this without CatalogService.Tags (TagColor).
/// </summary>
public sealed partial class CatalogService
{
    private static readonly JsonSerializerOptions TagSnapshotJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public Task<TagDeleteSnapshot?> SnapshotTagAsync(string tagId) =>
        _db.ReadAsync(conn => ReadTagSnapshot(conn, tagId));

    public Task RestoreTagSnapshotAsync(TagDeleteSnapshot snapshot) =>
        _db.WriteAsync(conn =>
        {
            RestoreTagSnapshot(conn, snapshot);
            InvalidateGraphCaches();
        });

    /// <summary>Undo helper: assign without implication fan-out (journal reverse of a remove).</summary>
    public Task AssignTagForUndoAsync(string assetId, string tagId, TagSource source) =>
        _db.WriteAsync(conn =>
        {
            // B1: never re-materialize Implied from old journals; never downgrade applied.
            if (source == TagSource.Implied)
            {
                return;
            }

            var idealId = ResolveIdealTagId(conn, tagId);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO AssetTag (AssetId, TagId, Source)
                VALUES ($asset, $tag, $source)
                ON CONFLICT(AssetId, TagId) DO UPDATE SET
                    Source = CASE
                        WHEN AssetTag.Source <> 'Implied' THEN AssetTag.Source
                        ELSE excluded.Source
                    END
                """;
            cmd.Parameters.AddWithValue("$asset", assetId);
            cmd.Parameters.AddWithValue("$tag", idealId);
            cmd.Parameters.AddWithValue("$source", source.ToString());
            cmd.ExecuteNonQuery();
            RefreshFts(conn, assetId);
        });

    /// <summary>
    /// KeepAssigned AssignTag Undo in <b>one</b> write: UPDATE the assigned row's
    /// Source back to the prior applied value (never rematerialize Implied), then
    /// strip Implied extras that assignment added. No delete+reinsert across
    /// separate <c>WriteAsync</c> calls.
    /// </summary>
    public Task UndoKeepAssignedAssignAsync(
        string assetId,
        string assignedTagId,
        TagSource? priorAppliedSource,
        IReadOnlyList<string>? impliedAdded = null) =>
        _db.WriteAsync(conn =>
        {
            var idealId = ResolveIdealTagId(conn, assignedTagId);
            if (priorAppliedSource is TagSource prior && prior != TagSource.Implied)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    UPDATE AssetTag SET Source = $source
                    WHERE AssetId = $asset AND TagId = $tag
                    """;
                cmd.Parameters.AddWithValue("$asset", assetId);
                cmd.Parameters.AddWithValue("$tag", idealId);
                cmd.Parameters.AddWithValue("$source", prior.ToString());
                cmd.ExecuteNonQuery();
            }

            if (impliedAdded is not null)
            {
                foreach (var extra in impliedAdded)
                {
                    if (string.IsNullOrEmpty(extra) || string.Equals(extra, idealId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var idealExtra = ResolveIdealTagId(conn, extra);
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                        DELETE FROM AssetTag
                        WHERE AssetId = $asset AND TagId = $tag AND Source = $source
                        """;
                    cmd.Parameters.AddWithValue("$asset", assetId);
                    cmd.Parameters.AddWithValue("$tag", idealExtra);
                    cmd.Parameters.AddWithValue("$source", TagSource.Implied.ToString());
                    cmd.ExecuteNonQuery();
                }
            }

            RefreshFts(conn, assetId);
        });

    /// <summary>
    /// Undo helper: remove the assigned tag and any implied tags that assignment added.
    /// Implied extras are deleted only while they are still <see cref="TagSource.Implied"/>.
    /// </summary>
    public Task RemoveTagForUndoAsync(string assetId, string? tagId, IReadOnlyList<string>? impliedAdded = null) =>
        _db.WriteAsync(conn =>
        {
            if (!string.IsNullOrEmpty(tagId))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM AssetTag WHERE AssetId = $asset AND TagId = $tag";
                cmd.Parameters.AddWithValue("$asset", assetId);
                cmd.Parameters.AddWithValue("$tag", tagId);
                cmd.ExecuteNonQuery();
            }

            if (impliedAdded is not null)
            {
                foreach (var extra in impliedAdded)
                {
                    if (string.IsNullOrEmpty(extra) || string.Equals(extra, tagId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = """
                        DELETE FROM AssetTag
                        WHERE AssetId = $asset AND TagId = $tag AND Source = $source
                        """;
                    cmd.Parameters.AddWithValue("$asset", assetId);
                    cmd.Parameters.AddWithValue("$tag", extra);
                    cmd.Parameters.AddWithValue("$source", TagSource.Implied.ToString());
                    cmd.ExecuteNonQuery();
                }
            }

            RefreshFts(conn, assetId);
        });

    public static string SerializeTagSnapshot(TagDeleteSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, TagSnapshotJson);

    public static TagDeleteSnapshot? DeserializeTagSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TagDeleteSnapshot>(json, TagSnapshotJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string SerializeAliasMigration(IReadOnlyList<AssetTag> migrated) =>
        JsonSerializer.Serialize(migrated, TagSnapshotJson);

    public static IReadOnlyList<AssetTag> DeserializeAliasMigration(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AssetTag>>(json, TagSnapshotJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public Task<int> CountTagGroupLinksAsync(string tagId) =>
        _db.ReadAsync(conn => CountTagGroupLinks(conn, tagId));

    /// <summary>
    /// CreateTag Undo: unused-check + DELETE in <b>one</b> write so an in-use tag cannot
    /// be deleted. Tag CASCADE clears membership / implication / alias / AssetTag.
    /// Returns false when AssetTag, membership, implication, or alias still references it.
    /// </summary>
    public Task<bool> TryDeleteUnusedCreatedTagAsync(string tagId) =>
        _db.WriteAsync(conn =>
        {
            if (string.IsNullOrEmpty(tagId))
            {
                return false;
            }

            if (CountAssetTags(conn, tagId) > 0 || CountTagGroupLinks(conn, tagId) > 0)
            {
                return false;
            }

            var probe = TakeTagCreateUndoFault();
            if (probe is { ThrowOnDelete: true })
            {
                throw new InvalidOperationException("Tag create undo fault probe: ThrowOnDelete");
            }

            InvalidateGraphCaches();
            var assetIds = AssetsHavingAnyTag(conn, [tagId]);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Tag WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", tagId);
            cmd.ExecuteNonQuery();
            foreach (var assetId in assetIds)
            {
                RefreshFts(conn, assetId);
            }

            return true;
        });

    private static int CountAssetTags(SqliteConnection conn, string tagId, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null)
        {
            cmd.Transaction = tx;
        }

        cmd.CommandText = "SELECT COUNT(*) FROM AssetTag WHERE TagId = $id";
        cmd.Parameters.AddWithValue("$id", tagId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static int CountTagGroupLinks(SqliteConnection conn, string tagId, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null)
        {
            cmd.Transaction = tx;
        }

        cmd.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM TagMembership WHERE ChildId = $id OR ParentId = $id)
              + (SELECT COUNT(*) FROM TagImplication WHERE TagId = $id OR ImpliedTagId = $id)
              + (SELECT COUNT(*) FROM TagAlias WHERE AliasTagId = $id OR IdealTagId = $id)
            """;
        cmd.Parameters.AddWithValue("$id", tagId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static TagDeleteSnapshot? ReadTagSnapshot(
        SqliteConnection conn,
        string tagId,
        SqliteTransaction? tx = null)
    {
        Tag? tag = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT Id, Name, Priority, Slug, Color, IsStarred FROM Tag WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", tagId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            tag = new Tag
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Priority = reader.GetInt32(2),
                Slug = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Color = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsStarred = reader.GetInt32(5) != 0
            };
        }

        var snapshot = new TagDeleteSnapshot { Tag = tag };
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT AssetId, TagId, Source FROM AssetTag WHERE TagId = $id";
            cmd.Parameters.AddWithValue("$id", tagId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                snapshot.Assignments.Add(new AssetTag
                {
                    AssetId = reader.GetString(0),
                    TagId = reader.GetString(1),
                    Source = Enum.TryParse<TagSource>(reader.GetString(2), out var source) ? source : TagSource.Manual
                });
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT ParentId, ChildId FROM TagMembership WHERE ParentId = $id OR ChildId = $id";
            cmd.Parameters.AddWithValue("$id", tagId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                snapshot.Memberships.Add(new TagMembership
                {
                    ParentId = reader.GetString(0),
                    ChildId = reader.GetString(1)
                });
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT TagId, ImpliedTagId FROM TagImplication WHERE TagId = $id OR ImpliedTagId = $id";
            cmd.Parameters.AddWithValue("$id", tagId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                snapshot.Implications.Add(new TagImplication
                {
                    TagId = reader.GetString(0),
                    ImpliedTagId = reader.GetString(1)
                });
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT AliasTagId, IdealTagId FROM TagAlias WHERE AliasTagId = $id OR IdealTagId = $id";
            cmd.Parameters.AddWithValue("$id", tagId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                snapshot.Aliases.Add(new TagAlias
                {
                    AliasTagId = reader.GetString(0),
                    IdealTagId = reader.GetString(1)
                });
            }
        }

        return snapshot;
    }

    private void RestoreTagSnapshot(SqliteConnection conn, TagDeleteSnapshot snapshot)
    {
        var tag = snapshot.Tag;
        if (string.IsNullOrEmpty(tag.Id) || string.IsNullOrWhiteSpace(tag.Name))
        {
            return;
        }

        if (TagNames.FindId(conn, tag.Name, excludeId: tag.Id) is not null)
        {
            throw new DuplicateTagNameException(tag.Name);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT OR REPLACE INTO Tag (Id, Name, Priority, Slug, Color, IsStarred)
                VALUES ($id, $name, $priority, $slug, $color, $starred)
                """;
            cmd.Parameters.AddWithValue("$id", tag.Id);
            cmd.Parameters.AddWithValue("$name", TagNames.Normalize(tag.Name));
            cmd.Parameters.AddWithValue("$priority", tag.Priority);
            cmd.Parameters.AddWithValue("$slug", string.IsNullOrEmpty(tag.Slug) ? PathSafe.Slug(tag.Name) : tag.Slug);
            cmd.Parameters.AddWithValue("$color", (object?)tag.Color ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$starred", tag.IsStarred ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        foreach (var edge in snapshot.Memberships)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO TagMembership (ParentId, ChildId) VALUES ($p, $c)";
            cmd.Parameters.AddWithValue("$p", edge.ParentId);
            cmd.Parameters.AddWithValue("$c", edge.ChildId);
            cmd.ExecuteNonQuery();
        }

        foreach (var edge in snapshot.Implications)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO TagImplication (TagId, ImpliedTagId) VALUES ($t, $i)";
            cmd.Parameters.AddWithValue("$t", edge.TagId);
            cmd.Parameters.AddWithValue("$i", edge.ImpliedTagId);
            cmd.ExecuteNonQuery();
        }

        foreach (var edge in snapshot.Aliases)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO TagAlias (AliasTagId, IdealTagId) VALUES ($a, $i)";
            cmd.Parameters.AddWithValue("$a", edge.AliasTagId);
            cmd.Parameters.AddWithValue("$i", edge.IdealTagId);
            cmd.ExecuteNonQuery();
        }

        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in snapshot.Assignments)
        {
            // Snapshot restore rewrites alias ids to ideal (same as assign).
            var tagId = ResolveIdealTagId(conn, row.TagId);
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
            cmd.Parameters.AddWithValue("$asset", row.AssetId);
            cmd.Parameters.AddWithValue("$tag", tagId);
            cmd.Parameters.AddWithValue("$source", row.Source.ToString());
            cmd.ExecuteNonQuery();
            touched.Add(row.AssetId);
        }

        foreach (var assetId in touched)
        {
            RefreshFts(conn, assetId);
        }
    }
}
