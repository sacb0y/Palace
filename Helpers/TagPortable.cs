using System.Text.Json;
using System.Text.Json.Serialization;

namespace Palace.Helpers;

/// <summary>
/// Portable tag catalog (JSON) for export/import. Tags stay global. Merge is
/// upsert-by-name (case-insensitive); parents, implicits, and aliases are
/// per-edge cycle/rebind-safe and never wipe unrelated rows. Requested by Sacb0y.
/// </summary>
public static class TagPortable
{
    public const string Schema = "palace-tags/1";
    public const string FileExtension = ".palace-tags.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Settings status-bar line after a tag import (summary + optional pre-import backup file).
    /// </summary>
    public static string FormatImportStatus(string summary, string? backupFilePath)
    {
        var message = "Imported tags. " + summary;
        if (!string.IsNullOrWhiteSpace(backupFilePath))
        {
            message += " Backup: " + backupFilePath;
        }

        return message;
    }

    public static string Serialize(TagPortableDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Schema = Schema;
        document.ExportedAt ??= DateTimeOffset.UtcNow.ToString("o");
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static TagPortableDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Tag export JSON is empty.", nameof(json));
        }

        var doc = JsonSerializer.Deserialize<TagPortableDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Tag export JSON deserialized to null.");
        if (!string.IsNullOrEmpty(doc.Schema)
            && !doc.Schema.StartsWith("palace-tags/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported tag export schema '{doc.Schema}'.");
        }

