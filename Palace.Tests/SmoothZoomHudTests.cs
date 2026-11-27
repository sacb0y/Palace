using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothZoomHudTests
{
    [Fact]
    public void RestOpacity_LightIsTwoPointsHigher()
    {
        Assert.Equal(0.20f, SmoothZoomHud.RestOpacity(isLightTheme: false));
        Assert.Equal(0.22f, SmoothZoomHud.RestOpacity(isLightTheme: true));
    }

    [Theory]
    [InlineData(1f, "100%")]
    [InlineData(2.4f, "240%")]
    [InlineData(32f, "3200%")]
    [InlineData(0.15f, "15%")]
    public void FormatPercent_MatchesSpecReadouts(float zoom, string expected)
    {
        Assert.Equal(expected, SmoothZoomHud.FormatPercent(zoom));
    }

    [Fact]
    public void AnnounceText_SaysZoomNPercent()
    {
        Assert.Equal("Zoom 240 percent", SmoothZoomHud.AnnounceText(2.4f));
    }

    [Fact]
    public void IsAtFit_And_Actual()
    {
        Assert.True(SmoothZoomHud.IsAtFit(0.2f, fitZoom: 0.2f));
        Assert.False(SmoothZoomHud.IsAtFit(1f, fitZoom: 0.2f));
        Assert.True(SmoothZoomHud.IsAtActual(1f));
        Assert.False(SmoothZoomHud.IsAtActual(2f));
    }

    [Fact]
    public void ShouldAnnounce_OnGestureEndAndThresholdCrossings()
    {
        Assert.True(SmoothZoomHud.ShouldAnnounce(2.3f, 2.4f, gestureEnded: true));
        Assert.False(SmoothZoomHud.ShouldAnnounce(2.4f, 2.4f, gestureEnded: true));
        Assert.True(SmoothZoomHud.ShouldAnnounce(0.9f, 1.1f, gestureEnded: false)); // 100%
        Assert.True(SmoothZoomHud.ShouldAnnounce(7.5f, 8.5f, gestureEnded: false)); // 8×
        Assert.True(SmoothZoomHud.ShouldAnnounce(31f, 32f, gestureEnded: false)); // 32×
        Assert.True(SmoothZoomHud.ShouldAnnounce(0.3f, 0.1f, fitZoom: 0.2f, gestureEnded: false));
        Assert.False(SmoothZoomHud.ShouldAnnounce(2.4f, 2.4f, fitZoom: 0.2f, gestureEnded: false));
    }

    [Fact]
    public void DrawnGridOpacity_OnlyFrom8xWhenArmed()
    {
        Assert.Equal(0f, SmoothZoomHud.DrawnGridOpacity(armed: false, zoom: 32f));
        Assert.Equal(0f, SmoothZoomHud.DrawnGridOpacity(armed: true, zoom: 4f));
        Assert.Equal(0f, SmoothZoomHud.DrawnGridOpacity(armed: true, zoom: 8f), precision: 3);
        Assert.Equal(0.4f, SmoothZoomHud.DrawnGridOpacity(armed: true, zoom: 8.8f), precision: 2);
        Assert.Equal(1f, SmoothZoomHud.DrawnGridOpacity(armed: true, zoom: 10f));
        Assert.Equal(1f, SmoothZoomHud.DrawnGridOpacity(armed: true, zoom: 32f));
    }

    [Fact]
    public void PackGridOpacity_IsToggleOnly()
    {
        Assert.Equal(0f, SmoothZoomHud.PackGridOpacity(armed: false));
        Assert.Equal(1f, SmoothZoomHud.PackGridOpacity(armed: true));
    }

    [Fact]
    public void GridTooltip_Mentions8xWhenDimmed()
    {
        Assert.Contains("8×", SmoothZoomHud.GridTooltip(4f), StringComparison.Ordinal);
        Assert.Equal("Pixel grid (G)", SmoothZoomHud.GridTooltip(8f));
        Assert.True(SmoothZoomHud.IsGridIconDimmed(4f));
        Assert.False(SmoothZoomHud.IsGridIconDimmed(8f));
    }

    [Fact]
    public void Shortcuts_FitActualGridZoom()
    {
        Assert.True(SmoothZoomHud.RequestsFit(GalleryScale.KeyNumber0));
        Assert.True(SmoothZoomHud.RequestsActual(GalleryScale.KeyNumber1));
        Assert.True(SmoothZoomHud.TogglesGrid(controlDown: false, SmoothZoomHud.KeyLetterG));
        Assert.False(SmoothZoomHud.TogglesGrid(controlDown: true, SmoothZoomHud.KeyLetterG));
        Assert.True(SmoothZoomHud.ZoomsIn(SmoothZoomHud.KeyOemPlus));
        Assert.True(SmoothZoomHud.ZoomsOut(SmoothZoomHud.KeyOemMinus));
    }

    [Fact]
    public void Fade_ActivityHoldsThenRestsNeverZero()
    {
        var fade = new SmoothZoomHudFade();
        fade.SetTheme(isLightTheme: false);
        fade.NotifyActivity();
        // Appear ~80 ms
        for (var i = 0; i < 6; i++)
        {
            fade.Tick(0.016f);
        }

        Assert.Equal(1f, fade.Opacity, precision: 2);

        // Hold 700 ms
        for (var i = 0; i < 50; i++)
        {
            fade.Tick(0.016f);
        }

        Assert.True(fade.Opacity <= 1f);
        // Finish fade 250 ms
        for (var i = 0; i < 30; i++)
        {
            fade.Tick(0.016f);
        }

        Assert.Equal(SmoothZoomHud.FadeOpacityDark, fade.Opacity, precision: 2);
        Assert.True(fade.Opacity > 0f);
    }

    [Fact]
    public void Fade_HoverRestoresFullOpacity()
    {
        var fade = new SmoothZoomHudFade();
        fade.SetTheme(isLightTheme: true);
        fade.NotifyActivity();
        for (var i = 0; i < 80; i++)
        {
            fade.Tick(0.016f);
        }

        Assert.Equal(SmoothZoomHud.FadeOpacityLight, fade.Opacity, precision: 2);
        fade.SetPointerOrFocus(true);
        Assert.Equal(1f, fade.Opacity);
        fade.SetPointerOrFocus(false);
        fade.Tick(0.016f);
        Assert.Equal(1f, fade.Opacity, precision: 2); // restart hold
    }

    [Fact]
    public void PackViewCb_WritesGridOpacityAndBands()
    {
        var dest = new float[32];
        var m = SmoothViewport.Affine2x3.FromZoomPan(1f, 0f, 0f);
        SmoothViewport.PackViewCb(dest, m, 100, 50, zoom: 8f, gridOpacity: 1f);
        Assert.Equal(1f, dest[24]);
        Assert.Equal(SmoothViewport.GridZoomStart, dest[25]);
        Assert.Equal(SmoothViewport.GridZoomFull, dest[26]);
    }
}
