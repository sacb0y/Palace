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
            File.WriteAllBytes(localDb, new byte[64_000]);

            var resolved = CatalogStore.ResolveDbPath(local, settingsRoot: null, overrideRoot: durable);
            Assert.Equal(CatalogStore.DbPathIn(durable), resolved);
            Assert.True(File.Exists(resolved));
            Assert.True(CatalogStore.MigratedFromLocalState);
            Assert.Equal(64_000, new FileInfo(resolved!).Length);
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
            File.WriteAllBytes(Path.Combine(local, CatalogStore.DbFileName), new byte[64_000]);
            File.WriteAllBytes(Path.Combine(durable, CatalogStore.DbFileName), new byte[20_000]);

            var resolved = CatalogStore.ResolveDbPath(local, settingsRoot: null, overrideRoot: durable);
            Assert.Equal(CatalogStore.DbPathIn(durable), resolved);
            Assert.False(CatalogStore.MigratedFromLocalState);
            Assert.Equal(20_000, new FileInfo(resolved!).Length);
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
}
