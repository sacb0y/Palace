namespace Palace.Helpers;

/// <summary>
/// Pure policy for V2 arm / rotate / recreate / panel resize — kept off WinUI so
/// tests can cover Fenrir contracts without a presenter host.
/// </summary>
public static class SmoothViewportArmPolicy
{
    /// <summary>Zoom within this of <c>_fitZoom</c> counts as "at fit" on panel resize.</summary>
    public const float AtFitEpsilon = 1e-3f;

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

    public static bool IsAtFit(float zoom, float fitZoom) =>
        MathF.Abs(zoom - fitZoom) <= AtFitEpsilon;

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

    /// <summary>
    /// Panel SizeChanged while armed: if still at fit, refit+center. Else keep zoom
    /// (clamped to new fit/min) and keep the oriented image point that was under the
    /// old panel center under the new panel center — never throw away user pan/zoom.
    /// </summary>
    public static (float Zoom, float Tx, float Ty, float FitZoom) AfterPanelResize(
        float oldPanelW,
        float oldPanelH,
        float newPanelW,
        float newPanelH,
        float zoom,
        float tx,
        float ty,
        float previousFitZoom,
        int orientedWidth,
        int orientedHeight)
    {
        var newFit = SmoothViewport.FitZoom(newPanelW, newPanelH, orientedWidth, orientedHeight);
        if (IsAtFit(zoom, previousFitZoom))
        {
            return FitCentered(newPanelW, newPanelH, orientedWidth, orientedHeight);
        }

        var newZoom = SmoothViewport.ClampZoom(zoom, newFit);
        var oldCx = oldPanelW * 0.5f;
        var oldCy = oldPanelH * 0.5f;
        if (MathF.Abs(newZoom - zoom) > 1e-12f)
        {
            var about = SmoothViewport.ZoomAbout(zoom, tx, ty, newZoom, oldCx, oldCy);
            zoom = about.Zoom;
            tx = about.Tx;
            ty = about.Ty;
        }

        var imgX = zoom > 1e-12f ? (oldCx - tx) / zoom : 0f;
        var imgY = zoom > 1e-12f ? (oldCy - ty) / zoom : 0f;
        var newCx = newPanelW * 0.5f;
        var newCy = newPanelH * 0.5f;
        return (newZoom, newCx - (imgX * newZoom), newCy - (imgY * newZoom), newFit);
    }
}
