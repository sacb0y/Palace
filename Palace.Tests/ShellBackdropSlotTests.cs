using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ShellBackdropSlotTests
{
    private const string Petal = "ms-appx:///Assets/Backgrounds/petal.jpg";
    private const string Aurora = "ms-appx:///Assets/Backgrounds/aurora.jpg";

    [Fact]
    public void IsSlotReady_preset_to_other_preset_rebuilds()
    {
        // Petal → Aurora: bound path still Petal while imagePath is Aurora.
        Assert.False(Ready(
            wantSurface: true,
            hasSurface: true,
            boundPath: Petal,
            imagePath: Aurora,
            boundLight: false,
            light: false));
    }

    [Fact]
    public void IsSlotReady_same_path_theme_flip_rebuilds()
    {
        Assert.False(Ready(
            wantSurface: true,
            hasSurface: true,
            boundPath: Petal,
            imagePath: Petal,
            boundLight: false,
            light: true));
    }

    [Fact]
    public void IsSlotReady_same_path_and_theme_keeps_slot()
    {
        Assert.True(Ready(
            wantSurface: true,
            hasSurface: true,
            boundPath: Petal,
            imagePath: Petal,
            boundLight: true,
            light: true));
    }

    [Fact]
    public void IsSlotReady_custom_path_case_only_keeps_slot()
    {
        const string lower = @"C:\LocalState\shell-recent-abc.jpg";
        const string mixed = @"C:\LocalState\Shell-Recent-ABC.jpg";
        Assert.True(Ready(
            wantSurface: true,
            hasSurface: true,
            boundPath: lower,
            imagePath: mixed,
            boundLight: false,
            light: false));
    }

    [Fact]
    public void IsSlotReady_backdrop_when_no_surface()
    {
        Assert.True(ShellBackdrop.IsSlotReady(
            hasVisual: true,
            hostMatches: true,
            compositorMatches: true,
            hasEffectBrush: true,
            wantSurface: false,
            hasSurfaceSourceBrush: false,
            hasBackdropSourceBrush: true,
            boundSourcePath: null,
            imagePath: null,
            boundSourceIsLight: false,
            light: false));

        Assert.False(ShellBackdrop.IsSlotReady(
            hasVisual: true,
            hostMatches: true,
            compositorMatches: true,
            hasEffectBrush: true,
            wantSurface: false,
            hasSurfaceSourceBrush: false,
            hasBackdropSourceBrush: false,
            boundSourcePath: null,
            imagePath: null,
            boundSourceIsLight: false,
            light: false));
    }

    [Fact]
    public void SystemBackdrop_theme_flip_does_not_recreate_while_mica_stays()
    {
        // Light/Dark flip rebuilds glass BoundSourceIsLight via IsSlotReady, but
        // Window.SystemBackdrop must keep the same Mica instance (AV otherwise).
        Assert.False(ShellBackground.ShouldRecreateSystemBackdrop(wantMica: true, currentlyHasMica: true));
    }

    private static bool Ready(
        bool wantSurface,
        bool hasSurface,
        string? boundPath,
        string? imagePath,
        bool boundLight,
        bool light) =>
        ShellBackdrop.IsSlotReady(
            hasVisual: true,
            hostMatches: true,
            compositorMatches: true,
            hasEffectBrush: true,
            wantSurface: wantSurface,
            hasSurfaceSourceBrush: hasSurface,
            hasBackdropSourceBrush: false,
            boundSourcePath: boundPath,
            imagePath: imagePath,
            boundSourceIsLight: boundLight,
            light: light);
}
