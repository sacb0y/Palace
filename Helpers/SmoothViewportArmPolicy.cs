namespace Palace.Helpers;

/// <summary>
/// Pure policy for V2 arm / rotate / recreate — kept off WinUI so tests can cover
/// the Fenrir R FailFast contract without a presenter host.
/// </summary>
public static class SmoothViewportArmPolicy
{
    /// <summary>
    /// Rotate may update the live transform only when the viewport is armed and
    /// no pyramid build is running. Otherwise only store EXIF orientation — 
    /// <c>ArmV2Viewport</c> at build completion applies it.
    /// </summary>
    public static bool MayApplyRotateTransform(bool viewportArmed, bool pyramidBuildRunning) =>
        viewportArmed && !pyramidBuildRunning;

    /// <summary>
    /// Pyramid is image-scoped. After GPU recreate (device-lost), rebuild only when
    /// the CPU pyramid was never completed. Swapchain Resize must never rebuild.
    /// </summary>
    public static bool MustRebuildPyramidAfterGpuRecreate(bool pyramidAlreadyBuilt) =>
        !pyramidAlreadyBuilt;

    /// <summary>Fit zoom + centered pan for oriented image size in panel device px.</summary>
    public static (float Zoom, float Tx, float Ty, float FitZoom) FitCentered(
        float panelW,
        float panelH,
        int orientedWidth,
        int orientedHeight)
    {
        var fit = SmoothViewport.FitZoom(panelW, panelH, orientedWidth, orientedHeight);
        var zoom = SmoothViewport.ClampZoom(fit, fit);
        var tx = (panelW - (orientedWidth * zoom)) * 0.5f;
        var ty = (panelH - (orientedHeight * zoom)) * 0.5f;
        return (zoom, tx, ty, fit);
    }
}
