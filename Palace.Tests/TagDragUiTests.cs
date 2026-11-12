using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class TagDragUiTests
{
    [Fact]
    public void DragUi_RequiresSynchronousContent_NoDeferralAwait()
    {
        Assert.True(TagDragUi.RequiresSynchronousDragUiContent);
        Assert.Equal(0.4, TagDragUi.DragSourceOpacity);
    }

    [Fact]
    public void GhostCacheKey_MatchesPayload()
    {
        var key = TagDragUi.GhostCacheKey("knight", 3);
        Assert.True(TagDragUi.TryGetCachedGhostKey(key, "knight", 3));
        Assert.False(TagDragUi.TryGetCachedGhostKey(key, "knight", 1));
        Assert.False(TagDragUi.TryGetCachedGhostKey(key, "creature", 3));
        Assert.False(TagDragUi.TryGetCachedGhostKey(null, "knight", 3));
    }

    [Fact]
    public void DropChrome_ValidVsRefuse()
    {
        Assert.True(TagDragUi.ShowsValidDropChrome(isUserGroupRow: true, allowDrop: true));
        Assert.False(TagDragUi.ShowsValidDropChrome(isUserGroupRow: true, allowDrop: false));
        Assert.False(TagDragUi.ShowsValidDropChrome(isUserGroupRow: false, allowDrop: true));
        Assert.True(TagDragUi.ShowsRefuseDropChrome(isUserGroupRow: true, allowDrop: false));
        Assert.False(TagDragUi.ShowsRefuseDropChrome(isUserGroupRow: true, allowDrop: true));
    }

    [Fact]
    public void DragLeave_ClearsOnlyOutsideRowBounds()
    {
        Assert.False(TagDragUi.ShouldClearDropChromeOnLeave(10, 10, 200, 34));
        Assert.True(TagDragUi.ShouldClearDropChromeOnLeave(-1, 10, 200, 34));
        Assert.True(TagDragUi.ShouldClearDropChromeOnLeave(10, 40, 200, 34));
        Assert.True(TagDragUi.ShouldClearDropChromeOnLeave(0, 0, 0, 0));
    }

    [Fact]
    public void MultiBadge_Dg01b()
    {
        Assert.Equal("", TagMembershipAdd.MultiBadge(1));
        Assert.Equal("3 tags", TagMembershipAdd.MultiBadge(3));
    }
}
