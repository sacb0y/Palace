using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class GalleryScaleTests
{
    [Fact]
    public void FromDigit_MatchesSkivShortcuts()
    {
        Assert.Equal(ImageScaling.Actual, GalleryScale.FromDigit(1));
        Assert.Equal(ImageScaling.Fit, GalleryScale.FromDigit(2));
        Assert.Equal(ImageScaling.Fit, GalleryScale.FromDigit(0));
        Assert.Equal(ImageScaling.Fill, GalleryScale.FromDigit(3));
        Assert.Null(GalleryScale.FromDigit(4));
    }

    [Fact]
    public void FromKeyCode_ReadsNumberRowAndPad()
    {
        Assert.Equal(ImageScaling.Actual, GalleryScale.FromKeyCode(GalleryScale.KeyNumber1));
        Assert.Equal(ImageScaling.Fit, GalleryScale.FromKeyCode(GalleryScale.KeyNumber2));
        Assert.Equal(ImageScaling.Fit, GalleryScale.FromKeyCode(GalleryScale.KeyNumber0));
        Assert.Equal(ImageScaling.Fill, GalleryScale.FromKeyCode(GalleryScale.KeyNumberPad3));
        Assert.Null(GalleryScale.FromKeyCode(65));
    }

    [Fact]
    public void Cycle_WalksFitFillActual()
    {
        Assert.Equal(ImageScaling.Fill, GalleryScale.Cycle(ImageScaling.Fit));
        Assert.Equal(ImageScaling.Actual, GalleryScale.Cycle(ImageScaling.Fill));
        Assert.Equal(ImageScaling.Fit, GalleryScale.Cycle(ImageScaling.Actual));
    }

    [Fact]
    public void StretchName_AndScroll_MatchModes()
    {
        Assert.Equal("Uniform", GalleryScale.StretchName(ImageScaling.Fit));
        Assert.Equal("None", GalleryScale.StretchName(ImageScaling.Actual));
        Assert.Equal("UniformToFill", GalleryScale.StretchName(ImageScaling.Fill));
        Assert.True(GalleryScale.Scrolls(ImageScaling.Actual));
        Assert.False(GalleryScale.Scrolls(ImageScaling.Fit));
        Assert.Equal("1:1", GalleryScale.Label(ImageScaling.Actual));
    }
}
