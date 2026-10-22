using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SwipeZoomRestoreTests
{
    [Fact]
    public void SaveRestoreForget_AndWorthSaving()
    {
        var store = new SwipeZoomRestore();
        var zoomed = new SwipeZoomRestore.Snapshot(ImageScaling.Actual, 2.5, 40, 10);
        Assert.True(SwipeZoomRestore.IsWorthSaving(zoomed));
        Assert.False(SwipeZoomRestore.IsWorthSaving(
            new SwipeZoomRestore.Snapshot(ImageScaling.Fit, 1, 0, 0)));

        store.Save("C:\\a.jpg", zoomed);
        Assert.Equal(1, store.Count);
        Assert.True(store.TryRestore("C:\\a.jpg", out var got));
        Assert.Equal(ImageScaling.Actual, got.Scaling);
        Assert.Equal(2.5, got.PinchZoom, 3);
        Assert.Equal(40, got.OffsetX, 3);

        // Going back after a commit restores prior zoom/pan.
        store.Save("C:\\b.jpg", new SwipeZoomRestore.Snapshot(ImageScaling.Fit, 1, 0, 0));
        Assert.True(store.TryRestore("C:\\a.jpg", out _));

        store.Forget("C:\\a.jpg");
        Assert.False(store.TryRestore("C:\\a.jpg", out _));
        store.Clear();
        Assert.Equal(0, store.Count);
    }
}
