using Microsoft.Data.Sqlite;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class CatalogStoreTests
{
    [Fact]
    public void NormalizeRoot_TrimsAndRejectsEmpty()
    {
        Assert.Null(CatalogStore.NormalizeRoot(null));
        Assert.Null(CatalogStore.NormalizeRoot("  "));
        Assert.Null(CatalogStore.ParsePath(""));
        var path = CatalogStore.NormalizeRoot(@" C:\PalaceData\ ");
        Assert.NotNull(path);
        Assert.Equal(Path.GetFullPath(@"C:\PalaceData"), path);
    }

    [Fact]
    public void LooksLikeCatalog_DetectsNonEmptyDb()
    {
        var dir = Path.Combine(Path.GetTempPath(), "palace-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var empty = Path.Combine(dir, "empty.db");
            File.WriteAllBytes(empty, new byte[4096]);
            Assert.False(CatalogStore.LooksLikeCatalog(empty));

            var fat = Path.Combine(dir, "fat.db");
            File.WriteAllBytes(fat, new byte[32_768]);
            Assert.True(CatalogStore.LooksLikeCatalog(fat));

            Assert.False(CatalogStore.LooksLikeCatalog(Path.Combine(dir, "missing.db")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveDbPath_MigratesLocalStateWhenDurableEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "palace-resolve-" + Guid.NewGuid().ToString("N"));
        var local = Path.Combine(root, "local");
        var durable = Path.Combine(root, "durable");
        Directory.CreateDirectory(local);
        try
        {
            var localDb = Path.Combine(local, CatalogStore.DbFileName);
            MakeDb(localDb, "local");

            var resolved = CatalogStore.ResolveDbPath(local, settingsRoot: null, overrideRoot: durable);
            Assert.Equal(CatalogStore.DbPathIn(durable), resolved);
            Assert.True(File.Exists(resolved));
            Assert.True(CatalogStore.MigratedFromLocalState);
            Assert.Equal("local", ReadMarker(resolved!));
            Assert.True(File.Exists(localDb)); // LocalState left untouched
            var backups = Directory.GetDirectories(Path.Combine(durable, CatalogStore.BackupsFolderName));
            Assert.Single(backups);
            Assert.True(File.Exists(Path.Combine(backups[0], CatalogStore.DbFileName)));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void ResolveDbPath_PrefersExistingDurableOverLocalState()
    {
        var root = Path.Combine(Path.GetTempPath(), "palace-prefer-" + Guid.NewGuid().ToString("N"));
        var local = Path.Combine(root, "local");
        var durable = Path.Combine(root, "durable");
        Directory.CreateDirectory(local);
        Directory.CreateDirectory(durable);
        try
        {
            MakeDb(Path.Combine(local, CatalogStore.DbFileName), "local", pad: 64_000);
            MakeDb(Path.Combine(durable, CatalogStore.DbFileName), "durable");

            var resolved = CatalogStore.ResolveDbPath(local, settingsRoot: null, overrideRoot: durable);
            Assert.Equal(CatalogStore.DbPathIn(durable), resolved);
            Assert.False(CatalogStore.MigratedFromLocalState);
            Assert.Equal("durable", ReadMarker(resolved!));
            Assert.NotNull(CatalogStore.UnmergedLocalStateBackup);
            Assert.Equal("local", ReadMarker(Path.Combine(CatalogStore.UnmergedLocalStateBackup!, CatalogStore.DbFileName)));

            // A second launch does not pile up duplicate backups.
            CatalogStore.ResolveDbPath(local, settingsRoot: null, overrideRoot: durable);
            Assert.Single(Directory.GetDirectories(Path.Combine(durable, CatalogStore.BackupsFolderName)));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void CopyCatalogFiles_CopiesWalSidecars()
    {
        var root = Path.Combine(Path.GetTempPath(), "palace-copy-" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "src");
        var dst = Path.Combine(root, "dst");
        Directory.CreateDirectory(src);
        try
        {
            File.WriteAllText(Path.Combine(src, CatalogStore.DbFileName), "db");
            File.WriteAllText(Path.Combine(src, CatalogStore.DbFileName + "-wal"), "wal");
            CatalogStore.CopyCatalogFiles(src, dst);
            Assert.True(File.Exists(Path.Combine(dst, CatalogStore.DbFileName)));
            Assert.True(File.Exists(Path.Combine(dst, CatalogStore.DbFileName + "-wal")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void SnapshotDatabase_RefusesToOverwrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "palace-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var src = Path.Combine(root, "a.db");
            var dst = Path.Combine(root, "b.db");
            MakeDb(src, "a");
            MakeDb(dst, "b");
            Assert.Throws<IOException>(() => CatalogStore.SnapshotDatabase(src, dst));
            Assert.Equal("b", ReadMarker(dst));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void ResolveDbPath_MovesStaleSidecarsBeforeMigrating()
    {
        var root = Path.Combine(Path.GetTempPath(), "palace-stale-" + Guid.NewGuid().ToString("N"));
        var local = Path.Combine(root, "local");
        var durable = Path.Combine(root, "durable");
        Directory.CreateDirectory(local);
        Directory.CreateDirectory(durable);
        try
        {
            MakeDb(Path.Combine(local, CatalogStore.DbFileName), "local");
            File.WriteAllText(Path.Combine(durable, CatalogStore.DbFileName + "-wal"), "stale");
            var resolved = CatalogStore.ResolveDbPath(local, settingsRoot: null, overrideRoot: durable);
            Assert.False(File.Exists(Path.Combine(durable, CatalogStore.DbFileName + "-wal")) &&
                         File.ReadAllText(Path.Combine(durable, CatalogStore.DbFileName + "-wal")) == "stale");
            Assert.Equal("local", ReadMarker(resolved));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static void MakeDb(string path, string marker, int pad = 0)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE M(v TEXT, pad BLOB); INSERT INTO M VALUES ($v, zeroblob($p));";
        cmd.Parameters.AddWithValue("$v", marker);
        cmd.Parameters.AddWithValue("$p", pad);
        cmd.ExecuteNonQuery();
    }

    private static string ReadMarker(string path)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT v FROM M";
        return (string)cmd.ExecuteScalar()!;
    }
}
