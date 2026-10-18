using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>Catalog: tags assigned to assets, tag filters and implied-tag backfill.</summary>
public sealed partial class CatalogService
{
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

    public Task<int> CountAssetsForTagsAsync(IEnumerable<string> tagIds, string? projectId = null) =>
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

            cmd.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
            cmd.CommandText = $"""
                SELECT COUNT(*) FROM Asset
                 WHERE Id IN (SELECT DISTINCT AssetId FROM AssetTag WHERE TagId IN ({string.Join(",", names)}))
                   AND ($project IS NULL OR SourceFolderId IN (SELECT Id FROM SourceFolder WHERE ProjectId = $project))
                """;
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

    public Task<IReadOnlyList<Asset>> GetAssetsForTagsAsync(
        IEnumerable<string> tagIds,
        string? projectId = null,
        MosaicSort sort = MosaicSort.Name) =>
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
                 {MosaicSortOrder.OrderBySql(sort)}
                """;
            return ReadAssets(cmd);
        });

    public Task<IReadOnlyList<Asset>> GetAssetsForTagFilterAsync(
        IReadOnlyList<IReadOnlyCollection<string>> expandedIdSets,
        TagFilterMode mode,
        string? projectId = null,
        MosaicSort sort = MosaicSort.Name) =>
        _db.ReadAsync<IReadOnlyList<Asset>>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
            var projectClause =
                "($project IS NULL OR SourceFolderId IN (SELECT Id FROM SourceFolder WHERE ProjectId = $project))";
            var order = MosaicSortOrder.OrderBySql(sort);

            if (expandedIdSets.Count == 0 || expandedIdSets.All(set => set.Count == 0))
            {
                cmd.CommandText = SelectAssetSql + $"""
                     WHERE {projectClause}
                     {order}
                    """;
                return ReadAssets(cmd);
            }

            var param = 0;
            string InClause(IReadOnlyCollection<string> ids)
            {
                var names = new List<string>();
                foreach (var id in ids.Distinct(StringComparer.Ordinal))
                {
                    var p = $"$tf{param++}";
                    names.Add(p);
                    cmd.Parameters.AddWithValue(p, id);
                }

                return names.Count == 0 ? "NULL" : string.Join(",", names);
            }

            string Subquery(IReadOnlyCollection<string> ids) =>
                $"SELECT DISTINCT AssetId FROM AssetTag WHERE TagId IN ({InClause(ids)})";

            if (mode == TagFilterMode.All)
            {
                var parts = expandedIdSets
                    .Where(set => set.Count > 0)
                    .Select(Subquery)
                    .ToList();
                if (parts.Count == 0)
                {
                    cmd.CommandText = SelectAssetSql + $"""
                         WHERE {projectClause}
                         {order}
                        """;
                    return ReadAssets(cmd);
                }

                cmd.CommandText = SelectAssetSql + $"""
                     WHERE Id IN ({string.Join(" INTERSECT ", parts)})
                       AND {projectClause}
                     {order}
                    """;
                return ReadAssets(cmd);
            }

            var union = new HashSet<string>(StringComparer.Ordinal);
            foreach (var set in expandedIdSets)
            {
                foreach (var id in set)
                {
                    union.Add(id);
                }
            }

            var unionSql = Subquery(union);
            var op = mode == TagFilterMode.None ? "NOT IN" : "IN";
            cmd.CommandText = SelectAssetSql + $"""
                 WHERE Id {op} ({unionSql})
                   AND {projectClause}
                 {order}
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

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAssignedTagIdsByAssetAsync(
        IEnumerable<string> assetIds) =>
        _db.ReadAsync(conn =>
        {
            var ids = assetIds
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (ids.Count == 0)
            {
                return (IReadOnlyDictionary<string, IReadOnlyList<string>>)
                    new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            }

            const int batchSize = 400;
            for (var offset = 0; offset < ids.Count; offset += batchSize)
            {
                var take = Math.Min(batchSize, ids.Count - offset);
                using var cmd = conn.CreateCommand();
                var names = new List<string>(take);
                for (var i = 0; i < take; i++)
                {
                    var id = ids[offset + i];
                    var p = $"$a{i}";
                    names.Add(p);
                    cmd.Parameters.AddWithValue(p, id);
                    map[id] = [];
                }

                cmd.CommandText = $"SELECT AssetId, TagId FROM AssetTag WHERE AssetId IN ({string.Join(",", names)})";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var assetId = reader.GetString(0);
                    if (map.TryGetValue(assetId, out var list))
                    {
                        list.Add(reader.GetString(1));
                    }
                }
            }

            return map.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value,
                StringComparer.Ordinal);
        });

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
}