        doc.Tags ??= [];
        return doc;
    }

    public static TagPortableDocument FromCatalog(
        IEnumerable<TagPortableTagSource> tags,
        IEnumerable<TagPortableEdge> memberships,
        IEnumerable<TagPortableEdge> implications,
        IEnumerable<TagPortableEdge>? aliases = null)
    {
        var byId = tags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var parentsByChild = memberships
            .Where(e => byId.ContainsKey(e.FromId) && byId.ContainsKey(e.ToId))
            .GroupBy(e => e.ToId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => byId[e.FromId].Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.Ordinal);
        var impliesByTag = implications
            .Where(e => byId.ContainsKey(e.FromId) && byId.ContainsKey(e.ToId))
            .GroupBy(e => e.FromId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => byId[e.ToId].Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.Ordinal);
        // Alias edge: FromId = alias, ToId = ideal.
        var aliasOfByAliasId = (aliases ?? [])
            .Where(e => byId.ContainsKey(e.FromId) && byId.ContainsKey(e.ToId))
            .GroupBy(e => e.FromId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => byId[g.First().ToId].Name,
                StringComparer.Ordinal);

        var exported = byId.Values
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TagPortableEntry
            {
                Name = t.Name,
                Priority = t.Priority,
                Color = string.IsNullOrWhiteSpace(t.Color) ? null : t.Color,
                Starred = t.IsStarred ? true : null,
                Parents = parentsByChild.TryGetValue(t.Id, out var parents) && parents.Count > 0
                    ? parents
                    : null,
                Implies = impliesByTag.TryGetValue(t.Id, out var implies) && implies.Count > 0
                    ? implies
                    : null,
                AliasOf = aliasOfByAliasId.TryGetValue(t.Id, out var idealName)
                    ? idealName
                    : null
            })
            .ToList();

        return new TagPortableDocument
        {
            Schema = Schema,
            ExportedAt = DateTimeOffset.UtcNow.ToString("o"),
            Tags = exported
        };
    }

    /// <summary>
    /// Pure merge planner. Does not touch SQLite. Upserts by name; skips
    /// membership/implication/alias edges that would cycle or rebind. Per-edge
    /// refusal — refused edges keep earlier upserts on commit; exceptions roll the import tx back.
    /// </summary>
    public static TagMergePlan PlanMerge(
        IReadOnlyList<TagPortableTagSource> existing,
        IReadOnlyList<TagPortableEdge> existingMemberships,
        IReadOnlyList<TagPortableEdge> existingImplications,
        TagPortableDocument document) =>
        PlanMerge(existing, existingMemberships, existingImplications, [], document);

    /// <inheritdoc cref="PlanMerge(IReadOnlyList{TagPortableTagSource},IReadOnlyList{TagPortableEdge},IReadOnlyList{TagPortableEdge},TagPortableDocument)"/>
    public static TagMergePlan PlanMerge(
        IReadOnlyList<TagPortableTagSource> existing,
        IReadOnlyList<TagPortableEdge> existingMemberships,
        IReadOnlyList<TagPortableEdge> existingImplications,
        IReadOnlyList<TagPortableEdge> existingAliases,
        TagPortableDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var plan = new TagMergePlan();
        var byName = new Dictionary<string, TagPortableTagSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in existing)
        {
            if (!string.IsNullOrWhiteSpace(tag.Name))
            {
                byName[tag.Name.Trim()] = tag;
            }
        }

        var memberships = existingMemberships
            .Select(e => (e.FromId, e.ToId))
            .ToHashSet();
        var implications = existingImplications
            .Select(e => (e.FromId, e.ToId))
            .ToHashSet();
        // Working alias map (AliasId → IdealId); updated as edges are accepted.
        var aliasMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var edge in existingAliases)
        {
            if (!string.IsNullOrEmpty(edge.FromId) && !string.IsNullOrEmpty(edge.ToId))
            {
                aliasMap[edge.FromId] = edge.ToId;
            }
        }

        // Synthetic ids for tags that will be created (stable within this plan).
        var pendingCreates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string ResolveId(string name)
        {
            name = name.Trim();
            if (byName.TryGetValue(name, out var known))
            {
                return known.Id;
            }

            if (pendingCreates.TryGetValue(name, out var pending))
            {
                return pending;
            }

            var id = "new:" + Guid.NewGuid().ToString("N");
            pendingCreates[name] = id;
            return id;
        }

        foreach (var entry in document.Tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                plan.SkippedEmptyNames++;
                continue;
            }

            var name = entry.Name.Trim();
            if (byName.TryGetValue(name, out var current))
            {
                plan.Updates.Add(new TagMergeUpsert
                {
                    Id = current.Id,
                    Name = current.Name,
                    Priority = entry.Priority,
                    Color = entry.Color,
                    Starred = entry.Starred ?? false,
                    IsCreate = false
                });
                plan.Updated++;
            }
            else
            {
                var id = ResolveId(name);
                var created = new TagPortableTagSource
                {
                    Id = id,
                    Name = name,
                    Priority = entry.Priority,
                    Color = entry.Color,
                    IsStarred = entry.Starred ?? false
                };
                byName[name] = created;
                plan.Creates.Add(new TagMergeUpsert
                {
                    Id = id,
                    Name = name,
                    Priority = entry.Priority,
                    Color = entry.Color,
                    Starred = entry.Starred ?? false,
                    IsCreate = true
                });
                plan.Created++;
            }
        }

        // Ensure create stubs exist for parent/imply names not listed as entries.
        void EnsureNamed(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            name = name.Trim();
            if (byName.ContainsKey(name))
            {
                return;
            }

            var id = ResolveId(name);
            var stub = new TagPortableTagSource { Id = id, Name = name };
            byName[name] = stub;
            plan.Creates.Add(new TagMergeUpsert
            {
                Id = id,
                Name = name,
                Priority = 0,
                IsCreate = true
            });
            plan.Created++;
        }

        foreach (var entry in document.Tags ?? [])
        {
            foreach (var parent in entry.Parents ?? [])
            {
                EnsureNamed(parent);
            }

            foreach (var implied in entry.Implies ?? [])
            {
                EnsureNamed(implied);
            }

            // Empty aliasOf is skipped later; non-empty missing ideal → EnsureNamed.
            if (!string.IsNullOrWhiteSpace(entry.AliasOf))
            {
                EnsureNamed(entry.AliasOf);
            }
        }

        // Live id maps for cycle checks (include pending creates).
        var idByName = byName.ToDictionary(kv => kv.Key, kv => kv.Value.Id, StringComparer.OrdinalIgnoreCase);

        // Alias edges first so parents/implies can Ideal-resolve endpoints through
        // the same-file + existing alias map (Doktor PlanMerge follow-up).
        foreach (var entry in document.Tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Name)
                || !idByName.TryGetValue(entry.Name.Trim(), out var aliasId))
            {
                continue;
            }

            if (entry.AliasOf is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.AliasOf))
            {
                plan.AliasSkipped++;
                continue;
            }

            var idealName = entry.AliasOf.Trim();
            if (!idByName.TryGetValue(idealName, out var rawIdealId))
            {
                plan.AliasSkipped++;
                continue;
            }

            var ultimateIdealId = TagAliasResolve.Ideal(rawIdealId, aliasMap);
            var ultimateIdealName = byName.Values
                .FirstOrDefault(t => string.Equals(t.Id, ultimateIdealId, StringComparison.Ordinal))
                ?.Name ?? idealName;

            if (!TagAliasResolve.CanAdd(aliasId, ultimateIdealId, aliasMap))
            {
                plan.AliasRejected++;
                continue;
            }

            if (aliasMap.TryGetValue(aliasId, out var existingIdeal)
                && string.Equals(existingIdeal, ultimateIdealId, StringComparison.Ordinal))
            {
                // Idempotent same edge — nothing to apply.
                continue;
            }

            aliasMap[aliasId] = ultimateIdealId;
            plan.Aliases.Add(new TagMergeEdge
            {
                ParentName = entry.Name.Trim(),
                ChildName = ultimateIdealName,
                ParentId = aliasId,
                ChildId = ultimateIdealId
            });
            plan.AliasAdded++;
        }

        // File order may list an alias before its ideal's own aliasOf. Collapse
        // planned edges through the completed map so chains still land on the
        // ultimate ideal (Doktor reverse-order gap).
        foreach (var edge in plan.Aliases)
        {
            var ultimateIdealId = TagAliasResolve.Ideal(edge.ChildId, aliasMap);
            if (string.Equals(ultimateIdealId, edge.ChildId, StringComparison.Ordinal))
            {
                continue;
            }

            var ultimateIdealName = byName.Values
                .FirstOrDefault(t => string.Equals(t.Id, ultimateIdealId, StringComparison.Ordinal))
                ?.Name ?? edge.ChildName;
            aliasMap[edge.ParentId] = ultimateIdealId;
            edge.ChildId = ultimateIdealId;
            edge.ChildName = ultimateIdealName;
        }

        string IdealName(string id)
        {
            var idealId = TagAliasResolve.Ideal(id, aliasMap);
            return byName.Values
                .FirstOrDefault(t => string.Equals(t.Id, idealId, StringComparison.Ordinal))
                ?.Name
                ?? byName.Values
                    .FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))
                    ?.Name
                ?? id;
        }

        foreach (var entry in document.Tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || !idByName.TryGetValue(entry.Name.Trim(), out var childRaw))
            {
                continue;
            }

            var childId = TagAliasResolve.Ideal(childRaw, aliasMap);
            var childName = IdealName(childId);

            foreach (var parentName in entry.Parents ?? [])
            {
                if (string.IsNullOrWhiteSpace(parentName) || !idByName.TryGetValue(parentName.Trim(), out var parentRaw))
                {
                    plan.MembershipSkipped++;
                    continue;
                }

                var parentId = TagAliasResolve.Ideal(parentRaw, aliasMap);
                if (string.Equals(parentId, childId, StringComparison.Ordinal)
                    || WouldMembershipCycle(parentId, childId, memberships))
                {
                    plan.MembershipRejected++;
                    continue;
                }

                if (memberships.Add((parentId, childId)))
                {
                    plan.Memberships.Add(new TagMergeEdge
                    {
                        ParentName = IdealName(parentId),
                        ChildName = childName,
                        ParentId = parentId,
                        ChildId = childId
                    });
                    plan.MembershipAdded++;
                }
            }

            foreach (var impliedName in entry.Implies ?? [])
            {
                if (string.IsNullOrWhiteSpace(impliedName) || !idByName.TryGetValue(impliedName.Trim(), out var impliedRaw))
                {
                    plan.ImplicationSkipped++;
                    continue;
                }

                var impliedId = TagAliasResolve.Ideal(impliedRaw, aliasMap);
                if (string.Equals(childId, impliedId, StringComparison.Ordinal)
                    || WouldImplicationCycle(childId, impliedId, implications))
                {
                    plan.ImplicationRejected++;
                    continue;
                }

                if (implications.Add((childId, impliedId)))
                {
                    plan.Implications.Add(new TagMergeEdge
                    {
                        ParentName = childName,
                        ChildName = IdealName(impliedId),
                        ParentId = childId,
                        ChildId = impliedId
                    });
                    plan.ImplicationAdded++;
                }
            }
        }

        return plan;
    }

    public static bool WouldMembershipCycle(
        string parentId,
        string childId,
        IEnumerable<(string FromId, string ToId)> edges) =>
        Descendants(childId, edges.Select(e => (e.FromId, e.ToId))).Contains(parentId);

    public static bool WouldImplicationCycle(
        string tagId,
        string impliedTagId,
        IEnumerable<(string FromId, string ToId)> edges) =>
        Descendants(impliedTagId, edges.Select(e => (e.FromId, e.ToId))).Contains(tagId);

    private static HashSet<string> Descendants(string start, IEnumerable<(string FromId, string ToId)> edges)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (from, to) in edges)
        {
            if (!children.TryGetValue(from, out var list))
            {
                list = [];
                children[from] = list;
            }

            list.Add(to);
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(start);
        var seen = new HashSet<string>(StringComparer.Ordinal) { start };
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
}

