using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class BrandAccentTests
{
    [Fact]
    public void PlaceholderHex_IsSingleSwapConstant()
    {
        Assert.Equal("#E05A8C", BrandAccent.PlaceholderHex);
        Assert.NotNull(BrandAccent.ParseHex(BrandAccent.PlaceholderHex));
    }

    [Fact]
    public void ParseMode_DefaultsToFollowWindows()
    {
        Assert.Equal(BrandAccent.ModeFollowWindows, BrandAccent.ParseMode(null));
        Assert.Equal(BrandAccent.ModeFollowWindows, BrandAccent.ParseMode("nope"));
        Assert.Equal(BrandAccent.ModePalacePink, BrandAccent.ParseMode("PalacePink"));
        Assert.True(BrandAccent.IsPalacePink("palacepink"));
        Assert.False(BrandAccent.IsPalacePink(BrandAccent.ModeFollowWindows));
    }

    [Fact]
    public void BuildRamp_MeetsAaGuardrails()
    {
        var ramp = BrandAccent.BuildRamp();
        Assert.True(BrandAccent.MeetsAaGuardrails(ramp), "Palace pink ramp must meet AA on pane fills");
        Assert.True(BrandAccent.ContrastRatio(ramp.Light2, new BrandAccent.Rgb(0x2C, 0x2C, 0x2C)) >= 4.5);
        Assert.True(BrandAccent.ContrastRatio(ramp.Dark2, new BrandAccent.Rgb(0xF3, 0xF3, 0xF3)) >= 4.5);
    }

    [Fact]
    public void ThemeAccentColors_TextOnAccentBeatsAccentTextOnFill_InLight()
    {
        var ramp = BrandAccent.BuildRamp();
        var light = BrandAccent.ForLightTheme(ramp);
        // Light tree bug: AccentText (Dark2) on Fill (Dark1) is dark-on-mid.
        Assert.True(
            BrandAccent.ContrastRatio(light.AccentText, light.Fill) < 4.5,
            "AccentText on Fill must stay the failing Light combo we avoid in UI");
        Assert.True(
            BrandAccent.MeetsAaText(light.TextOnAccent, light.Fill),
            "TextOnAccent on Default fill must pass AA for Light selected rows");

        var dark = BrandAccent.ForDarkTheme(ramp);
        Assert.True(BrandAccent.MeetsAaText(dark.TextOnAccent, dark.Fill));
        Assert.True(BrandAccent.MeetsAaText(dark.AccentText, new BrandAccent.Rgb(0x2C, 0x2C, 0x2C)));
    }

    [Fact]
    public void AccentBrushKeys_CoverFillTextOnAccentAndNavIndicator()
    {
        Assert.Contains("AccentFillColorDefaultBrush", BrandAccent.AccentBrushKeys);
        Assert.Contains("AccentTextFillColorPrimaryBrush", BrandAccent.AccentBrushKeys);
        Assert.Contains("TextOnAccentFillColorPrimaryBrush", BrandAccent.AccentBrushKeys);
        Assert.Contains("NavigationViewSelectionIndicatorForeground", BrandAccent.AccentBrushKeys);
        Assert.Equal(10, BrandAccent.AccentBrushKeys.Length);
    }

    [Fact]
    public void SystemAccentColorKeys_CoverFullRamp()
    {
        Assert.Equal(7, BrandAccent.SystemAccentColorKeys.Length);
        Assert.Contains("SystemAccentColorLight2", BrandAccent.SystemAccentColorKeys);
        Assert.Contains("SystemAccentColorDark1", BrandAccent.SystemAccentColorKeys);
    }

    [Fact]
    public void ThemeDictionaryKeys_IncludeDefaultDarkAndLight()
    {
        Assert.Contains("Default", BrandAccent.ThemeDictionaryKeys);
        Assert.Contains("Dark", BrandAccent.ThemeDictionaryKeys);
        Assert.Contains("Light", BrandAccent.ThemeDictionaryKeys);
    }

    [Fact]
    public void BrushPaint_UsesFillForNavIndicatorAndTextOnAccentForInk()
    {
        var colors = BrandAccent.ForDarkTheme(BrandAccent.BuildRamp());
        Assert.True(BrandAccent.TryBrushPaint("AccentFillColorDefaultBrush", colors, out var fill, out var fillOpacity));
        Assert.Equal(colors.Fill, fill);
        Assert.Equal(1.0, fillOpacity);
        Assert.True(BrandAccent.TryBrushPaint("AccentFillColorTertiaryBrush", colors, out _, out var tertiaryOpacity));
        Assert.Equal(0.8, tertiaryOpacity);
        Assert.True(BrandAccent.TryBrushPaint(
            "NavigationViewSelectionIndicatorForeground", colors, out var indicator, out _));
        Assert.Equal(colors.Fill, indicator);
        Assert.True(BrandAccent.TryBrushPaint(
            "TextOnAccentFillColorPrimaryBrush", colors, out var ink, out var inkOpacity));
        Assert.Equal(colors.TextOnAccent, ink);
        Assert.Equal(1.0, inkOpacity);
        Assert.False(BrandAccent.TryBrushPaint("NotAnAccentBrush", colors, out _, out _));
    }

    [Fact]
    public void MergedDictionary_DoesNotOverrideOwnerThemeBrush()
    {
        // Mirrors packaged WinUI: AccentFillColorDefaultBrush already lives on the
        // XamlControlsResources theme dictionary. A dictionary appended to
        // MergedDictionaries is not the one controls resolve.
        var xcr = new AccentResourceScope.Node();
        var brushes = BrandAccent.AccentBrushKeys.ToHashSet(StringComparer.Ordinal);
        xcr.ThemeKeys["Default"] = brushes;
        xcr.ThemeKeys["Light"] = brushes;

        var injected = new AccentResourceScope.Node();
        injected.ThemeKeys["Default"] = BrandAccent.AccentBrushKeys.ToHashSet(StringComparer.Ordinal);
        injected.ThemeKeys["Dark"] = BrandAccent.AccentBrushKeys.ToHashSet(StringComparer.Ordinal);
        injected.ThemeKeys["Light"] = BrandAccent.AccentBrushKeys.ToHashSet(StringComparer.Ordinal);
        xcr.Merged.Add(injected);

        foreach (var theme in new[] { "Default", "Light" })
        {
            var hit = AccentResourceScope.Find(xcr, theme, "AccentFillColorDefaultBrush");
            Assert.Same(xcr, hit);
        }

        var darkOnly = AccentResourceScope.Find(xcr, "Dark", "NavigationViewSelectionIndicatorForeground");
        Assert.Same(injected, darkOnly);
    }
}
