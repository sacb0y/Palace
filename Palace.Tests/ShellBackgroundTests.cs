using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class ShellBackgroundTests
{
    public ShellBackgroundTests() => Reset();

    [Fact]
    public void ParsePreset_UnknownFallsBackToDefault()
    {
        Assert.Equal(ShellPresetKind.Default, ShellBackground.ParsePreset(null));
        Assert.Equal(ShellPresetKind.Default, ShellBackground.ParsePreset(""));
        Assert.Equal(ShellPresetKind.Default, ShellBackground.ParsePreset("nope"));
        Assert.Equal(ShellPresetKind.Petal, ShellBackground.ParsePreset("Petal"));
        Assert.Equal(ShellPresetKind.Ribbons, ShellBackground.ParsePreset("ribbons"));
        Assert.Equal(ShellPresetKind.MicaAlt, ShellBackground.ParsePreset("micaalt"));
    }

    [Fact]
    public void ParsePreset_MigratesLegacyNames()
    {
        Assert.Equal(ShellPresetKind.Default, ShellBackground.ParsePreset("Dusk"));
        Assert.Equal(ShellPresetKind.Neutral, ShellBackground.ParsePreset("Slate"));
        Assert.Equal(ShellPresetKind.Aero, ShellBackground.ParsePreset("Grain"));
    }

    [Fact]
    public void ResolvePreset_MigratesWallpaperPathToCustom()
    {
        Assert.Equal(ShellPresetKind.Default, ShellBackground.ResolvePreset(null, null));
        Assert.Equal(ShellPresetKind.Custom, ShellBackground.ResolvePreset(null, @"C:\wall.jpg"));
        Assert.Equal(ShellPresetKind.Default, ShellBackground.ResolvePreset("Custom", null));
        Assert.Equal(ShellPresetKind.Default, ShellBackground.ResolvePreset("Dusk", null));
        Assert.Equal(ShellPresetKind.Custom, ShellBackground.ResolvePreset("Custom", @"C:\wall.jpg"));
    }

    [Fact]
    public void Apply_PresetChange_DoesNotBumpEpochUnlessImageLayer()
    {
        ShellBackground.Apply(ShellPresetKind.Default, null, 40, 45, false, ShellBackground.DefaultTintHex);
        var epoch = ShellBackground.WallpaperEpoch;
        ShellBackground.Apply(ShellPresetKind.Neutral, null, 40, 45, false, ShellBackground.DefaultTintHex);
        Assert.Equal(ShellPresetKind.Neutral, ShellBackground.Preset);
        Assert.Equal(epoch, ShellBackground.WallpaperEpoch);

        ShellBackground.Apply(ShellPresetKind.Ribbons, null, 40, 45, false, ShellBackground.DefaultTintHex);
        Assert.Equal(ShellPresetKind.Ribbons, ShellBackground.Preset);
        Assert.True(ShellBackground.WallpaperEpoch > epoch);
        Assert.True(ShellBackground.UsesImageLayer);
        Assert.False(ShellBackground.HasWallpaper);
        Assert.Equal(
            "ms-appx:///Assets/Backgrounds/ribbons-dark.jpg",
            ShellBackground.BoundImagePath(false));
        Assert.Equal(
            "ms-appx:///Assets/Backgrounds/ribbons-light.jpg",
            ShellBackground.BoundImagePath(true));
    }

    [Fact]
    public void BlurAndDarkness_ApplyFlags_MatchPreset()
    {
        ShellBackground.Apply(ShellPresetKind.Default, null, 40, 45, false, ShellBackground.DefaultTintHex);
        Assert.True(ShellBackground.BlurApplies);
        Assert.True(ShellBackground.DarknessApplies);
        Assert.False(ShellBackground.UsesSystemBackdrop);

        ShellBackground.Apply(ShellPresetKind.Mica, null, 40, 45, false, ShellBackground.DefaultTintHex);
        Assert.False(ShellBackground.BlurApplies);
        Assert.False(ShellBackground.DarknessApplies);
        Assert.True(ShellBackground.UsesSystemBackdrop);

        ShellBackground.Apply(ShellPresetKind.Neutral, null, 40, 45, false, ShellBackground.DefaultTintHex);
        Assert.False(ShellBackground.BlurApplies);
        Assert.True(ShellBackground.DarknessApplies);

        ShellBackground.Apply(ShellPresetKind.Capsules, null, 45, 45, false, ShellBackground.DefaultTintHex);
        Assert.True(ShellBackground.BlurApplies);
        Assert.True(ShellBackground.DarknessApplies);
        Assert.True(ShellBackground.UsesImageLayer);
    }

    [Fact]
    public void DarknessAndBlurKeys_ArePerPreset()
    {
        Assert.Equal("ShellDarkness_Nebula", ShellBackground.DarknessKeyFor(ShellPresetKind.Nebula));
        Assert.Equal("ShellBlur_Ribbons", ShellBackground.BlurKeyFor(ShellPresetKind.Ribbons));
    }

    [Fact]
    public void BlurRadius_MapsPercentWithEaseInGamma()
    {
        Assert.Equal(0f, ShellBackground.BlurRadius(0), 2);
        Assert.Equal(ShellBackground.MaxBlurRadius, ShellBackground.BlurRadius(100), 2);
        Assert.Equal(ShellBackground.MaxBlurRadius, ShellBackground.BlurRadius(400), 2);
        Assert.Equal(0f, ShellBackground.BlurRadius(-10), 2);

        // Low end is softer than linear (gamma > 1) so 0→10 is not a hard jump.
        var at10 = ShellBackground.BlurRadius(10);
        var linear10 = ShellBackground.MaxBlurRadius * 0.10f;
        Assert.True(at10 < linear10);
        Assert.True(at10 > 0f);
        Assert.Equal(
            (float)(ShellBackground.MaxBlurRadius * Math.Pow(0.10, ShellBackground.BlurAmountGamma)),
            at10,
            2);
        Assert.Equal(
            (float)(ShellBackground.MaxBlurRadius * Math.Pow(0.50, ShellBackground.BlurAmountGamma)),
            ShellBackground.BlurRadius(50),
            2);
        // Monotonic: more percent → more radius.
        Assert.True(ShellBackground.BlurRadius(10) < ShellBackground.BlurRadius(25));
        Assert.True(ShellBackground.BlurRadius(25) < ShellBackground.BlurRadius(50));
        Assert.True(ShellBackground.BlurRadius(50) < ShellBackground.BlurRadius(100));
    }

    [Fact]
    public void BlurEdgePad_CoversSoftKernel()
    {
        Assert.Equal(0f, ShellBackground.BlurEdgePad(0));
        Assert.Equal(0f, ShellBackground.BlurEdgePad(0.005f));
        Assert.Equal(
            MathF.Ceiling(ShellBackground.MaxBlurRadius * ShellBackground.BlurEdgeKernel),
            ShellBackground.BlurEdgePad(ShellBackground.MaxBlurRadius));
        Assert.True(ShellBackground.BlurEdgePad(10f) >= 30f);
    }

    [Fact]
    public void BlurVisualPadDip_CeilingsInPhysicalPixels()
    {
        // 150% DPI: DIP pad 192 → 288 physical → 192 DIP again (exact).
        var at150 = ShellBackground.BlurVisualPadDip(ShellBackground.MaxBlurRadius, 1.5);
        Assert.Equal(
            ShellBackground.BlurEdgePad(ShellBackground.MaxBlurRadius),
            at150,
            3);

        // Fractional DIP×scale must ceiling so coverage never undershoots.
        var pad10 = ShellBackground.BlurEdgePad(10f);
        var scale = 1.25;
        var expected = (float)(Math.Ceiling(pad10 * scale) / scale);
        Assert.Equal(expected, ShellBackground.BlurVisualPadDip(10f, scale), 3);
        Assert.True(ShellBackground.BlurVisualPadDip(0, 1.5) >= ShellBackground.BlurCoverageMinPhysicalPx / 1.5f);
    }

    [Fact]
    public void BlurHostSizeDip_SnapsUpToPhysicalPixels()
    {
        Assert.Equal(0f, ShellBackground.BlurHostSizeDip(0, 1.5));
        // 100.2 DIP @ 150% → 150.3 px → ceil 151 → 151/1.5 DIP
        Assert.Equal((float)(151.0 / 1.5), ShellBackground.BlurHostSizeDip(100.2, 1.5), 4);
        Assert.Equal(800f, ShellBackground.BlurHostSizeDip(800, 1.0), 3);
    }

    [Fact]
    public void BlurHidesWallpaper_WhenBlurAppliesAndRadiusPositive()
    {
        ShellBackground.Apply(ShellPresetKind.Blueprint, null, 40, 100, false, null);
        Assert.True(ShellBackground.BlurHidesWallpaper(100));
        Assert.True(ShellBackground.BlurHidesWallpaper(10));
        Assert.False(ShellBackground.BlurHidesWallpaper(0));

        ShellBackground.Apply(ShellPresetKind.Neutral, null, 40, 100, false, null);
        Assert.False(ShellBackground.BlurHidesWallpaper(100));
    }

    [Fact]
    public void QuantizedBlurRadius_StepsAndCacheKey()
    {
        Assert.Equal(0, ShellBackground.QuantizedBlurRadius(0));
        Assert.Equal(
            ShellBackground.QuantizeRadiusDip(ShellBackground.BlurRadius(25)),
            ShellBackground.QuantizedBlurRadius(25));
        Assert.Equal(
            ShellBackground.QuantizeRadiusDip(ShellBackground.BlurRadius(50)),
            ShellBackground.QuantizedBlurRadius(50));
        Assert.Equal(64, ShellBackground.QuantizedBlurRadius(100));
        Assert.Equal(
            $"Ribbons:D:{ShellBackground.QuantizedBlurRadius(50)}",
            ShellBackground.BlurCacheKey(ShellPresetKind.Ribbons, false, 50));
        Assert.Equal(
            $"Ribbons:L:{ShellBackground.QuantizedBlurRadius(25)}",
            ShellBackground.BlurCacheKey(ShellPresetKind.Ribbons, true, 25));
    }

    [Fact]
    public void DefaultAmounts_FollowManifestPerTheme()
    {
        Assert.Equal(45, ShellBackground.DefaultDarknessFor(ShellPresetKind.Ribbons), 2);
        Assert.Equal(45, ShellBackground.DefaultBlurFor(ShellPresetKind.Ribbons), 2);
        Assert.Equal(35, ShellBackground.DefaultDarknessFor(ShellPresetKind.Bokeh), 2);
        Assert.Equal(35, ShellBackground.DefaultBlurFor(ShellPresetKind.Nebula), 2);
        Assert.Equal(40, ShellBackground.DefaultDarknessFor(ShellPresetKind.Aero), 2);
        Assert.Equal(40, ShellBackground.DefaultDarknessFor(ShellPresetKind.Default), 2);
        Assert.Equal(40, ShellBackground.ParseDarknessFor(ShellPresetKind.Aurora, null), 2);
        Assert.Equal(70, ShellBackground.ParseDarknessFor(ShellPresetKind.Aurora, 70), 2);

        Assert.Equal(20, ShellBackground.DefaultDarknessFor(ShellPresetKind.Ribbons, lightChrome: true), 2);
        Assert.Equal(35, ShellBackground.DefaultDarknessFor(ShellPresetKind.Aero, lightChrome: true), 2);
        Assert.Equal(15, ShellBackground.DefaultDarknessFor(ShellPresetKind.Blueprint, lightChrome: true), 2);
        Assert.Equal(45, ShellBackground.DefaultBlurFor(ShellPresetKind.Blueprint, lightChrome: true), 2);
        Assert.Equal(30, ShellBackground.DefaultBlurFor(ShellPresetKind.Pixel), 2);
        Assert.Equal(15, ShellBackground.DefaultDarknessFor(ShellPresetKind.Nodes, lightChrome: true), 2);
        Assert.Equal(40, ShellBackground.DefaultBlurFor(ShellPresetKind.Nodes, lightChrome: true), 2);
        Assert.Equal(30, ShellBackground.DefaultDarknessFor(ShellPresetKind.Nodes), 2);
    }

    [Fact]
    public void DesignedPresets_UseThemeThumbsAndFinalNames()
    {
        Assert.Equal(ShellPresetGroup.Vivid, ShellBackground.PresetGroup(ShellPresetKind.Aero));
        Assert.Equal(ShellPresetGroup.Creator, ShellBackground.PresetGroup(ShellPresetKind.Nodes));
        Assert.Equal(ShellPresetGroup.None, ShellBackground.PresetGroup(ShellPresetKind.Custom));
        Assert.Equal("Aero", ShellBackground.PresetDisplayName(ShellPresetKind.Aero));
        Assert.Equal("Bokeh", ShellBackground.PresetDisplayName(ShellPresetKind.Bokeh));
        Assert.Equal("Node Graph", ShellBackground.PresetDisplayName(ShellPresetKind.Nodes));
        Assert.Equal(ShellPresetKind.Nodes, ShellBackground.ParsePreset("nodes"));
        Assert.Equal(ShellPresetKind.Nodes, ShellBackground.ParsePreset("Node Graph"));
        Assert.Equal("nodes", ShellBackground.PresetAssetStem(ShellPresetKind.Nodes));
        Assert.True(ShellBackground.IsPackagedImagePreset(ShellPresetKind.Viewport));
        Assert.False(ShellBackground.IsPackagedImagePreset(ShellPresetKind.Neutral));
        Assert.Equal(
            "ms-appx:///Assets/Backgrounds/aero-dark-thumb.jpg",
            ShellBackground.PackagedThumbUri(ShellPresetKind.Aero, false));
        Assert.Equal(
            "ms-appx:///Assets/Backgrounds/aero-light-thumb.jpg",
            ShellBackground.PackagedThumbUri(ShellPresetKind.Aero, true));
        Assert.Equal(
            "ms-appx:///Assets/Backgrounds/nodes-light-thumb.jpg",
            ShellBackground.PackagedThumbUri(ShellPresetKind.Nodes, true));
        Assert.Equal(
            "ms-appx:///Assets/Backgrounds/ribbons-light-thumb.jpg",
            ShellBackground.PackagedThumbUri(ShellPresetKind.Ribbons, true));
        Assert.Equal(
            "ms-appx:///Assets/Backgrounds/ribbons-dark-thumb.jpg",
            ShellBackground.PackagedThumbUri(ShellPresetKind.Ribbons, false));
    }

    [Fact]
    public void PickerOrder_GroupsVividAndCreator()
    {
        Assert.Equal(
            [
                ShellPresetKind.Default,
                ShellPresetKind.Mica,
                ShellPresetKind.MicaAlt,
                ShellPresetKind.Neutral,
                ShellPresetKind.Ribbons,
                ShellPresetKind.Capsules,
                ShellPresetKind.Bokeh,
                ShellPresetKind.Nebula,
                ShellPresetKind.Aero,
                ShellPresetKind.Petal,
                ShellPresetKind.Facets,
                ShellPresetKind.Aurora,
                ShellPresetKind.Viewport,
                ShellPresetKind.Blueprint,
                ShellPresetKind.Sketchbook,
                ShellPresetKind.Pixel,
                ShellPresetKind.Nodes,
                ShellPresetKind.Custom
            ],
            ShellBackground.PickerOrder);
        Assert.Equal("Vivid", ShellBackground.PickerSections[1].Header);
        Assert.Equal(8, ShellBackground.PickerSections[1].Kinds.Length);
        Assert.Equal("Creator", ShellBackground.PickerSections[2].Header);
        Assert.Equal(
            [
                ShellPresetKind.Viewport,
                ShellPresetKind.Blueprint,
                ShellPresetKind.Sketchbook,
                ShellPresetKind.Pixel,
                ShellPresetKind.Nodes
            ],
            ShellBackground.PickerSections[2].Kinds);
    }

    [Fact]
    public void ParseEnabled_ReadsBoolAndString()
    {
        Assert.False(ShellBackground.ParseEnabled(null));
        Assert.True(ShellBackground.ParseEnabled(true));
        Assert.False(ShellBackground.ParseEnabled(false));
        Assert.True(ShellBackground.ParseEnabled("true"));
        Assert.False(ShellBackground.ParseEnabled("nope"));
    }

    [Fact]
    public void ParsePath_TrimsAndDropsEmpty()
    {
        Assert.Null(ShellBackground.ParsePath(null));
        Assert.Null(ShellBackground.ParsePath(""));
        Assert.Null(ShellBackground.ParsePath("  "));
        Assert.Null(ShellBackground.ParsePath(12));
        Assert.Equal(@"C:\wall.png", ShellBackground.ParsePath(@" C:\wall.png "));
    }

    [Fact]
    public void ParseDarknessAndBlur_ClampAndFallback()
    {
        Assert.Equal(ShellBackground.DefaultDarkness, ShellBackground.ParseDarkness(null), 2);
        Assert.Equal(ShellBackground.DefaultBlur, ShellBackground.ParseBlur(null), 2);
        Assert.Equal(25, ShellBackground.ParseDarkness(25.0), 2);
        Assert.Equal(80, ShellBackground.ParseBlur("80"), 2);
        Assert.Equal(0, ShellBackground.ParseDarkness(-12), 2);
        Assert.Equal(100, ShellBackground.ParseBlur(400), 2);
        Assert.Equal(33, ShellBackground.ParseDarkness(33L), 2);
    }

    [Fact]
    public void ParseTint_NormalizesHexAndFallsBack()
    {
        Assert.Equal(ShellBackground.DefaultTintHex, ShellBackground.ParseTint(null));
        Assert.Equal(ShellBackground.DefaultTintHex, ShellBackground.ParseTint("nope"));
        Assert.Equal("#FF336699", ShellBackground.ParseTint("#336699"));
        Assert.Equal("#502C3A6B", ShellBackground.ParseTint("0x502c3a6b"));
        Assert.True(ShellBackground.TryParseTint("#80AABBCC", out var a, out var r, out var g, out var b));
        Assert.Equal(0x80, a);
        Assert.Equal(0xAA, r);
        Assert.Equal(0xBB, g);
        Assert.Equal(0xCC, b);
        Assert.Equal("#80AABBCC", ShellBackground.NormalizeTint("#80aabbcc"));
        Assert.Null(ShellBackground.NormalizeTint("zzz"));
    }

    [Fact]
    public void Apply_SetsWallpaperDarknessBlurTint()
    {
        var fired = 0;
        void OnChanged(object? _, EventArgs e) => fired++;
        ShellBackground.Changed += OnChanged;
        try
        {
            ShellBackground.Apply(@"C:\pics\bg.jpg", -5, 250, true, "#336699");
            Assert.True(ShellBackground.HasWallpaper);
            Assert.Equal(@"C:\pics\bg.jpg", ShellBackground.WallpaperPath);
            Assert.Equal("bg.jpg", ShellBackground.WallpaperLabel);
            Assert.Equal(0, ShellBackground.Darkness, 2);
            Assert.Equal(100, ShellBackground.Blur, 2);
            Assert.True(ShellBackground.TintEnabled);
            Assert.Equal("#FF336699", ShellBackground.TintHex);
            Assert.Equal(1, fired);

            ShellBackground.Apply(@"C:\pics\bg.jpg", 0, 100, true, "#FF336699");
            Assert.Equal(1, fired);

            ShellBackground.Apply("  ", 40, 45, false, "bad");
            Assert.False(ShellBackground.HasWallpaper);
            Assert.Null(ShellBackground.WallpaperPath);
            Assert.Equal(ShellBackground.DefaultWallpaperLabel, ShellBackground.WallpaperLabel);
            Assert.Equal(40, ShellBackground.Darkness, 2);
            Assert.Equal(45, ShellBackground.Blur, 2);
            Assert.False(ShellBackground.TintEnabled);
            Assert.Equal(ShellBackground.DefaultTintHex, ShellBackground.TintHex);
            Assert.Equal(2, fired);
        }
        finally
        {
            ShellBackground.Changed -= OnChanged;
            Reset();
        }
    }

    [Fact]
    public void WallpaperLabel_PrefersChosenNameOverStoredCopy()
    {
        try
        {
            ShellBackground.Apply(@"C:\store\shell-wallpaper-abc.jpg", 40, 45, false, ShellBackground.DefaultTintHex);
            Assert.Equal("shell-wallpaper-abc.jpg", ShellBackground.WallpaperLabel);

            ShellBackground.SetWallpaperName("sunset.jpg");
            Assert.Equal("sunset.jpg", ShellBackground.WallpaperLabel);

            ShellBackground.Apply(null, 40, 45, false, ShellBackground.DefaultTintHex);
            Assert.Equal(ShellBackground.DefaultWallpaperLabel, ShellBackground.WallpaperLabel);
        }
        finally
        {
            Reset();
        }
    }

    [Fact]
    public void Apply_ReloadWallpaper_FiresWhenPathUnchanged()
    {
        ShellBackground.Apply(@"C:\pics\bg.jpg", 40, 45, false, ShellBackground.DefaultTintHex);
        var epoch = ShellBackground.WallpaperEpoch;
        var fired = 0;
        void OnChanged(object? _, EventArgs e) => fired++;
        ShellBackground.Changed += OnChanged;
        try
        {
            ShellBackground.Apply(@"C:\pics\bg.jpg", 40, 45, false, ShellBackground.DefaultTintHex);
            Assert.Equal(0, fired);
            Assert.Equal(epoch, ShellBackground.WallpaperEpoch);

            ShellBackground.Apply(@"C:\pics\bg.jpg", 40, 45, false, ShellBackground.DefaultTintHex, reloadWallpaper: true);
            Assert.Equal(1, fired);
            Assert.True(ShellBackground.WallpaperEpoch > epoch);
            Assert.Equal(@"C:\pics\bg.jpg", ShellBackground.WallpaperPath);
        }
        finally
        {
            ShellBackground.Changed -= OnChanged;
            Reset();
        }
    }

    [Fact]
    public void Apply_DimBlurTint_DoesNotBumpWallpaperEpoch()
    {
        ShellBackground.Apply(@"C:\pics\bg.jpg", 40, 45, false, ShellBackground.DefaultTintHex);
        var epoch = ShellBackground.WallpaperEpoch;
        var fired = 0;
        void OnChanged(object? _, EventArgs e) => fired++;
        ShellBackground.Changed += OnChanged;
        try
        {
            ShellBackground.Apply(@"C:\pics\bg.jpg", 70, 45, false, ShellBackground.DefaultTintHex);
            ShellBackground.Apply(@"C:\pics\bg.jpg", 70, 10, false, ShellBackground.DefaultTintHex);
            ShellBackground.Apply(@"C:\pics\bg.jpg", 70, 10, true, "#336699");
            Assert.Equal(3, fired);
            Assert.Equal(epoch, ShellBackground.WallpaperEpoch);
            Assert.Equal(70, ShellBackground.Darkness, 2);
            Assert.Equal(10, ShellBackground.Blur, 2);
            Assert.True(ShellBackground.TintEnabled);
        }
        finally
        {
            ShellBackground.Changed -= OnChanged;
            Reset();
        }
    }

    [Fact]
    public void ReleaseDisplay_AndRefresh_NotifyWithoutChangingPath()
    {
        ShellBackground.Apply(@"C:\pics\bg.jpg", 40, 45, false, ShellBackground.DefaultTintHex);
        var epoch = ShellBackground.WallpaperEpoch;
        var released = 0;
        var changed = 0;
        void OnRelease(object? _, EventArgs e) => released++;
        void OnChanged(object? _, EventArgs e) => changed++;
        ShellBackground.DisplayReleasing += OnRelease;
        ShellBackground.Changed += OnChanged;
        try
        {
            ShellBackground.ReleaseDisplay();
            Assert.Equal(1, released);
            Assert.Equal(0, changed);
            Assert.True(ShellBackground.DisplayHeld);
            Assert.False(ShellBackground.ShouldBindWallpaper);
            Assert.Equal(@"C:\pics\bg.jpg", ShellBackground.WallpaperPath);

            ShellBackground.Apply(@"C:\pics\bg.jpg", 70, 45, false, ShellBackground.DefaultTintHex);
            Assert.True(ShellBackground.DisplayHeld);
            Assert.False(ShellBackground.ShouldBindWallpaper);
            Assert.Equal(70, ShellBackground.Darkness, 2);
            Assert.Equal(epoch, ShellBackground.WallpaperEpoch);

            ShellBackground.Refresh();
            Assert.True(ShellBackground.DisplayHeld);
            Assert.False(ShellBackground.ShouldBindWallpaper);
            Assert.Equal(2, changed);

            ShellBackground.RestoreDisplay();
            Assert.Equal(3, changed);
            Assert.False(ShellBackground.DisplayHeld);
            Assert.True(ShellBackground.ShouldBindWallpaper);
            Assert.Equal(@"C:\pics\bg.jpg", ShellBackground.WallpaperPath);
            Assert.Equal(epoch, ShellBackground.WallpaperEpoch);

            ShellBackground.ReleaseDisplay();
            ShellBackground.Apply(@"C:\pics\new.jpg", 70, 10, false, ShellBackground.DefaultTintHex, reloadWallpaper: true);
            Assert.False(ShellBackground.DisplayHeld);
            Assert.True(ShellBackground.ShouldBindWallpaper);
            Assert.Equal(@"C:\pics\new.jpg", ShellBackground.WallpaperPath);
        }
        finally
        {
            ShellBackground.DisplayReleasing -= OnRelease;
            ShellBackground.Changed -= OnChanged;
            Reset();
        }
    }

    [Fact]
    public void NewWallpaperFileName_IsUniqueStoreName()
    {
        var a = ShellBackground.NewWallpaperFileName(".jpg");
        var b = ShellBackground.NewWallpaperFileName("png");
        Assert.NotEqual(a, b);
        Assert.StartsWith(ShellBackground.WallpaperFilePrefix, a);
        Assert.EndsWith(".jpg", a);
        Assert.EndsWith(".png", b);
        Assert.True(ShellBackground.IsStoredWallpaperName(a));
        Assert.True(ShellBackground.IsStoredWallpaperName(@"C:\local\shell-wallpaper.jpg"));
        Assert.False(ShellBackground.IsStoredWallpaperName(@"C:\pics\photo.jpg"));
    }

    [Fact]
    public void DecodePixelWidth_CapsCameraPhotosKeepsSmallerSources()
    {
        Assert.Equal(2560, ShellBackground.WallpaperDecodeWidth);
        Assert.Equal(1920, ShellBackground.DecodePixelWidth(1920));
        Assert.Equal(2560, ShellBackground.DecodePixelWidth(8000));
        Assert.Equal(2560, ShellBackground.DecodePixelWidth(0));
        Assert.Equal(2560, ShellBackground.DecodePixelWidth(-12));
    }

    [Fact]
    public void LightChrome_UsesSofterDimAndLightGradient()
    {
        Assert.True(ShellBackground.IsLightChrome("Light", false));
        Assert.False(ShellBackground.IsLightChrome("Dark", true));
        Assert.True(ShellBackground.IsLightChrome("System", true));
        Assert.False(ShellBackground.IsLightChrome(null, false));
        Assert.Equal(ShellBackground.LightGradientStartHex, ShellBackground.GradientStartHex(true));
        Assert.Equal(ShellBackground.DarkGradientStartHex, ShellBackground.GradientStartHex(false));
        Assert.Equal(ShellBackground.LightGlassTintHex, ShellBackground.GlassTintHex(true));
        Assert.Equal(0.40, ShellBackground.DimOpacity(false, 40), 3);
        Assert.Equal(0.40 * ShellBackground.LightDimScale, ShellBackground.DimOpacity(true, 40), 3);
        Assert.True(ShellBackground.DimOpacity(true, 40) < ShellBackground.DimOpacity(false, 40));
    }

    [Fact]
    public void DimOverlay_IsWhiteInLightAndBlackInDark()
    {
        Assert.Equal(ShellBackground.LightDimHex, ShellBackground.DimHex(true));
        Assert.Equal(ShellBackground.DarkDimHex, ShellBackground.DimHex(false));
        Assert.Equal((255, 255, 255), ShellBackground.DimRgb(true));
        Assert.Equal((0, 0, 0), ShellBackground.DimRgb(false));
    }

    [Fact]
    public void ResolveFromStorage_ReadsMicaCrashBlobShape()
    {
        // Shape from docs/pr-blockers/pr98-crash-settings.dat (Theme=Light, ShellPreset=Mica).
        var snap = ShellBackground.ResolveFromStorage(
            "Mica",
            "",
            null,
            preset => preset == ShellPresetKind.Mica ? 40.0 : null,
            preset => preset == ShellPresetKind.Mica ? 45.0 : null,
            40.0,
            45.0,
            false,
            ShellBackground.DefaultTintHex,
            micaSupported: true);
        Assert.False(snap.FellBackToDefault);
        Assert.Equal(ShellPresetKind.Mica, snap.Preset);
        Assert.Null(snap.WallpaperPath);
        Assert.Equal(40, snap.Darkness, 2);
        Assert.Equal(45, snap.Blur, 2);
    }

    [Fact]
    public void ResolveFromStorage_FallsBackToDefaultOnFailure()
    {
        var snap = ShellBackground.ResolveFromStorage(
            "Mica",
            null,
            null,
            _ => null,
            _ => null,
            null,
            null,
            null,
            null,
            micaSupported: true,
            forceFailure: true);
        Assert.True(snap.FellBackToDefault);
        Assert.Equal(ShellPresetKind.Default, snap.Preset);
        Assert.Equal(ShellBackground.DefaultDarkness, snap.Darkness, 2);
        Assert.Equal(ShellBackground.DefaultBlur, snap.Blur, 2);
        Assert.False(snap.TintEnabled);
        Assert.Equal(ShellBackground.DefaultTintHex, snap.TintHex);
    }

    [Fact]
    public void ResolveFromStorage_MicaWithoutSupport_BecomesNeutral()
    {
        var snap = ShellBackground.ResolveFromStorage(
            "MicaAlt",
            null,
            null,
            _ => null,
            _ => null,
            null,
            null,
            null,
            null,
            micaSupported: false);
        Assert.Equal(ShellPresetKind.Neutral, snap.Preset);
        Assert.False(snap.FellBackToDefault);
    }

    [Fact]
    public void SystemBackdrop_ShouldNotRecreateWhenStayingOnMica()
    {
        // Theme Dark↔Light with Mica active must keep the same SystemBackdrop instance.
        Assert.False(ShellBackground.ShouldRecreateSystemBackdrop(wantMica: true, currentlyHasMica: true));
        Assert.True(ShellBackground.ShouldRecreateSystemBackdrop(wantMica: true, currentlyHasMica: false));
        Assert.True(ShellBackground.ShouldRecreateSystemBackdrop(wantMica: false, currentlyHasMica: true));
        Assert.False(ShellBackground.ShouldRecreateSystemBackdrop(wantMica: false, currentlyHasMica: false));
        Assert.True(ShellBackground.WantsSystemBackdrop(ShellPresetKind.Mica, micaSupported: true));
        Assert.False(ShellBackground.WantsSystemBackdrop(ShellPresetKind.Mica, micaSupported: false));
        Assert.False(ShellBackground.WantsSystemBackdrop(ShellPresetKind.Ribbons, micaSupported: true));
    }

    [Fact]
    public void AmountLabel_IsPercent()
    {
        Assert.Equal("0%", ShellBackground.AmountLabel(-3));
        Assert.Equal("40%", ShellBackground.AmountLabel(40));
        Assert.Equal("100%", ShellBackground.AmountLabel(140));
    }

    private static void Reset()
    {
        ShellBackground.SetWallpaperName(null);
        ShellBackground.Apply(
            ShellPresetKind.Default,
            null,
            ShellBackground.DefaultDarkness,
            ShellBackground.DefaultBlur,
            false,
            ShellBackground.DefaultTintHex,
            reloadWallpaper: true);
    }
}
