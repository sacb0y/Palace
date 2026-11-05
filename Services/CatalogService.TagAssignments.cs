using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>Catalog: tags assigned to assets, tag filters. Implied tags are computed, not stored (T5).</summary>
public sealed partial class CatalogService
{
    /// <summary>No-op after T5 (implied tags are not materialized).</summary>
    public Task<int> BackfillImplicationAddedAsync(string sourceTagId, CancellationToken cancellationToken = default)
    {
        _ = sourceTagId;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(0);
    }

    /// <summary>No-op after T5 (implied tags are not materialized).</summary>
    public Task<int> BackfillImplicationRemovedAsync(string impliedTagId, CancellationToken cancellationToken = default)
    {
        _ = impliedTagId;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(0);
    }

    public Task<int> CountAssetsForTagsAsync(IEnumerable<string> tagIds, string? projectId = null) =>
        _db.ReadAsync(conn =>
        {
            var ids = ExpandTagsForParentFilter(conn, tagIds);
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
            // Parent filter finds children: include tags that imply each requested ideal.
            var ids = ExpandTagsForParentFilter(conn, tagIds);
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
            // Rewrite alias → ideal. Do not materialize implied (T5 — compute at display/search).
            if (source == TagSource.Implied)
            {
                return;
            }

            var idealId = ResolveIdealTagId(conn, tagId);
            UpsertAssetTag(conn, assetId, idealId, source);
            RefreshFts(conn, assetId);
        });

    public Task RemoveTagAsync(string assetId, string tagId) =>
        _db.WriteAsync(conn =>
        {
            DeleteAssetTag(conn, assetId, ResolveIdealTagId(conn, tagId));
            RefreshFts(conn, assetId);
        });

    public Task<IReadOnlyList<AssignedTag>> GetAssignedTagsAsync(string assetId) =>
        _db.ReadAsync(conn => GetAssignedTags(conn, assetId));

    /// <summary>Stored applied tags only (no computed Implied) — for file-op journaling.</summary>
    public Task<IReadOnlyList<AssignedTag>> GetStoredAssignedTagsAsync(string assetId) =>
        _db.ReadAsync(conn => GetAssignedTags(conn, assetId, includeComputedImplied: false));

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

    internal static IReadOnlyList<AssignedTag> GetAssignedTags(
        SqliteConnection conn,
        string assetId,
        bool includeComputedImplied = true)
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
        var byId = tags.ToDictionary(t => t.Id, StringComparer.Ordinal);

        if (includeComputedImplied)
        {
            var have = list.Select(t => t.TagId).ToHashSet(StringComparer.Ordinal);
            var impliedIds = new HashSet<string>(StringComparer.Ordinal);
            var applied = list.Where(t => t.Source != TagSource.Implied).ToList();
            foreach (var row in applied)
            {
                foreach (var impliedId in CollectImpliedIdeals(conn, row.TagId))
                {
                    if (have.Contains(impliedId) || !impliedIds.Add(impliedId))
                    {
                        continue;
                    }

                    if (!byId.TryGetValue(impliedId, out var tag))
                    {
                        continue;
                    }

                    list.Add(new AssignedTag
                    {
                        TagId = tag.Id,
                        TagName = tag.Name,
                        TagPriority = tag.Priority,
                        Source = TagSource.Implied,
                        Slug = tag.Slug
                    });
                }
            }
        }

        var memberships = LoadMemberships(conn);
        var colors = MapEffectiveColors(tags, memberships);
        foreach (var tag in list)
        {
            tag.ParentNames = memberships
                .Where(m => m.ChildId == tag.TagId && byId.ContainsKey(m.ParentId))
                .Select(m => byId[m.ParentId].Name)
                .ToList();
            tag.EffectiveColor = colors.GetValueOrDefault(tag.TagId);
        }

        return list
            .OrderByDescending(t => t.TagPriority)
            .ThenBy(t => t.TagName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Resolve aliases, then add every tag that transitively implies each ideal
    /// so a parent filter finds children (applied on those children).
    /// </summary>
    private static List<string> ExpandTagsForParentFilter(SqliteConnection conn, IEnumerable<string> tagIds)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in tagIds.Where(id => !string.IsNullOrEmpty(id)))
        {
            var ideal = ResolveIdealTagId(conn, raw);
            if (!result.Add(ideal))
            {
                continue;
            }

            foreach (var child in TagsThatImply(conn, ideal))
            {
                result.Add(ResolveIdealTagId(conn, child));
            }
        }

        return result.ToList();
    }

    private static void UpsertAssetTag(
        SqliteConnection conn,
        string assetId,
        string tagId,
        TagSource source,
        SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
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

    private static void DeleteAssetTag(
        SqliteConnection conn,
        string assetId,
        string tagId,
        SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM AssetTag WHERE AssetId = $asset AND TagId = $tag";
        cmd.Parameters.AddWithValue("$asset", assetId);
        cmd.Parameters.AddWithValue("$tag", tagId);
        cmd.ExecuteNonQuery();
    }

    private static List<string> TagsThatImply(
        SqliteConnection conn,
        string targetId,
        SqliteTransaction? tx = null)
    {
        var sources = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in LoadImplications(conn, tx))
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

    private static List<string> AssetsHavingAnyTag(
        SqliteConnection conn,
        IReadOnlyList<string> tagIds,
        SqliteTransaction? tx = null)
    {
        var ids = tagIds.Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
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

    private static List<(string TagId, TagSource Source)> LoadAssignedRefs(
        SqliteConnection conn,
        string assetId,
        SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
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

    /// <summary>
    /// Implication closure from <paramref name="tagId"/>, resolving every hop through
    /// aliases and continuing from each ideal (aliases before implications).
    /// </summary>
    private static List<string> CollectImpliedIdeals(
        SqliteConnection conn,
        string tagId,
        SqliteTransaction? tx = null)
    {
        var map = LoadAliasMap(conn, tx);
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(tagId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var raw in TransitiveImplied(conn, current, tx))
            {
                var ideal = TagAliasResolve.Ideal(raw, map);
                if (!seen.Add(ideal))
                {
                    continue;
                }

                found.Add(ideal);
                if (!string.Equals(ideal, raw, StringComparison.Ordinal))
                {
                    stack.Push(ideal);
                }
            }
        }

        return found;
    }
}
