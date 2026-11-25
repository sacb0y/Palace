using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothGpuTileLruTests
{
    [Fact]
    public void CapacityFromVram_Clamps128to192()
    {
        Assert.Equal(128, SmoothGpuTileLru.CapacityFromVramBytes(0));
        Assert.Equal(128, SmoothGpuTileLru.CapacityFromVramBytes(SmoothTileMath.BytesPerTileRgba16F() * 50));
        // Huge VRAM still caps at 192
        Assert.Equal(192, SmoothGpuTileLru.CapacityFromVramBytes(64L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void Lru_EvictsUnpinned_KeepsCoarsePinned()
    {
        // capacity 3; coarse pin level 2 => L2+ never evicted
        var lru = new SmoothGpuTileLru(capacity: 3, coarsePinLevel: 2);
        Assert.True(lru.TryInsert(new SmoothTileId(0, 0, 0), out var s0, out _));
        Assert.True(lru.TryInsert(new SmoothTileId(0, 1, 0), out var s1, out _));
        Assert.True(lru.TryInsert(new SmoothTileId(2, 0, 0), out var s2, out _)); // pinned
        Assert.Equal(3, lru.ResidentCount);

        Assert.True(lru.TryInsert(new SmoothTileId(0, 2, 0), out _, out var evicted));
        Assert.NotNull(evicted);
        Assert.True(evicted!.Value.Level < 2);
        Assert.True(lru.TryGetSlot(new SmoothTileId(2, 0, 0), out var still));
        Assert.Equal(s2, still);
        Assert.False(lru.TryGetSlot(evicted.Value, out _));
        _ = s0;
        _ = s1;
    }

    [Fact]
    public void Touch_ProtectsRecentFromEviction()
    {
        var lru = new SmoothGpuTileLru(2, coarsePinLevel: 99);
        Assert.True(lru.TryInsert(new SmoothTileId(0, 0, 0), out _, out _));
        Assert.True(lru.TryInsert(new SmoothTileId(0, 1, 0), out _, out _));
        Assert.True(lru.TryGetSlot(new SmoothTileId(0, 0, 0), out _)); // touch first
        Assert.True(lru.TryInsert(new SmoothTileId(0, 2, 0), out _, out var evicted));
        Assert.Equal(new SmoothTileId(0, 1, 0), evicted);
    }

    [Fact]
    public void MemoryBudget_BytesPerTile()
    {
        Assert.Equal(512 * 512 * 8, SmoothTileMath.BytesPerTileRgba16F());
        var cache = new SmoothCpuTileCache();
        var buf = new byte[SmoothTileMath.BytesPerTileRgba16F()];
        cache.Set(new SmoothTileId(0, 0, 0), buf);
        Assert.Equal(SmoothTileMath.BytesPerTileRgba16F(), cache.ApproxBytes);
    }
}
