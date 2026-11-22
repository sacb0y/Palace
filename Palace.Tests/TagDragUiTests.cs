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
        Assert.False(TagDragUi.ShouldCapturePointerForPressChrome(canDrag: true));
        Assert.True(TagDragUi.ShouldCapturePointerForPressChrome(canDrag: false));
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
    public void PreArmsCanDragOnMouseHover_MouseOnly()
    {
        Assert.True(TagDragUi.PreArmsCanDragOnMouseHover(isMouse: true));
        Assert.False(TagDragUi.PreArmsCanDragOnMouseHover(isMouse: false));
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

    [Fact]
    public void ProbeLogFileEnvVar_IsStableForHarness()
    {
        Assert.Equal("PALACE_TAG_DRAG_LOG_FILE", TagDragUi.ProbeLogFileEnvVar);
        Assert.Equal("PALACE_TAG_DRAG_LOG", TagDragUi.ProbeEnvVar);
    }

    [Fact]
    public void WriteProbe_AppendsToLogFileWhenEnvSet()
    {
        var path = Path.Combine(Path.GetTempPath(), "palace-tagdrag-test-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            Environment.SetEnvironmentVariable(TagDragUi.ProbeLogFileEnvVar, path);
            TagDragUi.ProbeEnabledOverride = true;
            TagDragUi.ResetProbe();
            TagDragUi.LogDragStarting("chip1", 2);
            Assert.True(File.Exists(path));
            var text = File.ReadAllText(path);
            Assert.Contains("Palace.TagDrag DragStarting", text, StringComparison.Ordinal);
            Assert.Contains("count=2", text, StringComparison.Ordinal);
        }
        finally
        {
            TagDragUi.ProbeEnabledOverride = null;
            Environment.SetEnvironmentVariable(TagDragUi.ProbeLogFileEnvVar, null);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
