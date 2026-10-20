using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ViewerOverlayChromeTests
{
    [Fact]
    public void Metrics_MatchGooeySpec()
    {
        Assert.Equal(2500, ViewerOverlayChrome.IdleHideMs);
        Assert.Equal(200, ViewerOverlayChrome.FadeOutMs);
        Assert.Equal(100, ViewerOverlayChrome.FadeInMs);
        Assert.Equal(40, ViewerOverlayChrome.NavPlateDip);
        Assert.Equal(36, ViewerOverlayChrome.ClosePlateDip);
        Assert.Equal(44, ViewerOverlayChrome.MinHitTargetDip);
        Assert.Equal(16, ViewerOverlayChrome.EdgeInsetDip);
        Assert.Equal(16, ViewerOverlayChrome.GlyphDip);
        Assert.Equal(0.50, ViewerOverlayChrome.PlateRestOpacity);
        Assert.Equal(0.60, ViewerOverlayChrome.PlateHoverOpacity);
        Assert.Equal(0.72, ViewerOverlayChrome.PlatePressedOpacity);
        Assert.Equal(0.30, ViewerOverlayChrome.PlateDisabledOpacity);
        Assert.Equal(0.85, ViewerOverlayChrome.GlyphPressedOpacity);
        Assert.Equal(0.40, ViewerOverlayChrome.GlyphDisabledOpacity);
        Assert.Equal(0.20, ViewerOverlayChrome.BorderOpacity);
    }

    [Fact]
    public void Tooltips_IncludeShortcuts()
    {
        Assert.Equal("Previous (Left)", ViewerOverlayChrome.PreviousTooltip);
        Assert.Equal("Next (Right)", ViewerOverlayChrome.NextTooltip);
        Assert.Equal("Close (Esc)", ViewerOverlayChrome.CloseTooltip);
        Assert.Equal("Previous", ViewerOverlayChrome.PreviousName);
        Assert.Equal("Next", ViewerOverlayChrome.NextName);
        Assert.Equal("Close", ViewerOverlayChrome.CloseName);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void AllowsAutoHide_RequiresAnimationsAndNoScreenReader(
        bool animationsEnabled,
        bool screenReaderRunning,
        bool expected) =>
        Assert.Equal(expected, ViewerOverlayChrome.AllowsAutoHide(animationsEnabled, screenReaderRunning));

    [Theory]
    [InlineData(false, false, true, false, true)]
    [InlineData(true, false, true, false, false)]
    [InlineData(false, true, true, false, false)]
    [InlineData(false, false, false, false, false)]
    [InlineData(false, false, true, true, false)]
    public void MayHide_BlocksHoverFocusNarratorAndNoAnim(
        bool isHovered,
        bool isFocused,
        bool animationsEnabled,
        bool screenReaderRunning,
        bool expected) =>
        Assert.Equal(
            expected,
            ViewerOverlayChrome.MayHide(isHovered, isFocused, animationsEnabled, screenReaderRunning));

    [Fact]
    public void ShouldHideAfterIdle_NeedsFullIdleWindow()
    {
        Assert.False(ViewerOverlayChrome.ShouldHideAfterIdle(
            idleMs: 2499,
            isHovered: false,
            isFocused: false,
            animationsEnabled: true,
            screenReaderRunning: false));
        Assert.True(ViewerOverlayChrome.ShouldHideAfterIdle(
            idleMs: 2500,
            isHovered: false,
            isFocused: false,
            animationsEnabled: true,
            screenReaderRunning: false));
        Assert.False(ViewerOverlayChrome.ShouldHideAfterIdle(
            idleMs: 5000,
            isHovered: true,
            isFocused: false,
            animationsEnabled: true,
            screenReaderRunning: false));
    }

    [Fact]
    public void ChromeOpacity_AndFadeDuration()
    {
        Assert.Equal(1.0, ViewerOverlayChrome.ChromeOpacity(visible: true));
        Assert.Equal(0.0, ViewerOverlayChrome.ChromeOpacity(visible: false));
        Assert.Equal(100, ViewerOverlayChrome.FadeDurationMs(visible: true));
        Assert.Equal(200, ViewerOverlayChrome.FadeDurationMs(visible: false));
    }

    [Fact]
    public void ShouldAnimateFade_FollowsAutoHideGuards()
    {
        Assert.True(ViewerOverlayChrome.ShouldAnimateFade(animationsEnabled: true, screenReaderRunning: false));
        Assert.False(ViewerOverlayChrome.ShouldAnimateFade(animationsEnabled: false, screenReaderRunning: false));
        Assert.False(ViewerOverlayChrome.ShouldAnimateFade(animationsEnabled: true, screenReaderRunning: true));
    }

    [Fact]
    public void CanGoPrevious_FirstItemDisabled()
    {
        Assert.False(ViewerOverlayChrome.CanGoPrevious(0));
        Assert.True(ViewerOverlayChrome.CanGoPrevious(1));
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(0, 2, true)]
    [InlineData(1, 2, false)]
    [InlineData(-1, 3, false)]
    [InlineData(0, 0, false)]
    public void CanGoNext_LastItemDisabled(int index, int count, bool expected) =>
        Assert.Equal(expected, ViewerOverlayChrome.CanGoNext(index, count));
}
