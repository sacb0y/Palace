using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagSelectionTests
{
    [Fact]
    public void ShouldApplyLoadedName_EmptyOrSame_Applies()
    {
        Assert.True(TagSelection.ShouldApplyLoadedName(null, "Sonic"));
        Assert.True(TagSelection.ShouldApplyLoadedName("", "Sonic"));
        Assert.True(TagSelection.ShouldApplyLoadedName("Sonic", "Sonic"));
    }

    [Fact]
    public void ShouldApplyLoadedName_EditedText_KeepsUserInput()
    {
        Assert.False(TagSelection.ShouldApplyLoadedName("Son", "Sonic"));
        Assert.False(TagSelection.ShouldApplyLoadedName("Tails", "Sonic"));
    }
}
