using Microsoft.Data.Sqlite;
using Palace.Data;
using Xunit;

namespace Palace.Tests;

public sealed class TagNamesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "palace-tagnames-tests-" + Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(_dir, "palace.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("Cat", "cat", true)]
    [InlineData("  Cat ", "CAT", true)]
    [InlineData("Été", "été", true)]
    [InlineData("Cat", "Cats", false)]
    [InlineData("Black Hair", "black_hair", true)]
    [InlineData("black-hair", "Black  Hair", true)]
    [InlineData("black__hair", "black - hair", true)]
    [InlineData("black_hair", "blonde_hair", false)]
    public void Same_ignores_case_whitespace_underscores_and_hyphens(string a, string b, bool expected) =>
        Assert.Equal(expected, TagNames.Same(a, b));

    [Theory]
    [InlineData("  black_hair ", "black hair")]
    [InlineData("Black-Hair", "Black Hair")]
    [InlineData("a__b--c", "a b c")]
    public void Normalize_folds_underscores_hyphens_and_whitespace(string raw, string expected) =>
        Assert.Equal(expected, TagNames.Normalize(raw));

    [Theory]
    [InlineData("(black hair:1.2)", "black hair")]
    [InlineData("((word:1.1))", "word")]
    [InlineData("[scenery]", "scenery")]
    [InlineData("{cat}", "cat")]
    [InlineData("(black_hair:0.8)", "black hair")]
    [InlineData("plain", "plain")]
    [InlineData("weighted:1.5", "weighted")]
    public void FromPromptToken_strips_weights_then_normalizes(string raw, string expected) =>
        Assert.Equal(expected, TagNames.FromPromptToken(raw));

    [Fact]
    public void ResolvePromptToken_maps_alias_after_strip_and_normalize()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["doggy"] = "dog",
            ["black hair"] = "dark hair"
        };
        Assert.Equal("dog", TagNames.ResolvePromptToken("(doggy:1.2)", aliases));
        Assert.Equal("dark hair", TagNames.ResolvePromptToken("black_hair", aliases));
        Assert.Equal("untouched", TagNames.ResolvePromptToken("untouched", aliases));
    }

    [Fact]
    public void DuplicateMessage_names_the_tag_and_says_names_are_global()
    {
        var message = TagNames.DuplicateMessage("  black_hair ");
        Assert.Contains("\"black hair\"", message);
        Assert.Contains("all projects", message);
        Assert.Equal(message, new DuplicateTagNameException("black_hair").Message);
    }

    [Fact]
    public async Task Fresh_catalog_rejects_a_case_only_duplicate_at_the_database_level()
    {
        using var db = new PalaceDb(DbPath);
        Assert.Equal(0, db.TagNameMerge.MergedTags);
        await db.WriteAsync(conn =>
        {
            Assert.True(IndexExists(conn));
            // Seeded defaults include "Subject".
            var ex = Assert.Throws<SqliteException>(() => InsertTag(conn, "t-dup", "subject"));
            Assert.Equal(19, ex.SqliteErrorCode); // SQLITE_CONSTRAINT
        });
    }

    [Fact]
    public async Task FindId_matches_any_case_trimmed_and_honours_excludeId()
    {
        using var db = new PalaceDb(DbPath);
        await db.WriteAsync(conn =>
        {
            InsertTag(conn, "t-ete", "Été");
            InsertTag(conn, "t-hair", "Black Hair");
            var subject = TagNames.FindId(conn, "  SUBJECT ");
            Assert.NotNull(subject);
            Assert.Null(TagNames.FindId(conn, "subject", excludeId: subject));
            Assert.Equal("t-ete", TagNames.FindId(conn, "été"));
            Assert.Equal("t-hair", TagNames.FindId(conn, "black_hair"));
            Assert.Equal("t-hair", TagNames.FindId(conn, "black-hair"));
            Assert.Null(TagNames.FindId(conn, "nope"));
            Assert.Null(TagNames.FindId(conn, "   "));
        });
    }

    [Fact]
    public async Task Reopening_merges_underscore_near_duplicates_after_backup()
    {
        using (var first = new PalaceDb(DbPath))
        {
            await first.WriteAsync(conn =>
            {
                Exec(conn, $"DROP INDEX {TagNames.UniqueIndexName}");
                Exec(conn, "INSERT INTO SourceFolder (Id, Path) VALUES ('s1', '/lib')");
                Exec(conn, "INSERT INTO Asset (Id, SourceFolderId, Path, FileName, Kind, DateAdded) VALUES ('a1', 's1', '/lib/a1.png', 'a1.png', 'Image', '2026-01-01')");
                InsertTag(conn, "spaced", "Black Hair", priority: 2, starred: true);
                InsertTag(conn, "under", "black_hair", priority: 1);
                Assign(conn, "a1", "under", "Manual");
            });
        }

        SqliteConnection.ClearAllPools();
        using var db = new PalaceDb(DbPath);
        Assert.Null(db.TagNameMergeError);
        Assert.Null(db.TagNameMergeSkipReason);
        Assert.NotNull(db.TagNameMergeBackupDir);
        Assert.True(Directory.Exists(db.TagNameMergeBackupDir));
        Assert.True(File.Exists(Path.Combine(db.TagNameMergeBackupDir!, "palace.db")));
        Assert.Equal(1, db.TagNameMerge.MergedTags);
        Assert.Equal(["under"], db.TagNameMerge.SurvivorIds);

        await db.ReadAsync(conn =>
        {
            Assert.Equal(1L, Scalar(conn, "SELECT COUNT(*) FROM Tag WHERE Id IN ('spaced', 'under')"));
            Assert.Equal("black hair", Text(conn, "SELECT Name FROM Tag WHERE Id = 'under'"));
            Assert.Equal(1L, Scalar(conn, "SELECT COUNT(*) FROM AssetTag WHERE TagId = 'under'"));
            Assert.Equal(2L, Scalar(conn, "SELECT Priority FROM Tag WHERE Id = 'under'"));
            Assert.Equal(1L, Scalar(conn, "SELECT IsStarred FROM Tag WHERE Id = 'under'"));
            return 0;
        });
    }

    [Fact]
    public async Task Reopening_a_legacy_catalog_merges_duplicates_without_losing_assignments()
    {
        // Build a catalog as it looked before the unique index existed.
        using (var first = new PalaceDb(DbPath))
        {
            await first.WriteAsync(conn =>
            {
                Exec(conn, $"DROP INDEX {TagNames.UniqueIndexName}");
                Exec(conn, "INSERT INTO SourceFolder (Id, Path) VALUES ('s1', '/lib')");
                foreach (var a in new[] { "a1", "a2", "a3" })
                {
                    Exec(conn, $"INSERT INTO Asset (Id, SourceFolderId, Path, FileName, Kind, DateAdded) VALUES ('{a}', 's1', '/lib/{a}.png', '{a}.png', 'Image', '2026-01-01')");
                }

                // "cat" has more assignments, so it survives; "Cat" and " CAT " fold into it.
                InsertTag(conn, "cat", "cat", priority: 1);
                InsertTag(conn, "Cat", "Cat", priority: 5, color: "#FF0000", starred: true);
                InsertTag(conn, "CAT", " CAT ");
                InsertTag(conn, "animals", "Animals");
                InsertTag(conn, "pets", "Pets");
                InsertTag(conn, "fluffy", "Fluffy");
                Assign(conn, "a1", "cat", "Implied");
                Assign(conn, "a2", "cat", "Manual");
                Assign(conn, "a1", "Cat", "Manual");
                Assign(conn, "a3", "CAT", "Prompt");
                Exec(conn, "INSERT INTO TagMembership (ParentId, ChildId) VALUES ('animals', 'Cat')");
                Exec(conn, "INSERT INTO TagMembership (ParentId, ChildId) VALUES ('CAT', 'fluffy')");
                // Would become a cycle (cat -> pets -> cat) after the merge, so it is dropped.
                Exec(conn, "INSERT INTO TagMembership (ParentId, ChildId) VALUES ('cat', 'pets')");
                Exec(conn, "INSERT INTO TagMembership (ParentId, ChildId) VALUES ('pets', 'Cat')");
                Exec(conn, "INSERT INTO TagImplication (TagId, ImpliedTagId) VALUES ('Cat', 'animals')");
            });
        }

        SqliteConnection.ClearAllPools();
        using var db = new PalaceDb(DbPath);
        Assert.Null(db.TagNameMergeError);
        Assert.Equal(2, db.TagNameMerge.MergedTags);
        Assert.Equal(["cat"], db.TagNameMerge.SurvivorIds);
        Assert.Equal(1, db.TagNameMerge.SkippedEdges);

        await db.ReadAsync(conn =>
        {
            Assert.True(IndexExists(conn));
            Assert.Equal(1L, Scalar(conn, "SELECT COUNT(*) FROM Tag WHERE lower(trim(Name)) = 'cat'"));
            Assert.Equal("cat", Text(conn, "SELECT Name FROM Tag WHERE Id = 'cat'"));

            // Every asset keeps the tag; a manual assignment beats the implied duplicate.
            Assert.Equal("Manual", Text(conn, "SELECT Source FROM AssetTag WHERE AssetId = 'a1' AND TagId = 'cat'"));
            Assert.Equal("Manual", Text(conn, "SELECT Source FROM AssetTag WHERE AssetId = 'a2' AND TagId = 'cat'"));
            Assert.Equal("Prompt", Text(conn, "SELECT Source FROM AssetTag WHERE AssetId = 'a3' AND TagId = 'cat'"));
            Assert.Equal(0L, Scalar(conn, "SELECT COUNT(*) FROM AssetTag WHERE TagId IN ('Cat', 'CAT')"));

            // Groups and implications follow the survivor.
            Assert.Equal(1L, Scalar(conn, "SELECT COUNT(*) FROM TagMembership WHERE ParentId = 'animals' AND ChildId = 'cat'"));
            Assert.Equal(1L, Scalar(conn, "SELECT COUNT(*) FROM TagMembership WHERE ParentId = 'cat' AND ChildId = 'fluffy'"));
            Assert.Equal(1L, Scalar(conn, "SELECT COUNT(*) FROM TagMembership WHERE ParentId = 'cat' AND ChildId = 'pets'"));
            Assert.Equal(0L, Scalar(conn, "SELECT COUNT(*) FROM TagMembership WHERE ParentId = 'pets' AND ChildId = 'cat'"));
            Assert.Equal(1L, Scalar(conn, "SELECT COUNT(*) FROM TagImplication WHERE TagId = 'cat' AND ImpliedTagId = 'animals'"));

            // Survivor keeps the strongest priority / star and inherits the color it lacked.
            Assert.Equal(5L, Scalar(conn, "SELECT Priority FROM Tag WHERE Id = 'cat'"));
            Assert.Equal(1L, Scalar(conn, "SELECT IsStarred FROM Tag WHERE Id = 'cat'"));
            Assert.Equal("#FF0000", Text(conn, "SELECT Color FROM Tag WHERE Id = 'cat'"));
            return 0;
        });
    }

    [Fact]
    public async Task A_padded_survivor_and_padded_singles_are_stored_normalized()
    {
        using (var first = new PalaceDb(DbPath))
        {
            await first.WriteAsync(conn =>
            {
                Exec(conn, $"DROP INDEX {TagNames.UniqueIndexName}");
                Exec(conn, "INSERT INTO SourceFolder (Id, Path) VALUES ('s1', '/lib')");
                Exec(conn, "INSERT INTO Asset (Id, SourceFolderId, Path, FileName, Kind, DateAdded) VALUES ('a1', 's1', '/lib/a1.png', 'a1.png', 'Image', '2026-01-01')");
                // The padded tag has the assignment, so it survives the merge.
                InsertTag(conn, "padded", " Dog ");
                InsertTag(conn, "plain", "dog");
                InsertTag(conn, "lonely", "  Bird");
                InsertTag(conn, "hyphen", "blue-sky");
                Assign(conn, "a1", "padded", "Manual");
            });
        }

        SqliteConnection.ClearAllPools();
        using var db = new PalaceDb(DbPath);
        Assert.Null(db.TagNameMergeError);
        Assert.Equal(["padded"], db.TagNameMerge.SurvivorIds);
        await db.ReadAsync(conn =>
        {
            Assert.True(IndexExists(conn));
            Assert.Equal("Dog", Text(conn, "SELECT Name FROM Tag WHERE Id = 'padded'"));
            Assert.Equal("Bird", Text(conn, "SELECT Name FROM Tag WHERE Id = 'lonely'"));
            Assert.Equal("blue sky", Text(conn, "SELECT Name FROM Tag WHERE Id = 'hyphen'"));
            Assert.Equal(0L, Scalar(conn, "SELECT COUNT(*) FROM Tag WHERE Id = 'plain'"));
            return 0;
        });
    }

    [Fact]
    public async Task FlattenAliasChains_rewrites_edges_to_ultimate_ideal()
    {
        using var db = new PalaceDb(DbPath);
        await db.WriteAsync(conn =>
        {
            InsertTag(conn, "a", "a-name");
            InsertTag(conn, "b", "b-name");
            InsertTag(conn, "c", "c-name");
            Exec(conn, "INSERT INTO TagAlias (AliasTagId, IdealTagId) VALUES ('a', 'b')");
            Exec(conn, "INSERT INTO TagAlias (AliasTagId, IdealTagId) VALUES ('b', 'c')");
            Assert.Equal(1, TagNames.FlattenAliasChains(conn));
            Assert.Equal("c", Text(conn, "SELECT IdealTagId FROM TagAlias WHERE AliasTagId = 'a'"));
            Assert.Equal("c", Text(conn, "SELECT IdealTagId FROM TagAlias WHERE AliasTagId = 'b'"));
            Assert.Equal(0, TagNames.FlattenAliasChains(conn));
        });
    }

    [Fact]
    public async Task Non_ascii_duplicates_that_nocase_misses_are_merged_too()
    {
        using (var first = new PalaceDb(DbPath))
        {
            await first.WriteAsync(conn =>
            {
                InsertTag(conn, "e1", "Été");
                InsertTag(conn, "e2", "été");
            });
        }

        SqliteConnection.ClearAllPools();
        using var db = new PalaceDb(DbPath);
        Assert.Equal(1, db.TagNameMerge.MergedTags);
        Assert.Equal(1L, await db.ReadAsync(conn => Scalar(conn, "SELECT COUNT(*) FROM Tag WHERE Id IN ('e1', 'e2')")));
    }

    [Fact]
    public async Task A_clean_catalog_reopens_without_changes()
    {
        using (new PalaceDb(DbPath))
        {
        }

        SqliteConnection.ClearAllPools();
        using var db = new PalaceDb(DbPath);
        Assert.Same(TagNameMergeResult.None, db.TagNameMerge);
        Assert.True(await db.ReadAsync(IndexExists));
    }

    private static bool IndexExists(SqliteConnection conn) =>
        Scalar(conn, $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = '{TagNames.UniqueIndexName}'") == 1;

    private static void InsertTag(SqliteConnection conn, string id, string name, int priority = 0, string? color = null, bool starred = false)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Tag (Id, Name, Priority, Slug, Color, IsStarred) VALUES ($id, $name, $p, $name, $c, $s)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$p", priority);
        cmd.Parameters.AddWithValue("$c", (object?)color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", starred ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    private static void Assign(SqliteConnection conn, string assetId, string tagId, string source) =>
        Exec(conn, $"INSERT INTO AssetTag (AssetId, TagId, Source) VALUES ('{assetId}', '{tagId}', '{source}')");

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string? Text(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() as string;
    }
}
