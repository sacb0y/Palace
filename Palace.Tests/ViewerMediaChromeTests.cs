using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ViewerMediaChromeTests
{
    [Fact]
    public void AllowsMediaFullWindow_IsAlwaysFalse() =>
        Assert.False(ViewerMediaChrome.AllowsMediaFullWindow);

    [Fact]
    public void ShouldRestorePresenter_OnlyWhenVideoFlippedMaximize()
    {
        Assert.True(ViewerMediaChrome.ShouldRestorePresenter(false, true));
        Assert.False(ViewerMediaChrome.ShouldRestorePresenter(true, true));
        Assert.False(ViewerMediaChrome.ShouldRestorePresenter(false, false));
    }

    [Fact]
    public void ClampGalleryWindowSize_StaysUnderWorkArea()
    {
        var (w, h) = ViewerMediaChrome.ClampGalleryWindowSize(2422, 1550, 2560, 1600);
        Assert.True(w <= (int)(2560 * 0.90));
        Assert.True(h <= (int)(1600 * 0.90));
        Assert.True(w >= 320);
        Assert.True(h >= 240);
    }
}
