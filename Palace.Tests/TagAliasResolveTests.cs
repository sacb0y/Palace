using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>T4: pure alias→ideal resolve + cycle / n→1 guards (no DB).</summary>
public sealed class TagAliasResolveTests
{
    [Fact]
    public void Ideal_ReturnsSelf_WhenNotAnAlias()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["alias"] = "ideal"
        };
        Assert.Equal("ideal", TagAliasResolve.Ideal("ideal", map));
        Assert.Equal("other", TagAliasResolve.Ideal("other", map));
    }

    [Fact]
    public void Ideal_ResolvesTransitively()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a"] = "b",
            ["b"] = "c"
        };
        Assert.Equal("c", TagAliasResolve.Ideal("a", map));
        Assert.Equal("c", TagAliasResolve.Ideal("b", map));
        Assert.Equal("c", TagAliasResolve.Ideal("c", map));
    }

    [Fact]
    public void Ideal_StopsBeforeLooping_OnCycle()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a"] = "b",
            ["b"] = "a"
        };
        // Walk a→b→a: stops at b (seen already contains a).
        Assert.Equal("b", TagAliasResolve.Ideal("a", map));
    }

    [Fact]
    public void CanAdd_RejectsSelf()
    {
        Assert.False(TagAliasResolve.CanAdd("x", "x", new Dictionary<string, string>()));
    }

    [Fact]
    public void CanAdd_RejectsNTo1Clash()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["alias"] = "ideal-a"
        };
        Assert.False(TagAliasResolve.CanAdd("alias", "ideal-b", map));
        Assert.True(TagAliasResolve.CanAdd("alias", "ideal-a", map));
    }

    [Fact]
    public void CanAdd_RejectsCycle()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["b"] = "a"
        };
        // a→b would close a↔b
        Assert.False(TagAliasResolve.CanAdd("a", "b", map));
        Assert.True(TagAliasResolve.CanAdd("c", "a", map));
    }

    [Fact]
    public void WouldCycle_DetectsTransitiveLoop()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["b"] = "c",
            ["c"] = "a"
        };
        Assert.True(TagAliasResolve.WouldCycle("a", "b", map));
        Assert.False(TagAliasResolve.WouldCycle("d", "b", map));
    }
}
