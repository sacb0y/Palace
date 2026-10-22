using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class MosaicDeleteTests
{
    private sealed class Tile
    {
        public bool Header { get; init; }
        public string? Id { get; init; }
        public string Name { get; init; } = "";
    }

    [Theory]
    [InlineData(WatcherChangeKinds.Changed, true, false)]
    [InlineData(WatcherChangeKinds.Deleted, true, false)]
    [InlineData(WatcherChangeKinds.Created, true, true)]
    [InlineData(WatcherChangeKinds.Renamed, true, true)]
    [InlineData(WatcherChangeKinds.Created, false, false)]
    public void ShouldIndexDirectory_OnlyCreatedOrRenamedWhilePresent(
        WatcherChangeKinds change,
        bool exists,
        bool expected) =>
        Assert.Equal(expected, MosaicDelete.ShouldIndexDirectory(change, exists));

    [Theory]
    [InlineData(@"C:\lib\shots", false, true)]
    [InlineData(@"C:\lib\shots.backup", false, true)]
    [InlineData(@"C:\lib\.cache", false, true)]
    [InlineData(@"C:\lib\a.png", false, false)]
    [InlineData(@"C:\lib\clip.mp4", false, false)]
    [InlineData(@"C:\lib\shots", true, false)]
    [InlineData("", false, false)]
    public void LooksLikeDeletedDirectory_UsesCatalogExtNotGetExtension(
        string path,
        bool hadAsset,
        bool expected) =>
        Assert.Equal(expected, MosaicDelete.LooksLikeDeletedDirectory(path, hadAsset));

    [Fact]
    public void PruneItems_RemovesAssetsAndEmptyHeaders()
    {
        var items = new List<Tile>
        {
            new() { Header = true, Name = "A" },
            new() { Id = "1", Name = "one" },
            new() { Id = "2", Name = "two" },
            new() { Header = true, Name = "B" },
            new() { Id = "3", Name = "three" },
        };

        var pruned = MosaicDelete.PruneItems(
            items,
            t => t.Header,
            t => t.Id,
            ["1", "2"]);

        Assert.Equal(2, pruned.Count);
        Assert.True(pruned[0].Header);
        Assert.Equal("B", pruned[0].Name);
        Assert.Equal("3", pruned[1].Id);
    }

    [Fact]
    public void PruneItems_KeepsHeaderWhenGroupStillHasTiles()
    {
        var items = new List<Tile>
        {
            new() { Header = true, Name = "A" },
            new() { Id = "1", Name = "one" },
            new() { Id = "2", Name = "two" },
        };

        var pruned = MosaicDelete.PruneItems(items, t => t.Header, t => t.Id, ["1"]);
        Assert.Equal(2, pruned.Count);
        Assert.True(pruned[0].Header);
        Assert.Equal("2", pruned[1].Id);
    }

    [Fact]
    public void ExceptIds_FiltersCatalogRows()
    {
        var assets = new[] { ("a", 1), ("b", 2), ("c", 3) };
        var kept = MosaicDelete.ExceptIds(assets, x => x.Item1, ["b", "c"]);
        Assert.Equal([("a", 1)], kept);
    }

    [Fact]
    public void ChunkIds_RespectsSizeAndDedupes()
    {
        var ids = Enumerable.Range(0, 5).Select(i => $"id{i}").Concat(["id0", "id1", ""]);
        var chunks = MosaicDelete.ChunkIds(ids, chunkSize: 2).ToList();
        Assert.Equal(3, chunks.Count);
        Assert.Equal(["id0", "id1"], chunks[0]);
        Assert.Equal(["id2", "id3"], chunks[1]);
        Assert.Equal(["id4"], chunks[2]);
    }

    [Fact]
    public void DeleteInsideCollapsedSection_PrunesOrderAndReviewTargets()
    {
        // Ordered: A[a1], B(collapsed)[b1,b2], C[c1]. Delete b1.
        var a = new CountTile { Header = true, Name = "A", Count = 1 };
        var a1 = new CountTile { Id = "a1", Name = "a1" };
        var b = new CountTile { Header = true, Name = "B", Count = 2, Collapsed = true };
        var b1 = new CountTile { Id = "b1", Name = "b1" };
        var b2 = new CountTile { Id = "b2", Name = "b2" };
        var c = new CountTile { Header = true, Name = "C", Count = 1 };
        var c1 = new CountTile { Id = "c1", Name = "c1" };
        var ordered = new List<CountTile> { a, a1, b, b1, b2, c, c1 };

        var pruned = MosaicDelete.PruneItems(ordered, t => t.Header, t => t.Id, ["b1"]);
        MosaicDelete.RecomputeFolderItemCounts(pruned, t => t.Header, (t, n) => t.Count = n);

        Assert.Equal(6, pruned.Count);
        Assert.DoesNotContain(pruned, t => t.Id == "b1");
        Assert.Equal(1, b.Count); // was 2
        Assert.Contains(pruned, t => t.Id == "b2");

        var display = FolderSectionPrefs.FilterCollapsedDisplay(
            pruned, t => t.Header, t => t.Collapsed);
        Assert.DoesNotContain(display, t => t.Id == "b2"); // still collapsed
        Assert.Equal(["A", "a1", "B", "C", "c1"], display.Select(t => t.Name).ToArray());

        var review = MosaicDelete.OrderedNonHeaderTargets(pruned, t => t.Header);
        Assert.Equal(new[] { "a1", "b2", "c1" }, review.Select(t => t.Id!).ToArray());
    }

    [Fact]
    public void OrderedNonHeaderTargets_IsSelectAllAcrossCollapsed()
    {
        var ordered = new List<CountTile>
        {
            new() { Header = true, Name = "A" },
            new() { Id = "a1" },
            new() { Header = true, Name = "B", Collapsed = true },
            new() { Id = "b1" },
            new() { Id = "b2" },
        };
        var all = MosaicDelete.OrderedNonHeaderTargets(ordered, t => t.Header);
        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { "a1", "b1", "b2" }, all.Select(t => t.Id!).ToArray());
    }

    private sealed class CountTile
    {
        public bool Header { get; init; }
        public bool Collapsed { get; init; }
        public string? Id { get; init; }
        public string Name { get; init; } = "";
        public int Count { get; set; }
    }
}
