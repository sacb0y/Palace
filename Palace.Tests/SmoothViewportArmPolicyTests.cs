using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothViewportArmPolicyTests
{
    [Theory]
    [InlineData(false, false, false)] // before arm
    [InlineData(false, true, false)]  // during build, not armed
    [InlineData(true, true, false)]   // during build, armed (stale)
    [InlineData(true, false, true)]   // after arm, idle
    public void MayApplyRotateTransform_GatesArmAndBuild(
        bool armed, bool building, bool expected)
    {
        Assert.Equal(
            expected,
            SmoothViewportArmPolicy.MayApplyRotateTransform(armed, building));
    }

    [Fact]
    public void RotateBeforeArm_KeepsOrientationOnly_DoesNotRequireTransform()
    {
        // Simulate R before arm: policy forbids transform; orientation int is stored by caller.
        var orient = 1;
        orient = SmoothViewport.ExifOrient.Rotate90(orient, clockwise: true);
        Assert.Equal(6, orient); // 1 → 6 (90 CW)
        Assert.False(SmoothViewportArmPolicy.MayApplyRotateTransform(
            viewportArmed: false, pyramidBuildRunning: false));
    }

    [Fact]
    public void RotateAfterArm_FitCentered_ChangesTransformOnly()
    {
        Assert.True(SmoothViewportArmPolicy.MayApplyRotateTransform(
            viewportArmed: true, pyramidBuildRunning: false));

        var before = SmoothViewportArmPolicy.FitCentered(
            panelW: 800, panelH: 600, orientedWidth: 100, orientedHeight: 50);
        // After 90 CW, oriented size swaps.
        var after = SmoothViewportArmPolicy.FitCentered(
            panelW: 800, panelH: 600, orientedWidth: 50, orientedHeight: 100);

        Assert.NotEqual(before.Zoom, after.Zoom);
        Assert.NotEqual(before.Tx, after.Tx);
        Assert.NotEqual(before.Ty, after.Ty);
        Assert.True(after.Zoom > 0);
        Assert.True(50 * after.Zoom <= 800f + 0.5f);
        Assert.True(100 * after.Zoom <= 600f + 0.5f);
    }

    [Fact]
    public void Resize_DoesNotRequirePyramidRebuild()
    {
        // Swapchain Resize is never a rebuild trigger — only GPU recreate without a built pyramid.
        Assert.False(SmoothViewportArmPolicy.MustRebuildPyramidAfterGpuRecreate(
            pyramidAlreadyBuilt: true));
        Assert.True(SmoothViewportArmPolicy.MustRebuildPyramidAfterGpuRecreate(
            pyramidAlreadyBuilt: false));
    }

    [Fact]
    public void PyramidBuilt_TracksFirstCoarseMs_AndEvidenceLabel()
    {
        var session = new SmoothTileSession(64, 64);
        Assert.False(session.PyramidBuilt);
        _ = session.BuildSyntheticLevel0AndCoarse();
        Assert.True(session.PyramidBuilt);
        Assert.Contains("gpuEvidence=OFF", session.StatusLine(gpuEvidenceOn: false), StringComparison.Ordinal);
        Assert.Contains("gpuEvidence=ON", session.StatusLine(gpuEvidenceOn: true), StringComparison.Ordinal);
    }

    [Fact]
    public void AfterPanelResize_AtFit_RefitsCentered()
    {
        var fit = SmoothViewportArmPolicy.FitCentered(800, 600, 100, 50);
        var next = SmoothViewportArmPolicy.AfterPanelResize(
            oldPanelW: 800, oldPanelH: 600,
            newPanelW: 400, newPanelH: 300,
            zoom: fit.Zoom, targetZoom: fit.Zoom, tx: fit.Tx, ty: fit.Ty,
            previousFitZoom: fit.FitZoom,
            orientedWidth: 100, orientedHeight: 50);
        var expect = SmoothViewportArmPolicy.FitCentered(400, 300, 100, 50);
        Assert.Equal(expect.Zoom, next.Zoom, precision: 4);
        Assert.Equal(expect.Tx, next.Tx, precision: 3);
        Assert.Equal(expect.Ty, next.Ty, precision: 3);
        Assert.True(SmoothViewportArmPolicy.IsAtFit(next.Zoom, next.FitZoom));
    }

    [Fact]
    public void AfterPanelResize_TargetAtFit_RefitsEvenIfLiveZoomDrifted()
    {
        var fit = SmoothViewportArmPolicy.FitCentered(1920, 1080, 10000, 6400);
        Assert.True(SmoothViewportArmPolicy.IsAtFit(fit.Zoom, fit.FitZoom));
        // Live zoom clearly off fit (would keep mid-zoom without target check); target still at fit.
        var drifted = fit.Zoom * 2.5f;
        Assert.False(SmoothViewportArmPolicy.IsAtFit(drifted, fit.FitZoom));
        var next = SmoothViewportArmPolicy.AfterPanelResize(
            oldPanelW: 1920, oldPanelH: 1080,
            newPanelW: 1080, newPanelH: 585,
            zoom: drifted, targetZoom: fit.Zoom, tx: fit.Tx, ty: fit.Ty,
            previousFitZoom: fit.FitZoom,
            orientedWidth: 10000, orientedHeight: 6400);
        var expect = SmoothViewportArmPolicy.FitCentered(1080, 585, 10000, 6400);
        Assert.Equal(expect.Zoom, next.Zoom, precision: 4);
        Assert.True(SmoothViewportArmPolicy.IsAtFit(next.Zoom, next.FitZoom));
    }

    [Fact]
    public void FitCentered_OddPanel_MarginsWithinOnePx()
    {
        var fit = SmoothViewportArmPolicy.FitCentered(
            panelW: 801, panelH: 601, orientedWidth: 100, orientedHeight: 50);
        Assert.True(fit.MaxMarginDeltaX <= 1f + 1e-4f, $"dx={fit.MaxMarginDeltaX}");
        Assert.True(fit.MaxMarginDeltaY <= 1f + 1e-4f, $"dy={fit.MaxMarginDeltaY}");
        // Usable with top inset (status overlay): still symmetric in usable rect.
        var inset = SmoothViewportArmPolicy.FitCentered(
            panelW: 801, panelH: 601, orientedWidth: 100, orientedHeight: 50,
            insetLeft: 0, insetTop: 48, insetRight: 0, insetBottom: 0);
        Assert.True(inset.MaxMarginDeltaX <= 1f + 1e-4f);
        Assert.True(inset.MaxMarginDeltaY <= 1f + 1e-4f);
        Assert.True(inset.Ty >= 48f - 1e-3f);
    }

    [Fact]
    public void AfterPanelResize_Zoomed_KeepsZoomAndCenterImagePoint()
    {
        // Start fit, then zoom about panel center to 2× (still within max).
        var fit = SmoothViewportArmPolicy.FitCentered(800, 600, 100, 50);
        var zoomed = SmoothViewport.ZoomAbout(
            fit.Zoom, fit.Tx, fit.Ty, newZoom: 2f, cx: 400f, cy: 300f);
        Assert.False(SmoothViewportArmPolicy.IsAtFit(zoomed.Zoom, fit.FitZoom));

        var next = SmoothViewportArmPolicy.AfterPanelResize(
            oldPanelW: 800, oldPanelH: 600,
            newPanelW: 1000, newPanelH: 700,
            zoom: zoomed.Zoom, targetZoom: zoomed.Zoom, tx: zoomed.Tx, ty: zoomed.Ty,
            previousFitZoom: fit.FitZoom,
            orientedWidth: 100, orientedHeight: 50);

        Assert.Equal(2f, next.Zoom, precision: 4);
        // Image point under old center stays under new center.
        var oldImgX = (400f - zoomed.Tx) / zoomed.Zoom;
        var oldImgY = (300f - zoomed.Ty) / zoomed.Zoom;
        var newImgX = (500f - next.Tx) / next.Zoom;
        var newImgY = (350f - next.Ty) / next.Zoom;
        Assert.Equal(oldImgX, newImgX, precision: 3);
        Assert.Equal(oldImgY, newImgY, precision: 3);
    }

    [Theory]
    [InlineData(0, 250)]
    [InlineData(1, 1000)]
    [InlineData(2, 2000)]
    [InlineData(3, 2000)]
    [InlineData(4, 2000)]
    public void DeviceLostBackoff_Ms(int failedAttemptIndex, int expectedMs)
    {
        Assert.Equal(expectedMs, SmoothDeviceLostRecreatePolicy.BackoffMs(failedAttemptIndex));
    }

    [Fact]
    public void DeviceLostBackoff_CapsAtFiveThenExhaustedMessage()
    {
        Assert.Equal(5, SmoothDeviceLostRecreatePolicy.MaxAttempts);
        for (var n = 1; n <= 4; n++)
        {
            Assert.True(SmoothDeviceLostRecreatePolicy.ShouldRetry(n));
            var keep = SmoothDeviceLostRecreatePolicy.AfterFailedAttempt(n);
            Assert.True(keep.KeepRetrying);
            Assert.Null(keep.ExhaustedStatus);
            Assert.Equal(SmoothDeviceLostRecreatePolicy.BackoffMs(n - 1), keep.NextBackoffMs);
        }

        Assert.False(SmoothDeviceLostRecreatePolicy.ShouldRetry(5));
        var done = SmoothDeviceLostRecreatePolicy.AfterFailedAttempt(5);
        Assert.False(done.KeepRetrying);
        Assert.Equal(SmoothDeviceLostRecreatePolicy.ExhaustedMessage, done.ExhaustedStatus);
        Assert.Equal("GPU unavailable — reopen viewer", done.ExhaustedStatus);
    }
}
