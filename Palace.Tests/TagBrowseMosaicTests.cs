using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagBrowseMosaicTests
{
    private sealed class Node
    {
        public string? Id { get; init; }
        public List<Node> Children { get; } = [];
    }

    [Fact]
    public void Flatten_SkipsBucketsAndKeepsDepthFirstTags()
    {
        var ungrouped = new Node { Id = null };
        ungrouped.Children.Add(new Node { Id = "zeta" });
        var group = new Node { Id = "places" };
        group.Children.Add(new Node { Id = "beach" });

        var flat = TagBrowseMosaic.Flatten(
            [group, ungrouped],
            node => node.Id,
            node => node.Children);

        Assert.Equal(new[] { "places", "beach", "zeta" }, flat.Select(n => n.Id).ToArray());
    }

    [Fact]
    public void Flatten_EmptyTreeIsEmpty() =>
        Assert.Empty(TagBrowseMosaic.Flatten(
            Array.Empty<Node>(),
            node => node.Id,
            node => node.Children));
}
