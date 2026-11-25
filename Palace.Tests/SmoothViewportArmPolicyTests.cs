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
}
