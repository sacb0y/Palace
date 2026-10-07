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

    [Fact]
    public void LooksLikeDeletedDirectory_NeedsNoExtensionAndNoAsset()
    {
        Assert.True(MosaicDelete.LooksLikeDeletedDirectory(@"C:\lib\shots", hadMatchingAsset: false));
        Assert.False(MosaicDelete.LooksLikeDeletedDirectory(@"C:\lib\a.png", hadMatchingAsset: false));
        Assert.False(MosaicDelete.LooksLikeDeletedDirectory(@"C:\lib\shots", hadMatchingAsset: true));
        Assert.False(MosaicDelete.LooksLikeDeletedDirectory("", hadMatchingAsset: false));
    }

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
}
