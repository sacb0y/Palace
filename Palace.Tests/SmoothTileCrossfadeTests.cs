using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothTileCrossfadeTests
{
    [Fact]
    public void NewTile_RisesOverDuration()
    {
        var xf = new SmoothTileCrossfade(durationMs: 100f);
        var id = new SmoothTileId(0, 0, 0);
        var a = xf.Tick(id, dtMs: 50f, resident: true);
        Assert.InRange(a, 0.49f, 0.51f);
        var b = xf.Tick(id, dtMs: 50f, resident: true);
        Assert.Equal(1f, b);
        xf.EndFrame();
    }

    [Fact]
    public void SeedOpaque_StartsAtOne()
    {
        var xf = new SmoothTileCrossfade(durationMs: 100f);
        var id = new SmoothTileId(0, 0, 0);
        var a = xf.Tick(id, dtMs: 16f, resident: true, seedOpaque: true);
        Assert.Equal(1f, a);
    }

    [Fact]
    public void EndFrame_DropsUnseen()
    {
        var xf = new SmoothTileCrossfade();
        var keep = new SmoothTileId(0, 0, 0);
        var drop = new SmoothTileId(0, 1, 0);
        _ = xf.Tick(keep, 100f, true);
        _ = xf.Tick(drop, 100f, true);
        xf.EndFrame();

        // Next frame only touches keep.
        _ = xf.Tick(keep, 16f, true);
        xf.EndFrame();
        Assert.True(xf.TryGet(keep, out _));
        Assert.False(xf.TryGet(drop, out _));
    }
}
