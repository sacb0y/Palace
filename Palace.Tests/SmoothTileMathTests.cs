using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothTileMathTests
{
    [Fact]
    public void Slot_Border_Content_Constants()
    {
        Assert.Equal(512, SmoothTileMath.SlotSize);
        Assert.Equal(2, SmoothTileMath.Border);
        Assert.Equal(508, SmoothTileMath.ContentSize);
        Assert.Equal(SmoothTileWorkingSpace.Border, SmoothTileMath.Border);
        Assert.Equal(0, SmoothTileWorkingSpace.SmoothSrgb8);
        Assert.Equal("RGBA16F", SmoothTileWorkingSpace.GpuFormat);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(508, 1)]
    [InlineData(509, 2)]
    [InlineData(1016, 2)]
    [InlineData(1017, 3)]
    [InlineData(8192, 17)]
    [InlineData(12000, 24)]
    public void TilesAlong_CoversContent(int pixels, int expectedTiles)
    {
        Assert.Equal(expectedTiles, SmoothTileMath.TilesAlong(pixels));
    }

    [Fact]
    public void ContentRect_ClipsOddEdgeAndOnePixel()
    {
        var (x, y, w, h) = SmoothTileMath.ContentRect(0, 0, 1, 1);
        Assert.Equal((0, 0, 1, 1), (x, y, w, h));

        var edge = SmoothTileMath.ContentRect(23, 15, 12000, 8000);
        Assert.Equal(23 * 508, edge.X);
        Assert.Equal(15 * 508, edge.Y);
        Assert.Equal(12000 - 23 * 508, edge.W);
        Assert.Equal(8000 - 15 * 508, edge.H);
        Assert.True(edge.W < 508);
        Assert.True(edge.H < 508);
    }

    [Fact]
    public void ContentToSlot_AppliesBorder()
    {
        Assert.Equal((2, 2), SmoothTileMath.ContentToSlot(0, 0));
        Assert.Equal((509, 509), SmoothTileMath.ContentToSlot(507, 507));
    }

    [Fact]
    public void LevelSizes_CeilHalve_OddAndLarge()
    {
        Assert.Equal(6000, SmoothTileMath.LevelWidth(12000, 1));
        Assert.Equal(4000, SmoothTileMath.LevelHeight(8000, 1));
        Assert.Equal(5, SmoothTileMath.LevelWidth(9, 1)); // ceil(9/2)
        Assert.Equal(1, SmoothTileMath.LevelWidth(1, 10));
    }

    [Fact]
    public void CoarseLevel_PinsAtOrBelow2048()
    {
        // 12000 → 6000 → 3000 → 1500 (≤2048) => level 3
        Assert.Equal(3, SmoothTileMath.CoarseLevelIndex(12000, 8000));
        Assert.Equal(0, SmoothTileMath.CoarseLevelIndex(2048, 2048));
        Assert.Equal(0, SmoothTileMath.CoarseLevelIndex(512, 512));
        Assert.Equal(1, SmoothTileMath.CoarseLevelIndex(2049, 100));
    }

    [Fact]
    public void MaxLevel_UntilSingleTile()
    {
        var max = SmoothTileMath.MaxLevel(12000, 8000);
        Assert.True(max >= SmoothTileMath.CoarseLevelIndex(12000, 8000));
        var w = SmoothTileMath.LevelWidth(12000, max);
        var h = SmoothTileMath.LevelHeight(8000, max);
        Assert.True(w <= 508 && h <= 508);
    }
}