public sealed class TagPortableDocument
{
    public string Schema { get; set; } = TagPortable.Schema;
    public string? ExportedAt { get; set; }
    public List<TagPortableEntry> Tags { get; set; } = [];
}

public sealed class TagPortableEntry
{
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    public string? Color { get; set; }
    public bool? Starred { get; set; }
    public List<string>? Parents { get; set; }
    public List<string>? Implies { get; set; }
    /// <summary>Ideal tag name this entry aliases (n→1). Null/omitted = no alias edge.</summary>
    public string? AliasOf { get; set; }
}

public sealed class TagPortableTagSource
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    public string? Color { get; set; }
    public bool IsStarred { get; set; }
}

/// <summary>Directed edge: membership Parent→Child, or implication Tag→Implied.</summary>
public sealed class TagPortableEdge
{
    public string FromId { get; set; } = "";
    public string ToId { get; set; } = "";
}

public sealed class TagMergePlan
{
    public List<TagMergeUpsert> Creates { get; } = [];
    public List<TagMergeUpsert> Updates { get; } = [];
    public List<TagMergeEdge> Memberships { get; } = [];
    public List<TagMergeEdge> Implications { get; } = [];
    /// <summary>Alias→ideal edges (ParentName/Id = alias, ChildName/Id = ideal).</summary>
    public List<TagMergeEdge> Aliases { get; } = [];
    public int Created { get; set; }
    public int Updated { get; set; }
    public int MembershipAdded { get; set; }
    public int MembershipRejected { get; set; }
    public int MembershipSkipped { get; set; }
    public int ImplicationAdded { get; set; }
    public int ImplicationRejected { get; set; }
    public int ImplicationSkipped { get; set; }
    public int AliasAdded { get; set; }
    public int AliasRejected { get; set; }
    public int AliasSkipped { get; set; }
    public int SkippedEmptyNames { get; set; }

    public string Summary =>
        $"Created {Created}, updated {Updated}, memberships +{MembershipAdded} (rejected {MembershipRejected}), implicits +{ImplicationAdded} (rejected {ImplicationRejected}), aliases +{AliasAdded} (rejected {AliasRejected}).";
}

public sealed class TagMergeUpsert
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    public string? Color { get; set; }
    public bool Starred { get; set; }
    public bool IsCreate { get; set; }
}

public sealed class TagMergeEdge
{
    public string ParentName { get; set; } = "";
    public string ChildName { get; set; } = "";
    public string ParentId { get; set; } = "";
    public string ChildId { get; set; } = "";
}
