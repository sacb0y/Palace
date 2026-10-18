using Microsoft.Data.Sqlite;
using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>Catalog: the global tag graph (tags, groups / memberships, implications, colors, import / export).</summary>
public sealed partial class CatalogService
{
    public Task<IReadOnlyList<Tag>> GetTagsAsync() =>
        _db.ReadAsync(conn => (IReadOnlyList<Tag>)LoadTags(conn));

    public Task<Tag?> GetTagAsync(string id) =>
        _db.ReadAsync(conn => LoadTags(conn).FirstOrDefault(t => t.Id == id));

    public Task<IReadOnlyList<TagMembership>> GetMembershipsAsync() =>
        _db.ReadAsync(conn => (IReadOnlyList<TagMembership>)LoadMemberships(conn));

    public Task<IReadOnlyList<TagImplication>> GetImplicationsAsync() =>
        _db.ReadAsync(conn => (IReadOnlyList<TagImplication>)LoadImplications(conn));

    /// <summary>Export the global tag catalog to portable JSON (Sacb0y).</summary>
    public Task<string> ExportTagsJsonAsync() =>
        _db.ReadAsync(conn =>
        {
            var tags = LoadTags(conn)
                .Select(t => new TagPortableTagSource
                {
                    Id = t.Id,
                    Name = t.Name,
                    Priority = t.Priority,
                    Color = t.Color,
                    IsStarred = t.IsStarred
                })
                .ToList();
            var memberships = LoadMemberships(conn)
                .Select(m => new TagPortableEdge { FromId = m.ParentId, ToId = m.ChildId })
                .ToList();
            var implications = LoadImplications(conn)
                .Select(i => new TagPortableEdge { FromId = i.TagId, ToId = i.ImpliedTagId })
                .ToList();
            return TagPortable.Serialize(TagPortable.FromCatalog(tags, memberships, implications));
        });

