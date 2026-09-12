using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagSelectionTests
{
    [Fact]
    public void ShouldApplyLoadedName_NewTag_AlwaysApplies()
    {
        Assert.True(TagSelection.ShouldApplyLoadedName("Sonic", "Character", "Sonic", "sonic", "character"));
        Assert.True(TagSelection.ShouldApplyLoadedName("", "Character", "Sonic", "sonic", "character"));
        Assert.True(TagSelection.ShouldApplyLoadedName("Sonic", "Character", null, null, "character"));
    }

    [Fact]
    public void ShouldApplyLoadedName_SameTagUnedited_Applies()
    {
        Assert.True(TagSelection.ShouldApplyLoadedName("Sonic", "Sonic", "Sonic", "sonic", "sonic"));
    }

    [Fact]
    public void ShouldApplyLoadedName_SameTagEditedOrCleared_KeepsUserInput()
    {
        Assert.False(TagSelection.ShouldApplyLoadedName("Son", "Sonic", "Sonic", "sonic", "sonic"));
        Assert.False(TagSelection.ShouldApplyLoadedName("Tails", "Sonic", "Sonic", "sonic", "sonic"));
        Assert.False(TagSelection.ShouldApplyLoadedName("", "Sonic", "Sonic", "sonic", "sonic"));
        Assert.False(TagSelection.ShouldApplyLoadedName(null, "Sonic", "Sonic", "sonic", "sonic"));
    }
}
