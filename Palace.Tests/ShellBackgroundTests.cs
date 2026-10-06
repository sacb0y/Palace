using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class ShellBackgroundTests
{
    public ShellBackgroundTests() => Reset();

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
    public void AmountLabel_IsPercent()
    {
        Assert.Equal("0%", ShellBackground.AmountLabel(-3));
        Assert.Equal("40%", ShellBackground.AmountLabel(40));
        Assert.Equal("100%", ShellBackground.AmountLabel(140));
    }

    private static void Reset()
    {
        ShellBackground.SetWallpaperName(null);
        ShellBackground.Apply(null, ShellBackground.DefaultDarkness, ShellBackground.DefaultBlur, false, ShellBackground.DefaultTintHex, reloadWallpaper: true);
    }
}
