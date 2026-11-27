namespace Palace.Helpers;

/// <summary>
/// Smooth Viewer V4 Zoom HUD + pixel-grid policy (Gooey approved 2026-11-23).
/// WinUI-free so <c>Palace.Tests</c> can cover fade, announce, and grid bands.
/// Grid lines themselves stay in <c>SmoothImage.hlsl</c> — this only drives the
/// toggle opacity packed into <see cref="SmoothViewport.PackViewCb"/>.
/// </summary>
public static class SmoothZoomHud
{
    public const float FadeOpacityDark = 0.20f;
    public const float FadeOpacityLight = 0.22f;
    public const int AppearMs = 80;
    public const int HoldMs = 700;
    public const int FadeMs = 250;
    public const float BottomMarginDip = 16f;
    public const float PillHeightDip = 36f;
    public const float ButtonHeightDip = 28f;
    /// <summary>Zoom % readout is 1:1 when zoom == 1.</summary>
    public const float ActualZoom = 1f;

    public const int KeyLetterG = 71;
    public const int KeyAdd = 107;
    public const int KeySubtract = 109;
    public const int KeyOemPlus = 187;
    public const int KeyOemMinus = 189;

    public static float RestOpacity(bool isLightTheme) =>
        isLightTheme ? FadeOpacityLight : FadeOpacityDark;

    /// <summary>Live readout, e.g. <c>240%</c> or <c>3200%</c>. 100% = 1:1.</summary>
    public static string FormatPercent(float zoom)
    {
        var pct = Math.Clamp(zoom, 0f, SmoothViewport.MaxZoom) * 100f;
        var rounded = (int)MathF.Round(pct);
        if (rounded < 1 && zoom > 0f)
        {
            rounded = 1;
        }

        return rounded.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    public static string AnnounceText(float zoom) =>
        "Zoom " + ((int)MathF.Round(Math.Clamp(zoom, 0f, SmoothViewport.MaxZoom) * 100f))
            .ToString(System.Globalization.CultureInfo.InvariantCulture)
        + " percent";

    public static bool IsAtFit(float zoom, float fitZoom, float epsilon = 1e-3f) =>
        fitZoom > 0f && MathF.Abs(zoom - fitZoom) <= epsilon;

    public static bool IsAtActual(float zoom, float epsilon = 1e-3f) =>
        MathF.Abs(zoom - ActualZoom) <= epsilon;

    /// <summary>
    /// Announce on gesture end, and whenever the value crosses 100%, Fit, 8×, or 32×.
    /// Do not announce when the pill only fades / restores (value unchanged).
    /// </summary>
    public static bool ShouldAnnounce(float previousZoom, float nextZoom, bool gestureEnded)
    {
        if (gestureEnded && !NearlyEqual(previousZoom, nextZoom))
        {
            return true;
        }

        return Crossed(previousZoom, nextZoom, ActualZoom)
            || Crossed(previousZoom, nextZoom, SmoothViewport.GridZoomStart)
            || Crossed(previousZoom, nextZoom, SmoothViewport.MaxZoom);
    }

    /// <summary>
    /// Same as <see cref="ShouldAnnounce(float,float,bool)"/> plus Fit crossings
    /// (Fit zoom varies with panel size).
    /// </summary>
    public static bool ShouldAnnounce(
        float previousZoom,
        float nextZoom,
        float fitZoom,
        bool gestureEnded)
    {
        if (ShouldAnnounce(previousZoom, nextZoom, gestureEnded))
        {
            return true;
        }

        return fitZoom > 0f && Crossed(previousZoom, nextZoom, fitZoom);
    }

    /// <summary>
    /// Toggle may arm at any zoom; HLSL draws only from 8× (fade 8→10).
    /// Packed <c>grid_opacity</c> is 1 when armed, 0 when off.
    /// </summary>
    public static float PackGridOpacity(bool armed) => armed ? 1f : 0f;

    /// <summary>Drawn opacity after the 8×–10× ease (for tests / tooltips).</summary>
    public static float DrawnGridOpacity(bool armed, float zoom)
    {
        if (!armed)
        {
            return 0f;
        }

        var span = SmoothViewport.GridZoomFull - SmoothViewport.GridZoomStart;
        if (span <= 1e-6f)
        {
            return zoom >= SmoothViewport.GridZoomStart ? 1f : 0f;
        }

        return Math.Clamp((zoom - SmoothViewport.GridZoomStart) / span, 0f, 1f);
    }

    public static bool IsGridIconDimmed(float zoom) => zoom < SmoothViewport.GridZoomStart;

    public static string GridTooltip(float zoom) =>
        IsGridIconDimmed(zoom)
            ? "Pixel grid (G) · appears at 8× and above"
            : "Pixel grid (G)";

    public static string FitTooltip => "Fit to window  0";

    public static string ActualTooltip => "Actual size  1";

    public static bool TogglesGrid(bool controlDown, int keyCode) =>
        !controlDown && keyCode == KeyLetterG;

    public static bool ZoomsIn(int keyCode) =>
        keyCode is KeyAdd or KeyOemPlus;

    public static bool ZoomsOut(int keyCode) =>
        keyCode is KeySubtract or KeyOemMinus;

    /// <summary>
    /// Fit via bare <c>0</c> / numpad 0, or <c>Ctrl+0</c>. Ctrl alone does not
    /// change the digit mapping (alternates stay the same digit).
    /// </summary>
    public static bool RequestsFit(int keyCode) =>
        keyCode is GalleryScale.KeyNumber0 or GalleryScale.KeyNumberPad0;

    public static bool RequestsActual(int keyCode) =>
        keyCode is GalleryScale.KeyNumber1 or GalleryScale.KeyNumberPad1;

    private static bool Crossed(float a, float b, float threshold) =>
        (a < threshold && b >= threshold) || (a > threshold && b <= threshold)
        || (NearlyEqual(b, threshold) && !NearlyEqual(a, threshold));

    private static bool NearlyEqual(float a, float b) => MathF.Abs(a - b) <= 1e-3f;
}

/// <summary>
/// Opacity state machine for the Zoom HUD pill: activity → 100%, hold 700 ms,
/// then ease to rest fade (never to 0). Hover / keyboard focus restores 100%.
/// </summary>
public sealed class SmoothZoomHudFade
{
    private enum Phase
    {
        Active,
        Holding,
        Fading,
        Rest
    }

