using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class FolderTreeBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "palace-tree-" + Guid.NewGuid().ToString("N"));

    public FolderTreeBuilderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Dir(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    private List<FolderTreeEntry> Build(FolderTreeSort sort = FolderTreeSort.NameAsc, params (string Source, string Path)[] assets) =>
        FolderTreeBuilder.Build([new FolderTreeSource("s1", _root, true)], assets, FolderTreeBuilder.ListDiskChildren, sort);

    [Fact]
    public void Empty_folders_on_disk_show()
    {
        Dir("Empty");
        Dir("A", "Deep", "Deeper");
        var root = Assert.Single(Build());
        Assert.True(root.IsRoot);
        Assert.Equal(["A", "Empty"], root.Children.Select(c => c.Name));
        Assert.Equal("Deeper", root.Children[0].Children.Single().Children.Single().Name);
    }

    [Fact]
    public void Counts_include_sub_folders()
    {
        var a = Dir("A");
        var b = Dir("A", "B");
        var root = Assert.Single(Build(FolderTreeSort.NameAsc,
            ("s1", Path.Combine(a, "1.png")),
            ("s1", Path.Combine(b, "2.png")),
            ("s1", Path.Combine(b, "3.png")),
            ("s1", Path.Combine(_root, "top.png"))));
        Assert.Equal(4, root.AssetCount);
        Assert.Equal(3, root.Children.Single().AssetCount);
        Assert.Equal(2, root.Children.Single().Children.Single().AssetCount);
    }

    [Fact]
    public void Catalog_folders_missing_on_disk_still_show_and_dot_folders_hide()
    {
        Dir(".git");
        Dir("$RECYCLE.BIN");
        var root = Assert.Single(Build(FolderTreeSort.NameAsc, ("s1", Path.Combine(_root, "Gone", "x.png"))));
        Assert.Equal(["Gone"], root.Children.Select(c => c.Name));
    }

    [Fact]
    public void Assets_of_other_sources_or_outside_the_root_are_ignored()
    {
        var root = Assert.Single(Build(FolderTreeSort.NameAsc,
            ("s2", Path.Combine(_root, "Other", "x.png")),
            ("s1", Path.Combine(Path.GetTempPath(), "elsewhere", "y.png"))));
        Assert.Empty(root.Children);
        Assert.Equal(0, root.AssetCount);
    }

    [Fact]
    public void Sorts_name_natural_desc_count_and_newest()
    {
        var two = Dir("Shot 2");
        var ten = Dir("Shot 10");
        var one = Dir("shot 1");
        Directory.SetLastWriteTimeUtc(one, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Directory.SetLastWriteTimeUtc(two, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Directory.SetLastWriteTimeUtc(ten, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        (string, string)[] assets = [("s1", Path.Combine(ten, "a.png")), ("s1", Path.Combine(ten, "b.png")), ("s1", Path.Combine(one, "c.png"))];

        Assert.Equal(["shot 1", "Shot 2", "Shot 10"], Build(FolderTreeSort.NameAsc, assets)[0].Children.Select(c => c.Name));
        Assert.Equal(["Shot 10", "Shot 2", "shot 1"], Build(FolderTreeSort.NameDesc, assets)[0].Children.Select(c => c.Name));
        Assert.Equal(["Shot 10", "shot 1", "Shot 2"], Build(FolderTreeSort.MostItems, assets)[0].Children.Select(c => c.Name));
        Assert.Equal(["Shot 2", "Shot 10", "shot 1"], Build(FolderTreeSort.Newest, assets)[0].Children.Select(c => c.Name));
    }

    [Fact]
    public void Cloud_api_sources_do_not_list_disk()
    {
        Dir("OnDisk");
        var calls = 0;
        var roots = FolderTreeBuilder.Build(
            [new FolderTreeSource("s1", _root, ScanDisk: false)],
            [("s1", Path.Combine(_root, "Api", "x.png"))],
            dir => { calls++; return FolderTreeBuilder.ListDiskChildren(dir); },
            FolderTreeSort.NameAsc);
        Assert.Equal(0, calls);
        Assert.Equal(["Api"], roots[0].Children.Select(c => c.Name));
    }

    [Fact]
    public void Disk_listing_is_capped()
    {
        var listed = 0;
        IEnumerable<FolderTreeDir> Fake(string dir)
        {
            for (var i = 0; i < 10; i++)
            {
                listed++;
                yield return new FolderTreeDir(Path.Combine(dir, "d" + i), DateTime.UtcNow);
            }
        }

        var roots = FolderTreeBuilder.Build([new FolderTreeSource("s1", _root, true)], [], Fake, FolderTreeSort.NameAsc);
        var total = 0;
        void Count(FolderTreeEntry e)
        {
            foreach (var c in e.Children)
            {
                total++;
                Count(c);
            }
        }

        Count(roots[0]);
        Assert.Equal(FolderTreeBuilder.MaxDirectoriesPerSource, total);
    }

    [Fact]
    public void Symlinked_folders_are_skipped()
    {
        var target = Dir("Real");
        var link = Path.Combine(_root, "Link");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return; // Windows without developer mode cannot create links
        }

        Assert.Equal(["Real"], Build()[0].Children.Select(c => c.Name));
    }

    [Theory]
    [InlineData("NameDesc", FolderTreeSort.NameDesc)]
    [InlineData("bogus", FolderTreeSort.NameAsc)]
    [InlineData(null, FolderTreeSort.NameAsc)]
    [InlineData("99", FolderTreeSort.NameAsc)]
    public void Parse_falls_back_to_name(string? value, FolderTreeSort expected) =>
        Assert.Equal(expected, FolderTreeBuilder.Parse(value));

    [Fact]
    public void Catalog_files_under_skipped_folders_count_toward_the_nearest_visible_folder()
    {
        var a = Dir("A");
        var root = Assert.Single(FolderTreeBuilder.Build(
            [new FolderTreeSource("s1", _root, true)],
            [
                ("s1", Path.Combine(a, ".git", "objects", "x.png")),
                ("s1", Path.Combine(a, "$RECYCLE", "y.png")),
                ("s1", Path.Combine(a, "Hidden", "z.png")),
                ("s1", Path.Combine(a, "Shown", "w.png"))
            ],
            FolderTreeBuilder.ListDiskChildren,
            FolderTreeSort.NameAsc,
            dir => dir.EndsWith("Hidden", StringComparison.Ordinal)));

        var folderA = root.Children.Single();
        Assert.Equal(["Shown"], folderA.Children.Select(c => c.Name));
        Assert.Equal(4, folderA.AssetCount);
        Assert.Equal(4, root.AssetCount);
    }

    [Fact]
    public void Hidden_check_is_not_used_for_cloud_api_sources()
    {
        var called = false;
        var root = Assert.Single(FolderTreeBuilder.Build(
            [new FolderTreeSource("s1", _root, false)],
            [("s1", Path.Combine(_root, "Cloud", "a.png"))],
            FolderTreeBuilder.ListDiskChildren,
            FolderTreeSort.NameAsc,
            _ => called = true));

        Assert.False(called);
        Assert.Equal(["Cloud"], root.Children.Select(c => c.Name));
    }

    [Fact]
    public void IsHiddenOnDisk_is_false_for_missing_and_plain_folders()
    {
        Assert.False(FolderTreeBuilder.IsHiddenOnDisk(Path.Combine(_root, "missing")));
        Assert.False(FolderTreeBuilder.IsHiddenOnDisk(Dir("Plain")));
    }

    [Fact]
    public void RebasePaths_moves_keys_under_the_renamed_folder_only()
    {
        var old = Path.Combine(_root, "Old");
        var renamed = Path.Combine(_root, "New");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            old,
            Path.Combine(old, "Child"),
            Path.Combine(_root, "Older"),
            Path.Combine(_root, "Other")
        };

        FolderTreeBuilder.RebasePaths(keys, old, renamed);

        Assert.Equal(
            new[] { renamed, Path.Combine(renamed, "Child"), Path.Combine(_root, "Older"), Path.Combine(_root, "Other") }.OrderBy(k => k),
            keys.OrderBy(k => k));
    }
}
