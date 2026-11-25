using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothGpuTileLruTests
{
    [Fact]
    public void CapacityFromVram_ClampsDown_FloorIsCoarseSet()
    {
        const int coarse = 40;
        // Tiny budget must not inflate to 128 — floor at coarse set.
        Assert.Equal(coarse, SmoothGpuTileLru.CapacityFromVramBytes(0, coarse));
        Assert.Equal(coarse, SmoothGpuTileLru.CapacityFromVramBytes(SmoothTileMath.BytesPerTileRgba16F() * 10, coarse));
        // Huge VRAM still caps at 192
        Assert.Equal(192, SmoothGpuTileLru.CapacityFromVramBytes(64L * 1024 * 1024 * 1024, coarse));
    }

    [Fact]
    public void SmallVramBudget_DoesNotBumpAboveBudgetUntilFloor()
    {
        const int coarse = 8;
        // budget/8 allows ~2 tiles → still floor at coarse=8
        var cap = SmoothGpuTileLru.CapacityFromVramBytes(SmoothTileMath.BytesPerTileRgba16F() * 16, coarse);
        Assert.Equal(8, cap);
    }

    [Fact]
    public void Lru_EvictsUnpinned_KeepsCoarsePinned()
    {
        var lru = new SmoothGpuTileLru(capacity: 3, coarsePinLevel: 2);
        Assert.True(lru.TryInsert(new SmoothTileId(0, 0, 0), out var s0, out _));
        Assert.True(lru.TryInsert(new SmoothTileId(0, 1, 0), out var s1, out _));
        Assert.True(lru.TryInsert(new SmoothTileId(2, 0, 0), out var s2, out _));
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
    public void PinnedSaturation_RefusesInsertWhenAllPinned()
    {
        var lru = new SmoothGpuTileLru(capacity: 2, coarsePinLevel: 0); // everything coarse-pinned
        Assert.True(lru.TryInsert(new SmoothTileId(0, 0, 0), out _, out _));
        Assert.True(lru.TryInsert(new SmoothTileId(0, 1, 0), out _, out _));
        Assert.False(lru.TryInsert(new SmoothTileId(0, 2, 0), out var slot, out _));
        Assert.Equal(-1, slot);
    }

    [Fact]
    public void TempPin_ProtectsFromEviction()
    {
        var lru = new SmoothGpuTileLru(capacity: 2, coarsePinLevel: 99);
        Assert.True(lru.TryInsert(new SmoothTileId(0, 0, 0), out _, out _));
        Assert.True(lru.TryInsert(new SmoothTileId(0, 1, 0), out _, out _));
        lru.SetTempPin(new SmoothTileId(0, 0, 0), true);
        lru.SetTempPin(new SmoothTileId(0, 1, 0), true);
        Assert.False(lru.TryInsert(new SmoothTileId(0, 2, 0), out _, out _));
        lru.SetTempPin(new SmoothTileId(0, 1, 0), false);
        Assert.True(lru.TryInsert(new SmoothTileId(0, 2, 0), out _, out var evicted));
        Assert.Equal(new SmoothTileId(0, 1, 0), evicted);
    }

    [Fact]
    public void Touch_ProtectsRecentFromEviction()
    {
        var lru = new SmoothGpuTileLru(2, coarsePinLevel: 99);
        Assert.True(lru.TryInsert(new SmoothTileId(0, 0, 0), out _, out _));
        Assert.True(lru.TryInsert(new SmoothTileId(0, 1, 0), out _, out _));
        Assert.True(lru.TryGetSlot(new SmoothTileId(0, 0, 0), out _));
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
