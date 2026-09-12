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
        Assert.True(GalleryScale.Scrolls(ImageScaling.Fill));
        Assert.False(GalleryScale.Scrolls(ImageScaling.Fit));
        Assert.Equal("1:1", GalleryScale.Label(ImageScaling.Actual));
    }

    [Fact]
    public void ActualDipSize_IsOneDevicePixelPerImagePixel()
    {
        var oneX = GalleryScale.ActualDipSize(200, 100, 1);
        Assert.Equal(200, oneX.Width, 3);
        Assert.Equal(100, oneX.Height, 3);
        var oneHalf = GalleryScale.ActualDipSize(200, 100, 1.5);
        Assert.Equal(200 / 1.5, oneHalf.Width, 3);
        Assert.Equal(100 / 1.5, oneHalf.Height, 3);
        var missing = GalleryScale.ActualDipSize(0, 100, 1.5);
        Assert.True(double.IsNaN(missing.Width));
        Assert.True(double.IsNaN(missing.Height));
    }

    [Fact]
    public void FillCoverDipSize_CoversViewportSoOverflowCanPan()
    {
        var wide = GalleryScale.FillCoverDipSize(200, 100, 100, 100);
        Assert.Equal(200, wide.Width, 3);
        Assert.Equal(100, wide.Height, 3);
        var tall = GalleryScale.FillCoverDipSize(100, 200, 100, 100);
        Assert.Equal(100, tall.Width, 3);
        Assert.Equal(200, tall.Height, 3);
        var same = GalleryScale.FillCoverDipSize(100, 100, 100, 100);
        Assert.Equal(100, same.Width, 3);
        Assert.Equal(100, same.Height, 3);
        var unknown = GalleryScale.FillCoverDipSize(0, 0, 80, 60);
        Assert.Equal(80, unknown.Width, 3);
        Assert.Equal(60, unknown.Height, 3);
    }

    [Fact]
    public void DragPan_SubtractsDeltaAndClamps()
    {
        var moved = GalleryScale.DragPan(10, 20, 4, -3, 100, 80);
        Assert.Equal(6, moved.Horizontal, 3);
        Assert.Equal(23, moved.Vertical, 3);
        var low = GalleryScale.DragPan(2, 5, 10, 20, 50, 40);
        Assert.Equal(0, low.Horizontal, 3);
        Assert.Equal(0, low.Vertical, 3);
        var high = GalleryScale.DragPan(48, 38, -10, -10, 50, 40);
        Assert.Equal(50, high.Horizontal, 3);
        Assert.Equal(40, high.Vertical, 3);
        var none = GalleryScale.DragPan(4, 4, 1, 1, 0, 0);
        Assert.Equal(0, none.Horizontal, 3);
        Assert.Equal(0, none.Vertical, 3);
    }
}
