using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class NaturalStringComparerTests
{
    [Fact]
    public void Orders_numbers_by_value_ignoring_case()
    {
        string[] input = ["img10", "IMG2", "img1", "img02b", "alpha", "Img002"];
        var sorted = input.OrderBy(s => s, NaturalStringComparer.Instance).ToList();
        Assert.Equal(["alpha", "img1", "IMG2", "Img002", "img02b", "img10"], sorted);
    }

    [Fact]
    public void Handles_nulls_and_big_numbers()
    {
        Assert.True(NaturalStringComparer.Instance.Compare(null, "a") < 0);
        Assert.True(NaturalStringComparer.Instance.Compare("a99999999999999999999", "a100000000000000000000") < 0);
        Assert.Equal(0, NaturalStringComparer.Instance.Compare("x", "x"));
    }
}
