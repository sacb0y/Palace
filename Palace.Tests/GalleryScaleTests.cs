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
        Assert.Equal(68, GalleryScale.KeyLetterD);
        Assert.Equal(73, GalleryScale.KeyLetterI);
        Assert.True(GalleryScale.TogglesImageInfo(true, GalleryScale.KeyLetterD));
        Assert.False(GalleryScale.TogglesImageInfo(false, GalleryScale.KeyLetterD));
        Assert.True(GalleryScale.TogglesDetails(true, GalleryScale.KeyLetterI));
        Assert.False(GalleryScale.TogglesDetails(true, GalleryScale.KeyLetterD));
        Assert.False(GalleryScale.TogglesDetails(false, GalleryScale.KeyLetterI));
        Assert.Equal(32, GalleryScale.KeySpace);
        Assert.Equal(27, GalleryScale.KeyEscape);
        Assert.True(GalleryScale.OpensOverlay(false, false, GalleryScale.KeySpace));
        Assert.False(GalleryScale.OpensOverlay(true, false, GalleryScale.KeySpace));
        Assert.False(GalleryScale.OpensOverlay(false, true, GalleryScale.KeySpace));
        Assert.False(GalleryScale.OpensOverlay(false, false, GalleryScale.KeyEscape));
        Assert.True(GalleryScale.ClosesOverlay(true, GalleryScale.KeyEscape));
        Assert.False(GalleryScale.ClosesOverlay(false, GalleryScale.KeyEscape));
        Assert.False(GalleryScale.ClosesOverlay(true, GalleryScale.KeySpace));
        Assert.True(GalleryScale.PassesViewerSpace(true, GalleryScale.KeySpace));
        Assert.False(GalleryScale.PassesViewerSpace(false, GalleryScale.KeySpace));
        Assert.False(GalleryScale.PassesViewerSpace(true, GalleryScale.KeyEscape));
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
        Assert.Equal("Uniform", GalleryScale.StretchName(ImageScaling.Actual));
        Assert.Equal("UniformToFill", GalleryScale.StretchName(ImageScaling.Fill));
        Assert.True(GalleryScale.Scrolls(ImageScaling.Actual));
        Assert.True(GalleryScale.Scrolls(ImageScaling.Fill));
        Assert.False(GalleryScale.Scrolls(ImageScaling.Fit));
        Assert.True(GalleryScale.Scrolls(ImageScaling.Fit, 1.5));
        Assert.False(GalleryScale.Scrolls(ImageScaling.Fit, 1.0));
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
        Assert.True(GalleryScale.UsesDragPan(true, isMouse: true, leftButton: true, isTouch: false, isPen: false));
        Assert.False(GalleryScale.UsesDragPan(true, isMouse: true, leftButton: false, isTouch: false, isPen: false));
        Assert.True(GalleryScale.UsesDragPan(true, isMouse: false, leftButton: false, isTouch: true, isPen: false));
        Assert.True(GalleryScale.UsesDragPan(true, isMouse: false, leftButton: false, isTouch: false, isPen: true));
        Assert.False(GalleryScale.UsesDragPan(false, isMouse: false, leftButton: false, isTouch: true, isPen: false));
        Assert.True(GalleryScale.UsesPointerCapturePan(true, isMouse: true, leftButton: true));
        Assert.False(GalleryScale.UsesPointerCapturePan(true, isMouse: true, leftButton: false));
        Assert.False(GalleryScale.UsesPointerCapturePan(true, isMouse: false, leftButton: true));
        Assert.False(GalleryScale.UsesPointerCapturePan(true, isMouse: false, leftButton: false));
        Assert.Equal(2.0, GalleryScale.PinchZoom(1, 2), 3);
        Assert.Equal(1.0, GalleryScale.PinchZoom(1, 0.25), 3);
        Assert.Equal(8.0, GalleryScale.PinchZoom(4, 4), 3);
        Assert.Equal(1.0, GalleryScale.PinchZoom(1, 0), 3);
        var zoomed = GalleryScale.ApplyPinchZoom(100, 50, 2);
        Assert.Equal(200, zoomed.Width, 3);
        Assert.Equal(100, zoomed.Height, 3);
        var pinchPan = GalleryScale.PinchPan(10, 0, 40, 20, 1, 2, 200, 100);
        Assert.Equal(60, pinchPan.Horizontal, 3);
        Assert.Equal(20, pinchPan.Vertical, 3);
        var composed = GalleryScale.PinchThenDrag(10, 0, 40, 20, 1, 2, 4, -3, 200, 100);
        Assert.Equal(56, composed.Horizontal, 3);
        Assert.Equal(23, composed.Vertical, 3);
        var staleDrag = GalleryScale.DragPan(10, 0, 4, -3, 200, 100);
        Assert.NotEqual(composed.Horizontal, staleDrag.Horizontal);
        Assert.True(GalleryScale.UsesManipulationGesture(isTouch: true, isPen: false, isMouse: false, leftButton: false));
        Assert.True(GalleryScale.UsesManipulationGesture(isTouch: false, isPen: true, isMouse: false, leftButton: false));
        Assert.True(GalleryScale.UsesManipulationGesture(isTouch: false, isPen: false, isMouse: true, leftButton: false));
        Assert.False(GalleryScale.UsesManipulationGesture(isTouch: false, isPen: false, isMouse: true, leftButton: true));
        Assert.False(GalleryScale.HandlesManipulationDelta(
            isInertial: true, isTouch: true, isPen: false, isMouse: false, leftButton: false));
        Assert.True(GalleryScale.HandlesManipulationDelta(
            isInertial: false, isTouch: true, isPen: false, isMouse: false, leftButton: false));
        Assert.True(GalleryScale.HandlesManipulationDelta(
            isInertial: false, isTouch: false, isPen: false, isMouse: true, leftButton: false));
        Assert.False(GalleryScale.HandlesManipulationDelta(
            isInertial: false, isTouch: false, isPen: false, isMouse: true, leftButton: true));
        Assert.False(GalleryScale.ShowsScrollBars());
        Assert.True(GalleryScale.ShouldResetView(stillChanged: true, scalingChanged: false, viewportChanged: false));
        Assert.True(GalleryScale.ShouldResetView(stillChanged: false, scalingChanged: true, viewportChanged: false));
        Assert.True(GalleryScale.ShouldResetView(stillChanged: false, scalingChanged: false, viewportChanged: true));
        Assert.False(GalleryScale.ShouldResetView(stillChanged: false, scalingChanged: false, viewportChanged: false));
        Assert.True(GalleryScale.UsesWheelPinch(controlDown: true, wheelDelta: 120));
        Assert.False(GalleryScale.UsesWheelPinch(controlDown: false, wheelDelta: 120));
        Assert.False(GalleryScale.UsesWheelPinch(controlDown: true, wheelDelta: 0));
        Assert.True(GalleryScale.UsesWheelPan(scrolls: true, controlDown: false, wheelDelta: -120));
        Assert.False(GalleryScale.UsesWheelPan(scrolls: false, controlDown: false, wheelDelta: -120));
        Assert.False(GalleryScale.UsesWheelPan(scrolls: true, controlDown: true, wheelDelta: -120));
        Assert.True(GalleryScale.NeedsHandledWheelListener(hidesScrollBars: true));
        Assert.False(GalleryScale.NeedsHandledWheelListener(hidesScrollBars: false));
        Assert.Equal(1.1, GalleryScale.WheelPinchFactor(120), 5);
        Assert.Equal(1 / 1.1, GalleryScale.WheelPinchFactor(-120), 5);
        Assert.Equal(1.0, GalleryScale.WheelPinchFactor(0), 5);
        Assert.Equal(30, GalleryScale.WheelToPanDelta(120), 3);
        var wheelPan = GalleryScale.DragPan(40, 40, 0, GalleryScale.WheelToPanDelta(120), 100, 100);
        Assert.Equal(40, wheelPan.Horizontal, 3);
        Assert.Equal(10, wheelPan.Vertical, 3);
        Assert.Equal(3.0, GalleryScale.PinchZoom(GalleryScale.PinchZoom(1, 2), 1.5), 3);
    }

    [Fact]
    public void CoverCenterOffset_CentersFillOverflow()
    {
        var wide = GalleryScale.CoverCenterOffset(200, 100, 100, 100);
        Assert.Equal(50, wide.Horizontal, 3);
        Assert.Equal(0, wide.Vertical, 3);
        var tall = GalleryScale.CoverCenterOffset(100, 200, 100, 100);
        Assert.Equal(0, tall.Horizontal, 3);
        Assert.Equal(50, tall.Vertical, 3);
        var fit = GalleryScale.CoverCenterOffset(100, 100, 100, 100);
        Assert.Equal(0, fit.Horizontal, 3);
        Assert.Equal(0, fit.Vertical, 3);
        Assert.Equal((50.0, 0.0), GalleryScale.InitialScrollOffset(ImageScaling.Fill, 200, 100, 100, 100));
        Assert.Equal((0.0, 0.0), GalleryScale.InitialScrollOffset(ImageScaling.Actual, 200, 100, 100, 100));
    }
}
