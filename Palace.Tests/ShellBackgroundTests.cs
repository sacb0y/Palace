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
    public void AmountLabel_IsPercent()
    {
        Assert.Equal("0%", ShellBackground.AmountLabel(-3));
        Assert.Equal("40%", ShellBackground.AmountLabel(40));
        Assert.Equal("100%", ShellBackground.AmountLabel(140));
    }

    private static void Reset() =>
        ShellBackground.Apply(null, ShellBackground.DefaultDarkness, ShellBackground.DefaultBlur, false, ShellBackground.DefaultTintHex);
}
