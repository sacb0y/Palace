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
    public void Build_FallsBackWholePyramid_WhenFineMissing()
    {
        // Only coarse L2 resident — fine L0 must still draw via ancestor.
        var slots = new Dictionary<SmoothTileId, uint>
        {
            [new SmoothTileId(2, 0, 0)] = 9,
        };
        var xf = new SmoothTileCrossfade(durationMs: 100f);
        var m = SmoothViewport.Affine2x3.FromZoomPan(1f, 0f, 0f);
        var draws = SmoothViewportFrame.Build(
            m,
            panelW: 400,
            panelH: 400,
            zoom: 1f,
            fullWidth: 2032,
            fullHeight: 2032,
            maxLevel: 2,
            trySlice: id => slots.TryGetValue(id, out var s) ? s : null,
            crossfade: xf,
            dtMs: 16f);

        Assert.NotEmpty(draws);
        Assert.All(draws, d => Assert.Equal(2, d.Level));
        Assert.All(draws, d => Assert.Equal(1f, d.Opacity));
    }

    [Fact]
    public void Build_UnderlayAncestor_WhenFineFading()
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
            dtMs: 16f);

        Assert.Contains(draws, d => d.Level == 1 && d.Opacity >= 1f);
        Assert.Contains(draws, d => d.Level == 0 && d.Opacity < 1f);
        // N1: ParentSlice only when ancestor is exactly level+1.
        Assert.Contains(draws, d => d.Level == 0 && d.ParentSlice == 2);
    }

    [Fact]
    public void Build_ParentSlice_ClearedWhenAncestorDeeperThanLevelPlusOne()
    {
        var slots = new Dictionary<SmoothTileId, uint>
        {
            [new SmoothTileId(0, 0, 0)] = 1,
            [new SmoothTileId(2, 0, 0)] = 9, // skip L1 — deeper only
        };
        var xf = new SmoothTileCrossfade(durationMs: 100f);
        var m = SmoothViewport.Affine2x3.FromZoomPan(1f, 0f, 0f);
        var draws = SmoothViewportFrame.Build(
            m,
            panelW: 400,
            panelH: 400,
            zoom: 1f,
            fullWidth: 2032,
            fullHeight: 2032,
            maxLevel: 2,
            trySlice: id => slots.TryGetValue(id, out var s) ? s : null,
            crossfade: xf,
            dtMs: 16f);

        var fine = Assert.Single(draws, d => d.Level == 0);
        Assert.Equal(0xFFFFFFFFu, fine.ParentSlice);
        Assert.Contains(draws, d => d.Level == 2 && d.Opacity >= 1f);
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
