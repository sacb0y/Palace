using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothTileSessionTests
{
    [Fact]
    public void SyntheticSession_BuildsCoarsePinnedTiles()
    {
        // Smaller than 12k for unit speed; still exercises >8192 and odd coarse.
        var session = new SmoothTileSession(9000, 5000, availableVramBytes: 4L << 30);
        Assert.True(session.CoarseLevel >= 1);
        Assert.Equal(SmoothTileMath.CoarseLevelIndex(9000, 5000), session.CoarseLevel);
        var ms = session.BuildSyntheticLevel0AndCoarse();
        Assert.True(ms >= 0);
        Assert.True(session.CoarseTileCount > 0);
        Assert.True(session.Cpu.Count >= session.CoarseTileCount);
        Assert.Equal(session.CoarseTileCount, session.Lru.ResidentCount);
        Assert.Contains("Border=2", session.StatusLine(), StringComparison.Ordinal);
        Assert.Contains("RGBA16F", session.StatusLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void Float16_RoundTripSmoke()
    {
        Assert.Equal(0, SmoothTileSession.Float32ToFloat16Bits(0f));
        Assert.NotEqual((ushort)0, SmoothTileSession.Float32ToFloat16Bits(1f));
        Assert.NotEqual((ushort)0, SmoothTileSession.Float32ToFloat16Bits(4f)); // HDR Working
    }
}
