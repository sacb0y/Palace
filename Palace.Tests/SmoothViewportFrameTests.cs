using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothViewportFrameTests
{
    [Fact]
    public void Build_DrawsResidentTilesCoarseFirst()
    {
        var slots = new Dictionary<SmoothTileId, uint>
        {
            [new SmoothTileId(0, 0, 0)] = 1,
            [new SmoothTileId(1, 0, 0)] = 2,
        };
        var xf = new SmoothTileCrossfade(durationMs: 100f);
        var m = SmoothViewport.Affine2x3.FromZoomPan(1f, 0f, 0f);
        var draws = SmoothViewportFrame.Build(
            m,
            panelW: 400,
            panelH: 400,
            zoom: 1f,
            fullWidth: 508,
            fullHeight: 508,
            maxLevel: 1,
            trySlice: id => slots.TryGetValue(id, out var s) ? s : null,
            crossfade: xf,
            dtMs: 50f);

        Assert.NotEmpty(draws);
        Assert.All(draws, d => Assert.True(d.Opacity > 0f));
        // Coarser levels first when mixed.
        for (var i = 1; i < draws.Count; i++)
        {
            Assert.True(draws[i - 1].Level >= draws[i].Level);
        }
    }

    [Fact]
    public void PackInstance_WritesTwelveFloats()
    {
        var dest = new float[12];
        var t = new SmoothViewportFrame.DrawTile(
            new SmoothTileId(0, 0, 0),
            0, 0, 100, 100, 0, 0, 1, 0, 3, 0xFFFFFFFFu, 0.5f);
        SmoothViewportFrame.PackInstance(t, dest);
        Assert.Equal(100f, dest[2]);
        Assert.Equal(0.5f, dest[10]);
    }
}
