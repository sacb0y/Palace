using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothViewportTests
{
    [Fact]
    public void RemovesDipPanelCap_IsTrue()
    {
        Assert.True(SmoothViewport.RemovesDipPanelCap);
        Assert.False(SmoothViewport.AppliesLegacyDipCap(smoothPresenterEnabled: true));
        Assert.True(SmoothViewport.AppliesLegacyDipCap(smoothPresenterEnabled: false));
    }

    [Fact]
    public void CapStillPanelSize_RemoveDipCap_PassesThrough()
    {
        var (w, h) = TouchGestures.CapStillPanelSize(16000, 9000, rasterScale: 1.5, removeDipCap: true);
        Assert.Equal(16000, w);
        Assert.Equal(9000, h);

        var capped = TouchGestures.CapStillPanelSize(16000, 9000, rasterScale: 1.5, removeDipCap: false);
        Assert.True(Math.Max(capped.Width, capped.Height) <= TouchGestures.MaxStillPanelDipForScale(1.5) + 1e-6);
    }

    [Fact]
    public void ZoomAbout_KeepsCursorAnchor()
    {
        var (z, tx, ty) = SmoothViewport.ZoomAbout(zoom: 1f, tx: 10f, ty: 20f, newZoom: 2f, cx: 100f, cy: 200f);
        Assert.Equal(2f, z);
        // Point under cursor (100,200) in image space was (90,180); after ×2 stays under cursor.
        Assert.Equal(100f - 90f * 2f, tx, 3);
        Assert.Equal(200f - 180f * 2f, ty, 3);
    }

    [Fact]
    public void LevelForZoom_MatchesGeoLod()
    {
        Assert.Equal(0, SmoothViewport.LevelForZoom(1f, maxLevel: 5));
        Assert.Equal(0, SmoothViewport.LevelForZoom(0.75f, maxLevel: 5));
        Assert.Equal(1, SmoothViewport.LevelForZoom(0.4f, maxLevel: 5));
        Assert.Equal(2, SmoothViewport.LevelForZoom(0.2f, maxLevel: 5));
        Assert.Equal(5, SmoothViewport.LevelForZoom(0.001f, maxLevel: 5));
    }

    [Theory]
    [InlineData(0.5f, SmoothViewport.FilterMode.BicubicParentBlend)]
    [InlineData(1.5f, SmoothViewport.FilterMode.Bicubic)]
    [InlineData(5f, SmoothViewport.FilterMode.BicubicToNearest)]
    [InlineData(8f, SmoothViewport.FilterMode.Nearest)]
    public void FilterForZoom_Bands(float zoom, SmoothViewport.FilterMode expected)
    {
        Assert.Equal(expected, SmoothViewport.FilterForZoom(zoom));
    }

    [Fact]
    public void Affine_RoundTripInvert()
    {
        var m = SmoothViewport.Affine2x3.FromZoomPan(2f, 10f, 20f);
        var inv = m.Invert();
        var (x, y) = m.Transform(3f, 4f);
        var (ix, iy) = inv.Transform(x, y);
        Assert.Equal(3f, ix, 4);
        Assert.Equal(4f, iy, 4);
    }

    [Fact]
    public void PackViewCb_WritesViewportAndTileParams()
    {
        var dest = new float[32];
        var m = SmoothViewport.Affine2x3.FromZoomPan(1f, 0f, 0f);
        SmoothViewport.PackViewCb(dest, m, panelW: 100, panelH: 50, zoom: 1f);
        Assert.Equal(0.02f, dest[16], 5); // 2/W
        Assert.Equal(0.04f, dest[17], 5); // 2/H
        Assert.Equal(1f, dest[18]);
        Assert.Equal(SmoothTileMath.SlotSize, dest[28]);
        Assert.Equal(SmoothTileMath.Border, dest[29]);
    }

    [Fact]
    public void CollectVisibleTiles_CoversViewport()
    {
        var m = SmoothViewport.Affine2x3.FromZoomPan(1f, 0f, 0f);
        var list = new List<(int Tx, int Ty)>();
        SmoothViewport.CollectVisibleTiles(m, 508, 508, level: 0, fullWidth: 2000, fullHeight: 2000, list);
        Assert.NotEmpty(list);
        Assert.Contains((0, 0), list);
    }

    [Fact]
    public void ClampZoom_RespectsMax32()
    {
        Assert.Equal(32f, SmoothViewport.ClampZoom(64f, fitZoom: 0.2f));
        Assert.Equal(0.2f, SmoothViewport.ClampZoom(0.01f, fitZoom: 0.2f));
    }
}
