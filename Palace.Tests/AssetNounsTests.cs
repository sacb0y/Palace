using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class AssetNounsTests
{
    [Theory]
    [InlineData(0, "0 items")]
    [InlineData(1, "1 item")]
    [InlineData(3, "3 items")]
    public void Count_Pluralizes(int n, string expected) =>
        Assert.Equal(expected, AssetNouns.Count(n));

    [Fact]
    public void WithThisTag_UsesItemsNotImages()
    {
        Assert.Equal("1 item with this tag", AssetNouns.WithThisTag(1));
        Assert.Equal("12 items with this tag", AssetNouns.WithThisTag(12));
    }
}
