namespace Palace.Helpers;

/// <summary>
/// Pure policy for V2/V3 arm / rotate / recreate / panel resize — kept off WinUI so
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

    /// <summary>
    /// Absolute or relative tolerance so float drift after ease still counts as at-fit.
    /// </summary>
    public static bool IsAtFit(float zoom, float fitZoom)
    {
        var tol = MathF.Max(AtFitEpsilon, MathF.Abs(fitZoom) * 1e-3f);
        return MathF.Abs(zoom - fitZoom) <= tol;
    }

    /// <summary>
    /// Split spare device px into leading/trailing margins that differ by at most 1 px
    /// (odd panel / spare sizes stay visually centered).
    /// </summary>
    public static (float Leading, float Trailing) SplitSymmetricMargin(float spare)
    {
        if (spare <= 0f)
        {
            return (0f, 0f);
        }

        var leading = MathF.Floor(spare * 0.5f);
        var trailing = spare - leading;
        return (leading, trailing);
    }

    /// <summary>
    /// Fit zoom + centered pan in the usable panel rect (full panel minus overlay insets).
    /// Margins in the usable rect are symmetric within 1 device px.
    /// </summary>
    public static FitResult FitCentered(
        float panelW,
        float panelH,
        int orientedWidth,
        int orientedHeight,
        float insetLeft = 0f,
        float insetTop = 0f,
        float insetRight = 0f,
        float insetBottom = 0f)
    {
        var usableW = MathF.Max(0f, panelW - insetLeft - insetRight);
        var usableH = MathF.Max(0f, panelH - insetTop - insetBottom);
        var fit = SmoothViewport.FitZoom(usableW, usableH, orientedWidth, orientedHeight);
        var zoom = SmoothViewport.ClampZoom(fit, fit);
        var drawnW = orientedWidth * zoom;
        var drawnH = orientedHeight * zoom;
        var (mx0, mx1) = SplitSymmetricMargin(usableW - drawnW);
        var (my0, my1) = SplitSymmetricMargin(usableH - drawnH);
        var tx = insetLeft + mx0;
        var ty = insetTop + my0;
        return new FitResult(zoom, tx, ty, fit, mx0, mx1, my0, my1);
    }

    /// <summary>
    /// Panel SizeChanged while armed: if still at fit (live or target zoom), refit+center.
    /// Else keep zoom (clamped to new fit/min) and keep the oriented image point that was
    /// under the old panel center under the new panel center — never throw away user pan/zoom.
    /// </summary>
    public static FitResult AfterPanelResize(
        float oldPanelW,
        float oldPanelH,
        float newPanelW,
        float newPanelH,
        float zoom,
        float targetZoom,
        float tx,
        float ty,
        float previousFitZoom,
        int orientedWidth,
        int orientedHeight,
        float insetLeft = 0f,
        float insetTop = 0f,
        float insetRight = 0f,
        float insetBottom = 0f)
    {
        if (IsAtFit(zoom, previousFitZoom) || IsAtFit(targetZoom, previousFitZoom))
        {
            return FitCentered(
                newPanelW,
                newPanelH,
                orientedWidth,
                orientedHeight,
                insetLeft,
                insetTop,
                insetRight,
                insetBottom);
        }

        var newFit = SmoothViewport.FitZoom(
            MathF.Max(0f, newPanelW - insetLeft - insetRight),
            MathF.Max(0f, newPanelH - insetTop - insetBottom),
            orientedWidth,
            orientedHeight);
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
        return new FitResult(
            newZoom,
            newCx - (imgX * newZoom),
            newCy - (imgY * newZoom),
            newFit,
            MarginX0: 0f,
            MarginX1: 0f,
            MarginY0: 0f,
            MarginY1: 0f);
    }

    public readonly record struct FitResult(
        float Zoom,
        float Tx,
        float Ty,
        float FitZoom,
        float MarginX0,
        float MarginX1,
        float MarginY0,
        float MarginY1)
    {
        public float MaxMarginDeltaX => MathF.Abs(MarginX0 - MarginX1);

        public float MaxMarginDeltaY => MathF.Abs(MarginY0 - MarginY1);
    }
}

/// <summary>
/// Pure backoff for device-lost GPU recreate (TDR / RemoveDevice). Caps retries so
/// a sticky removed adapter does not spin the present timer forever.
/// </summary>
public static class SmoothDeviceLostRecreatePolicy
{
    /// <summary>Max recreate attempts after device-lost before giving up.</summary>
    public const int MaxAttempts = 5;

    public const string ExhaustedMessage = "GPU unavailable — reopen viewer";

    /// <summary>
    /// Delay before the next attempt after <paramref name="failedAttemptIndex"/>
    /// failures (0-based: after 1st fail → 250ms, 2nd → 1s, 3rd+ → 2s).
    /// </summary>
    public static int BackoffMs(int failedAttemptIndex)
    {
        if (failedAttemptIndex <= 0)
        {
            return 250;
        }

        if (failedAttemptIndex == 1)
        {
            return 1000;
        }

        return 2000;
    }

    public static bool ShouldRetry(int attemptCount) =>
        attemptCount < MaxAttempts;

    /// <summary>
    /// After a failed recreate attempt: whether to keep polling NeedsRecreate with
    /// backoff, or show <see cref="ExhaustedMessage"/>.
    /// </summary>
    public static (bool KeepRetrying, int NextBackoffMs, string? ExhaustedStatus)
        AfterFailedAttempt(int attemptCountAfterFailure)
    {
        if (!ShouldRetry(attemptCountAfterFailure))
        {
            return (false, 0, ExhaustedMessage);
        }

        return (true, BackoffMs(attemptCountAfterFailure - 1), null);
    }
}
