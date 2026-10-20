namespace Palace.Helpers;

/// <summary>
/// Shared metrics and auto-hide / disable policy for translucent circular viewer
/// chrome (<c>ViewerOverlayButtonStyle</c>). Used by overlay Close / Prev / Next
/// today; plate size is caller-owned so the same style can host smaller removes
/// or an Info toggle later. Off WinUI.
/// </summary>
public static class ViewerOverlayChrome
{
    public const int IdleHideMs = 2500;
    public const int FadeOutMs = 200;
    public const int FadeInMs = 100;

    public const double NavPlateDip = 40;
    public const double ClosePlateDip = 36;
    public const double MinHitTargetDip = 44;
    public const double EdgeInsetDip = 16;
    public const double GlyphDip = 16;

    /// <summary>Rest plate fill alpha (#000).</summary>
    public const double PlateRestOpacity = 0.50;

    /// <summary>Hover plate fill alpha (#000).</summary>
    public const double PlateHoverOpacity = 0.60;

    /// <summary>Pressed plate fill alpha (#000).</summary>
    public const double PlatePressedOpacity = 0.72;

    /// <summary>Disabled plate fill alpha (#000).</summary>
    public const double PlateDisabledOpacity = 0.30;

    /// <summary>Rest / hover glyph opacity (white).</summary>
    public const double GlyphRestOpacity = 1.0;

    /// <summary>Pressed glyph opacity (white).</summary>
    public const double GlyphPressedOpacity = 0.85;

    /// <summary>Disabled glyph opacity (white).</summary>
    public const double GlyphDisabledOpacity = 0.40;

    /// <summary>Default border alpha (#FFF).</summary>
    public const double BorderOpacity = 0.20;

    public const string PreviousName = "Previous";
    public const string NextName = "Next";
    public const string CloseName = "Close";

    public const string PreviousTooltip = "Previous (Left)";
    public const string NextTooltip = "Next (Right)";
    public const string CloseTooltip = "Close (Esc)";

    /// <summary>
    /// Auto-hide only when animations are on and a screen reader is not running.
    /// When either guard is true, chrome stays visible.
    /// </summary>
    public static bool AllowsAutoHide(bool animationsEnabled, bool screenReaderRunning) =>
        animationsEnabled && !screenReaderRunning;

    /// <summary>
    /// Idle hide is blocked while a chrome control is hovered or keyboard-focused,
    /// or when <see cref="AllowsAutoHide"/> is false.
    /// </summary>
    public static bool MayHide(
        bool isHovered,
        bool isFocused,
        bool animationsEnabled,
        bool screenReaderRunning) =>
        AllowsAutoHide(animationsEnabled, screenReaderRunning) && !isHovered && !isFocused;

    /// <summary>
    /// After <paramref name="idleMs"/> of no pointer/key activity, hide when
    /// <see cref="MayHide"/> allows it.
    /// </summary>
    public static bool ShouldHideAfterIdle(
        int idleMs,
        bool isHovered,
        bool isFocused,
        bool animationsEnabled,
        bool screenReaderRunning) =>
        idleMs >= IdleHideMs
        && MayHide(isHovered, isFocused, animationsEnabled, screenReaderRunning);

    /// <summary>Target opacity for the chrome host (faded keeps tab order).</summary>
    public static double ChromeOpacity(bool visible) => visible ? 1.0 : 0.0;

    /// <summary>Fade duration in ms for the requested visibility change.</summary>
    public static int FadeDurationMs(bool visible) =>
        visible ? FadeInMs : FadeOutMs;

    /// <summary>
    /// When animations are off, skip the fade and snap. When a screen reader is
    /// on, never fade away (opacity stays 1).
    /// </summary>
    public static bool ShouldAnimateFade(bool animationsEnabled, bool screenReaderRunning) =>
        AllowsAutoHide(animationsEnabled, screenReaderRunning);

    public static bool CanGoPrevious(int index) => index > 0;

    public static bool CanGoNext(int index, int count) =>
        count > 0 && index >= 0 && index < count - 1;
}