    /// <summary>
    /// Merge a portable tag JSON into the global catalog. Upsert by name;
    /// does not delete unrelated tags. Cycle-safe for parents and implicits.
    /// </summary>
    public Task<TagMergePlan> ImportTagsJsonAsync(string json) =>
        _db.WriteAsync(conn =>
        {
            var document = TagPortable.Parse(json);
            var existing = LoadTags(conn)
                .Select(t => new TagPortableTagSource
                {
                    Id = t.Id,
                    Name = t.Name,
                    Priority = t.Priority,
                    Color = t.Color,
                    IsStarred = t.IsStarred
                })
                .ToList();
            var memberships = LoadMemberships(conn)
                .Select(m => new TagPortableEdge { FromId = m.ParentId, ToId = m.ChildId })
                .ToList();
            var implications = LoadImplications(conn)
                .Select(i => new TagPortableEdge { FromId = i.TagId, ToId = i.ImpliedTagId })
                .ToList();
            var plan = TagPortable.PlanMerge(existing, memberships, implications, document);

            // Apply creates first so name→id resolves to real SQLite ids.
            // Names are unique ignoring case and padding (TagNames); keys are trimmed so a
            // stored " cat " still matches the trimmed edge names PlanMerge emits.
            var idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in existing)
            {
                idByName.TryAdd(TagNames.Normalize(t.Name), t.Id);
            }
            foreach (var create in plan.Creates)
            {
                // e.g. " cat " in the file vs "cat" here: reuse instead of creating a duplicate.
                if (TagNames.FindId(conn, create.Name) is { } existingId)
                {
                    idByName[TagNames.Normalize(create.Name)] = existingId;
                    continue;
                }

                var tag = InsertTag(conn, create.Name, create.Priority);
                ApplyTagFields(conn, tag.Id, create.Priority, create.Color, create.Starred);
                idByName[TagNames.Normalize(create.Name)] = tag.Id;
            }

            foreach (var update in plan.Updates)
            {
                ApplyTagFields(conn, update.Id, update.Priority, update.Color, update.Starred);
            }

            foreach (var edge in plan.Memberships)
            {
                if (!idByName.TryGetValue(TagNames.Normalize(edge.ParentName), out var parentId)
                    || !idByName.TryGetValue(TagNames.Normalize(edge.ChildName), out var childId))
                {
                    continue;
                }

                TryAddMembership(conn, parentId, childId);
            }

            foreach (var edge in plan.Implications)
            {
                // ParentName = tag, ChildName = implied (see TagPortable).
                if (!idByName.TryGetValue(TagNames.Normalize(edge.ParentName), out var tagId)
                    || !idByName.TryGetValue(TagNames.Normalize(edge.ChildName), out var impliedId))
                {
                    continue;
                }

                TryAddImplication(conn, tagId, impliedId);
            }

            return plan;
        });

    private static void ApplyTagFields(
        SqliteConnection conn,
        string id,
        int priority,
        string? color,
        bool starred)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Tag SET Priority = $p, Color = $c, IsStarred = $s WHERE Id = $id
            """;
        cmd.Parameters.AddWithValue("$p", priority);
        cmd.Parameters.AddWithValue("$c", (object?)TagColor.Normalize(color) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", starred ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

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
        _db.ReadAsync(conn => FindTagByName(conn, name));

    /// <summary>Creates a tag; throws <see cref="DuplicateTagNameException"/> if the name is taken (any case).</summary>
    public Task<Tag> CreateTagAsync(string name, int priority = 0) =>
        _db.WriteAsync(conn => InsertTag(conn, name, priority));

    /// <summary>Returns the existing tag with this name (any case), or creates it, in one write.</summary>
    public Task<Tag> GetOrCreateTagAsync(string name) =>
        _db.WriteAsync(conn => FindTagByName(conn, name) ?? InsertTag(conn, name, 0));

    public Task<Tag> CreateChildTagAsync(string parentId, string name, int priority = 0) =>
        _db.WriteAsync(conn =>
        {
            var tag = InsertTag(conn, name, priority);
            TryAddMembership(conn, parentId, tag.Id);
            return tag;
        });

    public Task<TagCreateBatchResult> CreateTagsAsync(IEnumerable<string> names, string? parentId = null) =>
        _db.WriteAsync(conn =>
        {
            var created = 0;
            var existed = 0;
            var rejected = 0;
            var tags = new List<Tag>();
            foreach (var raw in TagNameList.Split(string.Join('\n', names)))
            {
                var existing = FindTagByName(conn, raw);
                Tag tag;
                if (existing is null)
                {
                    tag = InsertTag(conn, raw, 0);
                    created++;
                }
                else
                {
                    tag = existing;
                    existed++;
                }

                if (parentId is not null && !TryAddMembership(conn, parentId, tag.Id))
                {
                    rejected++;
                }

                tags.Add(tag);
            }

            return new TagCreateBatchResult
            {
                Created = created,
                Existed = existed,
                MembershipRejected = rejected,
                Tags = tags
            };
        });

    public Task SetTagStarredAsync(string id, bool starred) =>
        _db.WriteAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Tag SET IsStarred = $s WHERE Id = $id";
            cmd.Parameters.AddWithValue("$s", starred ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        });

    public Task DeleteTagAsync(string id) =>
        _db.WriteAsync(conn =>
        {
            var assetIds = AssetsHavingAnyTag(conn, [id]);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Tag WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            foreach (var assetId in assetIds)
            {
                SweepUnjustifiedImplied(conn, assetId);
                RefreshFts(conn, assetId);
            }
        });

    public Task<bool> MoveTagAmongSiblingsAsync(string tagId, string? parentId, int delta) =>
        _db.WriteAsync(conn =>
        {
            var tags = LoadTags(conn);
            var memberships = LoadMemberships(conn);
            var siblings = TagSiblings.Of(tags, memberships, parentId)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var from = siblings.FindIndex(t => t.Id == tagId);
            var to = from + delta;
            if (from < 0 || to < 0 || to >= siblings.Count)
            {
                return false;
            }

            (siblings[from], siblings[to]) = (siblings[to], siblings[from]);
            for (var i = 0; i < siblings.Count; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Tag SET Priority = $p WHERE Id = $id";
                cmd.Parameters.AddWithValue("$p", siblings.Count - i);
                cmd.Parameters.AddWithValue("$id", siblings[i].Id);
                cmd.ExecuteNonQuery();
            }

            return true;
        });

    /// <summary>
    /// Renames a tag. Returns false (and changes nothing) when another tag already has the name,
    /// ignoring case; changing only the case of a tag's own name is allowed.
    /// </summary>
    public Task<bool> RenameTagAsync(string id, string name) =>
        _db.WriteAsync(conn =>
        {
            if (TagNames.FindId(conn, name, excludeId: id) is not null)
            {
                return false;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Tag SET Name = $name, Slug = $slug WHERE Id = $id";
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$slug", PathSafe.Slug(name));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            RefreshFtsForTag(conn, id);
            return true;
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
                Color = reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetString(4) : null,
                IsStarred = reader.FieldCount > 5 && !reader.IsDBNull(5) && reader.GetInt32(5) != 0
            });
        }

        return list;
    }

    private static Tag? FindTagByName(SqliteConnection conn, string name)
    {
        var id = TagNames.FindId(conn, name);
        if (id is null)
        {
            return null;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Priority, Slug, Color, IsStarred FROM Tag WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        var list = ReadTags(cmd);
        return list.Count > 0 ? list[0] : null;
    }

    private static List<Tag> LoadTags(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Priority, Slug, Color, IsStarred FROM Tag ORDER BY Priority DESC, Name COLLATE NOCASE";
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

    private static Tag InsertTag(SqliteConnection conn, string name, int priority)
    {
        if (TagNames.FindId(conn, name) is not null)
        {
            throw new DuplicateTagNameException(name);
        }

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
}
