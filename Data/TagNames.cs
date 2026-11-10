using System.Text;
using Microsoft.Data.Sqlite;
using Palace.Helpers;

namespace Palace.Data;

/// <summary>
/// Tag names are global (shared by every project) and unique ignoring case, surrounding
/// whitespace, and <c>_</c> / <c>-</c> / internal whitespace runs (all fold to a single space).
/// Lookups, the create / rename guards, and the startup merge of legacy duplicates
/// all use <see cref="Same"/>. The <see cref="UniqueIndexName"/> index (NOCASE, ASCII folding) is a
/// backstop for case-only clashes; the C# comparison is stricter, so the merge always leaves the
/// index creatable after names are stored in normalized form.
/// </summary>
public static class TagNames
{
    public const string UniqueIndexName = "UX_Tag_Name_NoCase";

    /// <summary>
    /// Trim; replace <c>_</c>, <c>-</c>, and runs of whitespace with a single space.
    /// Case is preserved for storage; equality uses <see cref="Same"/>.
    /// </summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "";
        }

        var sb = new StringBuilder(name.Length);
        var pendingSpace = false;
        var started = false;
        foreach (var c in name)
        {
            if (c is '_' or '-' || char.IsWhiteSpace(c))
            {
                if (started)
                {
                    pendingSpace = true;
                }

                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
            started = true;
        }

        return sb.ToString();
    }

    public static bool Same(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static string DuplicateMessage(string name) =>
        $"A tag named \"{Normalize(name)}\" already exists. Tag names are shared across all projects and ignore case.";

    /// <summary>
    /// Strip A1111 attention / alternate-weight wrappers from one prompt token, then
    /// <see cref="Normalize"/>. Does not resolve aliases — see <see cref="ResolvePromptToken"/>.
    /// </summary>
    public static string FromPromptToken(string? raw) => Normalize(StripPromptWeights(raw));

    /// <summary>
    /// Strip <c>(name:1.2)</c> / <c>[x]</c> / <c>{x}</c> (and nested forms), then normalize,
    /// then map through <paramref name="aliasNameToIdealName"/> keyed by normalized names.
    /// </summary>
    public static string ResolvePromptToken(
        string? raw,
        IReadOnlyDictionary<string, string>? aliasNameToIdealName)
    {
        var key = FromPromptToken(raw);
        if (key.Length == 0 || aliasNameToIdealName is null || aliasNameToIdealName.Count == 0)
        {
            return key;
        }

        if (aliasNameToIdealName.TryGetValue(key, out var ideal)
            || aliasNameToIdealName.TryGetValue(key.ToLowerInvariant(), out ideal))
        {
            return Normalize(ideal);
        }

        foreach (var (alias, mapped) in aliasNameToIdealName)
        {
            if (Same(alias, key))
            {
                return Normalize(mapped);
            }
        }

        return key;
    }

    /// <summary>
    /// Peel A1111 weight / attention syntax: <c>(black hair:1.2)</c>, nested parens,
    /// <c>[x]</c>, <c>{x}</c>, and a trailing <c>:1.2</c> without brackets.
    /// </summary>
    public static string StripPromptWeights(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var s = raw.Trim().Trim('"', '\'', ' ');
        var guard = 0;
        while (guard++ < 32)
        {
            s = s.Trim();
            if (s.Length < 2)
            {
                break;
            }

            var open = s[0];
            var close = open switch
            {
                '(' => ')',
                '[' => ']',
                '{' => '}',
                _ => '\0'
            };
            if (close != '\0' && s[^1] == close)
            {
                var inner = s[1..^1].Trim();
                var colon = inner.LastIndexOf(':');
                if (colon > 0 && IsPromptWeight(inner[(colon + 1)..]))
                {
                    inner = inner[..colon].Trim();
                }

                s = inner;
                continue;
            }

            var weightColon = s.LastIndexOf(':');
            if (weightColon > 0 && IsPromptWeight(s[(weightColon + 1)..]))
            {
                s = s[..weightColon].Trim();
                continue;
            }

            break;
        }

        return s;
    }

    private static bool IsPromptWeight(string text)
    {
        var t = text.Trim();
        if (t.Length == 0)
        {
            return false;
        }

        // "1.2", ".5", "1", "1.2.3" refuse — require a simple decimal.
        var dot = 0;
        for (var i = 0; i < t.Length; i++)
        {
            var c = t[i];
            if (c == '.')
            {
                dot++;
                if (dot > 1)
                {
                    return false;
                }

                continue;
            }

            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Id of a tag whose name matches <paramref name="name"/>, other than <paramref name="excludeId"/>.</summary>
    public static string? FindId(SqliteConnection conn, string name, string? excludeId = null, SqliteTransaction? tx = null)
    {
        var key = Normalize(name);
        if (key.Length == 0)
        {
            return null;
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // Full scan: underscore / hyphen folding is stricter than SQL trim+NOCASE.
        cmd.CommandText = "SELECT Id, Name FROM Tag";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            if (!string.Equals(id, excludeId, StringComparison.Ordinal) && Same(reader.GetString(1), key))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>True when at least one normalized-name group has more than one Tag row.</summary>
    public static bool HasDuplicateGroups(SqliteConnection conn) =>
        LoadTags(conn)
            .GroupBy(t => Normalize(t.Name), StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() > 1);

    /// <summary>True when any stored name differs from <see cref="Normalize"/> (merge or rewrite needed).</summary>
    public static bool NeedsNormalizePass(SqliteConnection conn)
    {
        foreach (var tag in LoadTags(conn))
        {
            var normalized = Normalize(tag.Name);
            if (normalized.Length > 0 && !string.Equals(normalized, tag.Name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return HasDuplicateGroups(conn);
    }

    /// <summary>Merge legacy duplicates, rewrite stored names to <see cref="Normalize"/>, then add the unique index. Safe to run on every open.</summary>
    public static TagNameMergeResult EnsureUnique(SqliteConnection conn)
    {
        var result = MergeDuplicates(conn);
        NormalizeStoredNames(conn);
        FlattenAliasChains(conn);
        using var index = conn.CreateCommand();
        index.CommandText = $"CREATE UNIQUE INDEX IF NOT EXISTS {UniqueIndexName} ON Tag(Name COLLATE NOCASE)";
        index.ExecuteNonQuery();
        return result;
    }

    /// <summary>
    /// Fold every group of same-named tags into one survivor (most assignments, then priority,
    /// starred, id). Asset assignments, parents / children and implications move to the survivor;
    /// a manual assignment wins over an implied one. Edges that would become self-loops or cycles
    /// are dropped (counted in <see cref="TagNameMergeResult.SkippedEdges"/>); assignments never are.
    /// </summary>
    public static TagNameMergeResult MergeDuplicates(SqliteConnection conn)
    {
        var groups = LoadTags(conn)
            .GroupBy(t => Normalize(t.Name), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g
                .OrderByDescending(t => t.Assignments)
                .ThenByDescending(t => t.Priority)
                .ThenByDescending(t => t.IsStarred)
                .ThenBy(t => t.Id, StringComparer.Ordinal)
                .ToList())
            .ToList();
        if (groups.Count == 0)
        {
            return TagNameMergeResult.None;
        }

        var survivors = new List<string>();
        var merged = 0;
        var skipped = 0;
        using var tx = conn.BeginTransaction();
        foreach (var group in groups)
        {
            var survivor = group[0];
            foreach (var loser in group.Skip(1))
            {
                skipped += MergeInto(conn, tx, loser, survivor);
                merged++;
            }

            survivors.Add(survivor.Id);
        }

        tx.Commit();
        return new TagNameMergeResult(merged, survivors, skipped);
    }

    /// <summary>
    /// Store every tag name in <see cref="Normalize"/> form. A padded / underscored legacy name can
    /// be the merge survivor; lookups and import key on the normalized form. Runs after the merge,
    /// so no two normalized names can collide. Whitespace-only names are left alone. Returns how
    /// many changed.
    /// </summary>
    public static int NormalizeStoredNames(SqliteConnection conn)
    {
        var padded = new List<(string Id, string Name)>();
        using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT Id, Name FROM Tag";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                var normalized = Normalize(name);
                if (normalized.Length > 0 && !string.Equals(normalized, name, StringComparison.Ordinal))
                {
                    padded.Add((reader.GetString(0), normalized));
                }
            }
        }

        if (padded.Count == 0)
        {
            return 0;
        }

        using var tx = conn.BeginTransaction();
        foreach (var (id, name) in padded)
        {
            using var update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE Tag SET Name = $name WHERE Id = $id";
            update.Parameters.AddWithValue("$name", name);
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }

        tx.Commit();
        return padded.Count;
    }

    /// <summary>Legacy name for <see cref="NormalizeStoredNames"/>.</summary>
    public static int TrimPaddedNames(SqliteConnection conn) => NormalizeStoredNames(conn);

    /// <summary>
    /// Re-point every alias edge at its ultimate ideal so the map stores no chains.
    /// Loops are left alone (resolve stops); <see cref="TagAliasResolve.CanAdd"/> refuses new ones.
    /// Returns how many edges were rewritten.
    /// </summary>
    public static int FlattenAliasChains(SqliteConnection conn)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT AliasTagId, IdealTagId FROM TagAlias";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                map[reader.GetString(0)] = reader.GetString(1);
            }
        }

        if (map.Count == 0)
        {
            return 0;
        }

        var updates = new List<(string AliasId, string UltimateIdealId)>();
        foreach (var (aliasId, idealId) in map)
        {
            var ultimate = TagAliasResolve.Ideal(aliasId, map);
            if (!string.Equals(ultimate, idealId, StringComparison.Ordinal)
                && !string.Equals(ultimate, aliasId, StringComparison.Ordinal))
            {
                updates.Add((aliasId, ultimate));
            }
        }

        if (updates.Count == 0)
        {
            return 0;
        }

        using var tx = conn.BeginTransaction();
        foreach (var (aliasId, ultimate) in updates)
        {
            using var update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE TagAlias SET IdealTagId = $i WHERE AliasTagId = $a";
            update.Parameters.AddWithValue("$i", ultimate);
            update.Parameters.AddWithValue("$a", aliasId);
            update.ExecuteNonQuery();
        }

        tx.Commit();
        return updates.Count;
    }

    private static int MergeInto(SqliteConnection conn, SqliteTransaction tx, TagRow loser, TagRow survivor)
    {
        Exec(conn, tx, """
            UPDATE AssetTag SET Source = (
                SELECT l.Source FROM AssetTag l WHERE l.AssetId = AssetTag.AssetId AND l.TagId = $loser)
            WHERE TagId = $survivor AND Source = 'Implied'
              AND EXISTS (
                SELECT 1 FROM AssetTag l
                WHERE l.AssetId = AssetTag.AssetId AND l.TagId = $loser AND l.Source <> 'Implied');
            INSERT OR IGNORE INTO AssetTag (AssetId, TagId, Source)
            SELECT AssetId, $survivor, Source FROM AssetTag WHERE TagId = $loser;
            """, loser.Id, survivor.Id);

        var skipped = MoveEdges(conn, tx, "TagMembership", "ParentId", "ChildId", loser.Id, survivor.Id);
        skipped += MoveEdges(conn, tx, "TagImplication", "TagId", "ImpliedTagId", loser.Id, survivor.Id);
        skipped += MoveEdges(conn, tx, "TagAlias", "AliasTagId", "IdealTagId", loser.Id, survivor.Id);

        using (var fields = conn.CreateCommand())
        {
            fields.Transaction = tx;
            fields.CommandText = """
                UPDATE Tag SET
                    Priority = max(Priority, $priority),
                    IsStarred = max(IsStarred, $starred),
                    Color = COALESCE(Color, $color)
                WHERE Id = $survivor
                """;
            fields.Parameters.AddWithValue("$priority", loser.Priority);
            fields.Parameters.AddWithValue("$starred", loser.IsStarred ? 1 : 0);
            fields.Parameters.AddWithValue("$color", (object?)loser.Color ?? DBNull.Value);
            fields.Parameters.AddWithValue("$survivor", survivor.Id);
            fields.ExecuteNonQuery();
        }

        // Cascades clear the loser's (already copied) AssetTag / edge rows.
        Exec(conn, tx, "DELETE FROM Tag WHERE Id = $loser", loser.Id, survivor.Id);
        return skipped;
    }

    /// <summary>Re-point every edge touching <paramref name="loser"/> at <paramref name="survivor"/>.</summary>
    private static int MoveEdges(
        SqliteConnection conn,
        SqliteTransaction tx,
        string table,
        string fromColumn,
        string toColumn,
        string loser,
        string survivor)
    {
        var moved = new List<(string From, string To)>();
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = $"SELECT {fromColumn}, {toColumn} FROM {table} WHERE {fromColumn} = $loser OR {toColumn} = $loser";
            read.Parameters.AddWithValue("$loser", loser);
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                var from = reader.GetString(0);
                var to = reader.GetString(1);
                moved.Add((from == loser ? survivor : from, to == loser ? survivor : to));
            }
        }

        using (var delete = conn.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = $"DELETE FROM {table} WHERE {fromColumn} = $loser OR {toColumn} = $loser";
            delete.Parameters.AddWithValue("$loser", loser);
            delete.ExecuteNonQuery();
        }

        var skipped = 0;
        foreach (var (from, to) in moved)
        {
            if (string.Equals(from, to, StringComparison.Ordinal) || Reaches(conn, tx, table, fromColumn, toColumn, to, from))
            {
                skipped++;
                continue;
            }

            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = $"INSERT OR IGNORE INTO {table} ({fromColumn}, {toColumn}) VALUES ($f, $t)";
            insert.Parameters.AddWithValue("$f", from);
            insert.Parameters.AddWithValue("$t", to);
            insert.ExecuteNonQuery();
        }

        return skipped;
    }

    /// <summary>True when <paramref name="target"/> is reachable from <paramref name="start"/> (edge from→to).</summary>
    private static bool Reaches(
        SqliteConnection conn,
        SqliteTransaction tx,
        string table,
        string fromColumn,
        string toColumn,
        string start,
        string target)
    {
        var next = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT {fromColumn}, {toColumn} FROM {table}";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var from = reader.GetString(0);
                if (!next.TryGetValue(from, out var list))
                {
                    list = [];
                    next[from] = list;
                }

                list.Add(reader.GetString(1));
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal) { start };
        var stack = new Stack<string>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (string.Equals(current, target, StringComparison.Ordinal))
            {
                return true;
            }

            if (next.TryGetValue(current, out var kids))
            {
                foreach (var kid in kids.Where(seen.Add))
                {
                    stack.Push(kid);
                }
            }
        }

        return false;
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql, string loser, string survivor)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$loser", loser);
        cmd.Parameters.AddWithValue("$survivor", survivor);
        cmd.ExecuteNonQuery();
    }

    private static List<TagRow> LoadTags(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.Id, t.Name, t.Priority, t.Color, t.IsStarred,
                   (SELECT COUNT(*) FROM AssetTag a WHERE a.TagId = t.Id)
            FROM Tag t
            """;
        var list = new List<TagRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TagRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                reader.GetInt64(5)));
        }

        return list;
    }

    private sealed record TagRow(string Id, string Name, int Priority, string? Color, bool IsStarred, long Assignments);
}

public sealed record TagNameMergeResult(int MergedTags, IReadOnlyList<string> SurvivorIds, int SkippedEdges)
{
    public static TagNameMergeResult None { get; } = new(0, [], 0);
}

/// <summary>Thrown when a create or rename would duplicate an existing tag name.</summary>
public sealed class DuplicateTagNameException(string name)
    : InvalidOperationException(TagNames.DuplicateMessage(name))
{
    public string Name { get; } = TagNames.Normalize(name);
}
