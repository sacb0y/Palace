using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagBoardShellTests
{
    [Fact]
    public void NewTagsHeader_UsesGroupOrUngrouped()
    {
        Assert.Equal("New tags in Medium", TagBoardShell.NewTagsHeader("Medium", ungrouped: false));
        Assert.Equal("New tags in Ungrouped", TagBoardShell.NewTagsHeader(null, ungrouped: true));
        Assert.Equal("New tags in Ungrouped", TagBoardShell.NewTagsHeader("  ", ungrouped: false));
    }

    [Fact]
    public void NextRecentlyCreated_StampsThenClears()
    {
        var stamped = TagBoardShell.NextRecentlyCreated([], ["a", "b"]);
        Assert.Equal(["a", "b"], stamped.OrderBy(x => x).ToArray());

        var faded = TagBoardShell.NextRecentlyCreated(stamped, justCreated: null);
        Assert.Empty(faded);
    }

    [Fact]
    public void NavKeys_RecognizePinnedSections()
    {
        Assert.True(TagBoardShell.IsNavSection("all"));
        Assert.True(TagBoardShell.IsGroupsHeader("groups-header"));
        Assert.True(TagBoardShell.IsUserGroupKey("medium"));
        Assert.False(TagBoardShell.IsUserGroupKey("recent"));
    }
}
