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

    [Fact]
    public void WheelZoomFactor_GeoNotches()
    {
        Assert.Equal(1f, SmoothViewport.WheelZoomFactor(0));
        Assert.Equal(MathF.Pow(2f, 0.25f), SmoothViewport.WheelZoomFactor(1), precision: 5);
        Assert.Equal(MathF.Pow(2f, -0.25f), SmoothViewport.WheelZoomFactor(-1), precision: 5);
        Assert.Equal(MathF.Pow(2f, 0.125f), SmoothViewport.WheelZoomFactor(1, fine: true), precision: 5);
    }

    [Fact]
    public void ClampFrameDt_CapsAt50Ms()
    {
        Assert.Equal(0f, SmoothViewport.ClampFrameDt(-1f));
        Assert.Equal(0.05f, SmoothViewport.ClampFrameDt(1f));
        Assert.Equal(0.016f, SmoothViewport.ClampFrameDt(0.016f), precision: 5);
    }

    [Fact]
    public void SnapPanTranslation_RoundsDevicePx()
    {
        var (tx, ty) = SmoothViewport.SnapPanTranslation(10.4f, -3.6f);
        Assert.Equal(10f, tx);
        Assert.Equal(-4f, ty);
    }

    [Fact]
    public void ManipulationScaleTranslate_CentroidThenTranslationTimesScale()
    {
        // Isiac: scale about position, then T += Translation × scale.
        var (z, tx, ty) = SmoothViewport.ManipulationScaleTranslate(
            zoom: 1f,
            tx: 0f,
            ty: 0f,
            scaleDelta: 2f,
            centroidX: 100f,
            centroidY: 50f,
            translationX: 10f,
            translationY: -4f,
            fitZoom: 0.1f);
        var about = SmoothViewport.PinchZoomAbout(
            1f, 0f, 0f, 2f, 100f, 50f, fitZoom: 0.1f);
        Assert.Equal(about.Zoom, z);
        Assert.Equal(about.Tx + (10f * 2f), tx, precision: 4);
        Assert.Equal(about.Ty + (-4f * 2f), ty, precision: 4);
    }

    [Fact]
    public void ManipulationScaleTranslate_ScaleOne_AppliesTranslationOnly()
    {
        var (z, tx, ty) = SmoothViewport.ManipulationScaleTranslate(
            zoom: 2f,
            tx: 5f,
            ty: 7f,
            scaleDelta: 1f,
            centroidX: 40f,
            centroidY: 60f,
            translationX: 3f,
            translationY: -2f,
            fitZoom: 0.1f);
        Assert.Equal(2f, z);
        Assert.Equal(5f + 3f, tx, precision: 4);
        Assert.Equal(7f - 2f, ty, precision: 4);
    }

    [Fact]
    public void IsActivePointerPan_SecondPointerDoesNotDriveTxTy()
    {
        Assert.True(SmoothViewport.IsActivePointerPan(
            panning: true, capturedPointerId: 1u, eventPointerId: 1u));
        Assert.False(SmoothViewport.IsActivePointerPan(
            panning: true, capturedPointerId: 1u, eventPointerId: 2u));
        Assert.False(SmoothViewport.IsActivePointerPan(
            panning: false, capturedPointerId: 1u, eventPointerId: 1u));
        Assert.True(SmoothViewport.AllowsPointerPanBegin(isMouseOrPen: true, leftButton: true));
        Assert.False(SmoothViewport.AllowsPointerPanBegin(isMouseOrPen: false, leftButton: true));
    }

    [Fact]
    public void PinchZoomAbout_ClampsAndAnchorsCentroid()
    {
        var (z, tx, ty) = SmoothViewport.PinchZoomAbout(
            zoom: 1f, tx: 0f, ty: 0f, scaleDelta: 64f, centroidX: 100f, centroidY: 50f, fitZoom: 0.2f);
        Assert.Equal(32f, z); // max clamp
        // Cursor still maps to same image point after clamp.
        var about = SmoothViewport.ZoomAbout(1f, 0f, 0f, 32f, 100f, 50f);
        Assert.Equal(about.Tx, tx, precision: 3);
        Assert.Equal(about.Ty, ty, precision: 3);
    }
}
