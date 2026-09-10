using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class RecentTagsTests
{
    [Fact]
    public void Remember_MovesExistingToFront_AndCaps()
    {
        var current = Enumerable.Range(1, 20).Select(i => $"t{i}").ToList();
        var next = RecentTags.Remember(current, "t21");
        Assert.Equal("t21", next[0]);
        Assert.Equal(20, next.Count);
        Assert.DoesNotContain("t20", next);
        var moved = RecentTags.Remember(next, "t5");
        Assert.Equal("t5", moved[0]);
        Assert.Equal(1, moved.Count(id => id == "t5"));
    }

    [Fact]
    public void ParseAndSerialize_RoundTrip()
    {
        var ids = RecentTags.Parse("a, b;c\n a");
        Assert.Equal(["a", "b", "c"], ids);
        Assert.Equal("a,b,c", RecentTags.Serialize(ids));
    }
}
