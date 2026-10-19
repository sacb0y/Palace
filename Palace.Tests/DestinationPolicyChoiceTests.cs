using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class DestinationPolicyChoiceTests
{
    [Theory]
    [InlineData("InSource", 0)]
    [InlineData("Destination", 1)]
    [InlineData("destination", 1)]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("nope", 0)]
    public void IndexOf_StoredName(string? stored, int expected) =>
        Assert.Equal(expected, DestinationPolicyChoice.IndexOf(stored));

    [Fact]
    public void NameAt_RoundTripsEveryPolicy()
    {
        foreach (var policy in Enum.GetValues<DestinationPolicy>())
        {
            var name = policy.ToString();
            Assert.Equal(name, DestinationPolicyChoice.NameAt(DestinationPolicyChoice.IndexOf(name)));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void NameAt_ClearedOrOutOfRange_IsNull(int index) =>
        Assert.Null(DestinationPolicyChoice.NameAt(index));
}