    private Phase _phase = Phase.Rest;
    private float _opacity = SmoothZoomHud.FadeOpacityDark;
    private float _holdElapsed;
    private float _fadeElapsed;
    private float _appearElapsed;
    private float _fromOpacity;
    private float _restOpacity = SmoothZoomHud.FadeOpacityDark;
    private bool _pointerOrFocus;

    public float Opacity => _opacity;

    public bool IsResting => _phase is Phase.Rest or Phase.Fading;

    public void SetTheme(bool isLightTheme) =>
        _restOpacity = SmoothZoomHud.RestOpacity(isLightTheme);

    public void SetPointerOrFocus(bool active)
    {
        _pointerOrFocus = active;
        if (active)
        {
            SnapActive();
        }
        else if (_phase == Phase.Active)
        {
            BeginHold();
        }
    }

    /// <summary>Zoom or pan activity — appear (or stay) at 100%.</summary>
    public void NotifyActivity()
    {
        if (_pointerOrFocus)
        {
            SnapActive();
            return;
        }

        if (_phase is Phase.Rest or Phase.Fading)
        {
            _fromOpacity = _opacity;
            _appearElapsed = 0f;
            _phase = Phase.Active;
            if (_fromOpacity >= 0.999f)
            {
                _opacity = 1f;
                BeginHold();
            }
        }
        else
        {
            SnapActive();
        }
    }

    public float Tick(float dtSeconds)
    {
        dtSeconds = Math.Clamp(dtSeconds, 0f, SmoothViewport.MaxFrameDtSeconds);
        if (_pointerOrFocus)
        {
            _opacity = 1f;
            _phase = Phase.Active;
            return _opacity;
        }

        switch (_phase)
        {
            case Phase.Active:
                _appearElapsed += dtSeconds * 1000f;
                var appearT = Math.Clamp(_appearElapsed / SmoothZoomHud.AppearMs, 0f, 1f);
                _opacity = _fromOpacity + ((1f - _fromOpacity) * EaseOut(appearT));
                if (appearT >= 1f)
                {
                    _opacity = 1f;
                    BeginHold();
                }

                break;
            case Phase.Holding:
                _holdElapsed += dtSeconds * 1000f;
                if (_holdElapsed >= SmoothZoomHud.HoldMs)
                {
                    _fromOpacity = _opacity;
                    _fadeElapsed = 0f;
                    _phase = Phase.Fading;
                }

                break;
            case Phase.Fading:
                _fadeElapsed += dtSeconds * 1000f;
                var fadeT = Math.Clamp(_fadeElapsed / SmoothZoomHud.FadeMs, 0f, 1f);
                _opacity = _fromOpacity + ((_restOpacity - _fromOpacity) * EaseIn(fadeT));
                if (fadeT >= 1f)
                {
                    _opacity = _restOpacity;
                    _phase = Phase.Rest;
                }

                break;
            case Phase.Rest:
                _opacity = _restOpacity;
                break;
        }

        return _opacity;
    }

    private void SnapActive()
    {
        _opacity = 1f;
        _fromOpacity = 1f;
        _appearElapsed = SmoothZoomHud.AppearMs;
        BeginHold();
    }

    private void BeginHold()
    {
        _phase = Phase.Holding;
        _holdElapsed = 0f;
        _opacity = 1f;
    }

    private static float EaseOut(float t) => 1f - ((1f - t) * (1f - t));

    private static float EaseIn(float t) => t * t;
}
