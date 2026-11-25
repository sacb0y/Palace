using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothTileSessionTests
{
    [Fact]
    public void SyntheticSession_BuildsCoarsePinnedTiles()
    {
        var session = new SmoothTileSession(3000, 2000, availableVramBytes: 1L << 30);
        Assert.True(session.CoarseLevel >= 1);
        Assert.Equal(SmoothTileMath.CoarseLevelIndex(3000, 2000), session.CoarseLevel);
        var ms = session.BuildSyntheticLevel0AndCoarse();
        Assert.True(ms >= 0);
        Assert.True(session.CoarseTileCount > 0);
        Assert.True(session.Cpu.Count >= session.CoarseTileCount);
        Assert.Equal(session.CoarseTileCount, session.Lru.ResidentCount);
        Assert.Equal(0, session.Cpu.Count - session.CoarseTileCount);
        Assert.Contains("Border=2", session.StatusLine(), StringComparison.Ordinal);
        Assert.Contains("RGBA16F", session.StatusLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void SyntheticSession_Over8192_BuildsCoarse()
    {
        // Restored >8192 coverage (Wimbly should-fix).
        var session = new SmoothTileSession(9000, 5000, availableVramBytes: 4L << 30);
        Assert.True(session.CoarseLevel >= 1);
        Assert.Equal(SmoothTileMath.CoarseLevelIndex(9000, 5000), session.CoarseLevel);
        var ms = session.BuildSyntheticLevel0AndCoarse();
        Assert.True(ms >= 0);
        Assert.True(session.CoarseTileCount > 0);
        Assert.Equal(session.CoarseTileCount, session.Lru.ResidentCount);
    }

    [Fact]
    public async Task DecodeGate_BlocksPyramidWhenPathRefused()
    {
        var session = new SmoothTileSession(512, 512);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.BuildPyramidAsync(CancellationToken.None, sourcePath: "/no/such/palace-v1-tile-file.bin"));
    }

    [Fact]
    public void Float16_RoundToNearestEven_Smoke()
    {
        Assert.Equal(0, SmoothTileSession.Float32ToFloat16Bits(0f));
        Assert.NotEqual((ushort)0, SmoothTileSession.Float32ToFloat16Bits(1f));
        Assert.NotEqual((ushort)0, SmoothTileSession.Float32ToFloat16Bits(4f));
        Assert.Equal((ushort)0x3c00, SmoothTileSession.Float32ToFloat16Bits(1f));
    }
}
