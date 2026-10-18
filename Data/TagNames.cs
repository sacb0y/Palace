using Microsoft.Data.Sqlite;

namespace Palace.Data;

/// <summary>
/// Tag names are global (shared by every project) and unique ignoring case and surrounding
/// whitespace. Lookups, the create / rename guards, and the startup merge of legacy duplicates
/// all use <see cref="Same"/>. The <see cref="UniqueIndexName"/> index (NOCASE, ASCII folding) is a
/// backstop; the C# comparison is stricter, so the merge always leaves the index creatable.
/// </summary>
public static class TagNames
{
    public const string UniqueIndexName = "UX_Tag_Name_NoCase";

    public static string Normalize(string? name) => (name ?? "").Trim();

    public static bool Same(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static string DuplicateMessage(string name) =>
        $"A tag named \"{Normalize(name)}\" already exists. Tag names are shared across all projects and ignore case.";

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
        // NOCASE only folds ASCII, so non-ASCII names fall back to a full in-process compare.
        cmd.CommandText = IsAscii(key)
            ? "SELECT Id, Name FROM Tag WHERE trim(Name) = $key COLLATE NOCASE"
            : "SELECT Id, Name FROM Tag";
        cmd.Parameters.AddWithValue("$key", key);
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

    /// <summary>Merge legacy duplicates, trim padded names, then add the unique index. Safe to run on every open.</summary>
    public static TagNameMergeResult EnsureUnique(SqliteConnection conn)
    {
        var result = MergeDuplicates(conn);
        TrimPaddedNames(conn);
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
    /// Store every tag name trimmed. A padded legacy name (" Cat ") can be the merge survivor, and
    /// name lookups such as tag import key on the trimmed form. Runs after the merge, so no two
    /// trimmed names can collide. Whitespace-only names are left alone. Returns how many changed.
    /// </summary>
    public static int TrimPaddedNames(SqliteConnection conn)
    {
        var padded = new List<(string Id, string Name)>();
        using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT Id, Name FROM Tag";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                var trimmed = Normalize(name);
                if (trimmed.Length > 0 && !string.Equals(trimmed, name, StringComparison.Ordinal))
                {
                    padded.Add((reader.GetString(0), trimmed));
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

    private static bool IsAscii(string value)
    {
        foreach (var c in value)
        {
            if (c > 127)
            {
                return false;
            }
        }

        return true;
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
