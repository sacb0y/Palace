using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

[Collection("ViewerSettingsStatic")]
public sealed class GalleryScaleTests
{
    public GalleryScaleTests()
    {
        ViewerSettings.Apply(
            ViewerSettings.DefaultZoom,
            ViewerSettings.DefaultTouchGestures,
            ViewerSettings.DefaultKeepZoomBetweenImages);
    }

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
        Assert.True(GalleryScale.TogglesDetails(false, GalleryScale.KeyLetterI));
        Assert.False(GalleryScale.TogglesDetails(true, GalleryScale.KeyLetterI));
        Assert.False(GalleryScale.TogglesDetails(true, GalleryScale.KeyLetterD));
        Assert.True(GalleryScale.TogglesDetails(false, altDown: true, GalleryScale.KeyEnter));
        Assert.False(GalleryScale.TogglesDetails(false, altDown: true, GalleryScale.KeyLetterI));
        Assert.Equal(72, GalleryScale.KeyLetterH);
        Assert.True(GalleryScale.TogglesHeatmap(true, GalleryScale.KeyLetterH));
        Assert.False(GalleryScale.TogglesHeatmap(false, GalleryScale.KeyLetterH));
        Assert.False(GalleryScale.TogglesHeatmap(true, GalleryScale.KeyLetterI));
        Assert.Equal(32, GalleryScale.KeySpace);
        Assert.Equal(27, GalleryScale.KeyEscape);
        Assert.Equal(117, GalleryScale.KeyF6);
        Assert.True(GalleryScale.CyclesViewerFocus(GalleryScale.KeyF6));
        Assert.True(GalleryScale.ClosesDetailsFirst(true, GalleryScale.KeyEscape));
        Assert.False(GalleryScale.ClosesDetailsFirst(false, GalleryScale.KeyEscape));
        Assert.True(GalleryScale.OpensOverlay(false, false, GalleryScale.KeySpace));
        Assert.False(GalleryScale.OpensOverlay(true, false, GalleryScale.KeySpace));
        Assert.False(GalleryScale.OpensOverlay(false, true, GalleryScale.KeySpace));
        Assert.False(GalleryScale.OpensOverlay(false, false, GalleryScale.KeyEscape));
        Assert.True(GalleryScale.OpensOverlayFromEnter(false, false, GalleryScale.KeyEnter));
        Assert.False(GalleryScale.OpensOverlayFromEnter(true, false, GalleryScale.KeyEnter));
        Assert.False(GalleryScale.OpensOverlayFromEnter(false, true, GalleryScale.KeyEnter));
        Assert.False(GalleryScale.OpensOverlayFromEnter(false, false, GalleryScale.KeySpace));
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
    public void DragPanContent_AndStickyDisplayScaling()
    {
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
        Assert.True(GalleryScale.UsesManipulationGesture(isTouch: true, isPen: false, isMouse: false, leftButton: false));
        Assert.True(GalleryScale.UsesManipulationGesture(isTouch: false, isPen: true, isMouse: false, leftButton: false));
        Assert.True(GalleryScale.UsesManipulationGesture(isTouch: false, isPen: false, isMouse: true, leftButton: false));
        Assert.False(GalleryScale.UsesManipulationGesture(isTouch: false, isPen: false, isMouse: true, leftButton: true));
        Assert.False(GalleryScale.UsesManipulationGesture(
            isTouch: true, isPen: false, isMouse: false, leftButton: false, touchGesturesEnabled: false));
        Assert.True(GalleryScale.UsesPointerCapturePan(true, isMouse: true, leftButton: true));
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

        // Keep-zoom sticky must survive HDR; DisplayScaling is Fit only on screen.
        var sticky = ImageScaling.Actual;
        sticky = GalleryScale.StickyScalingAfterStillOpen(
            sticky, keepZoomBetweenImages: true, ImageScaling.Fit);
        Assert.Equal(ImageScaling.Actual, sticky);
        Assert.Equal(ImageScaling.Fit, GalleryScale.DisplayScaling(sticky, hdrPresentable: true));
        // 1:1 → HDR → normal stays 1:1
        sticky = GalleryScale.StickyScalingAfterStillOpen(
            sticky, keepZoomBetweenImages: true, ImageScaling.Fit);
        Assert.Equal(ImageScaling.Actual, sticky);
        Assert.Equal(ImageScaling.Actual, GalleryScale.DisplayScaling(sticky, hdrPresentable: false));
        // Keep-zoom off + Fill default: sticky becomes Fill; HDR still displays Fit.
        sticky = GalleryScale.StickyScalingAfterStillOpen(
            ImageScaling.Actual, keepZoomBetweenImages: false, ImageScaling.Fill);
        Assert.Equal(ImageScaling.Fill, sticky);
        Assert.Equal(ImageScaling.Fit, GalleryScale.DisplayScaling(sticky, hdrPresentable: true));
        Assert.Equal(ImageScaling.Fill, GalleryScale.DisplayScaling(sticky, hdrPresentable: false));

        // Fit pinch-1 clears scroll; Actual does not (no ChangeView 0,0 snap).
        Assert.True(GalleryScale.ClearsScrollOnPinchRelease(ImageScaling.Fit, 1.0));
        Assert.False(GalleryScale.ClearsScrollOnPinchRelease(ImageScaling.Actual, 1.0));
        Assert.False(GalleryScale.ClearsScrollOnPinchRelease(ImageScaling.Fill, 1.0));
        Assert.True(GalleryScale.IsOpenAtFit(ImageScaling.Fit, 1.0, 0, 0));
        Assert.False(GalleryScale.IsOpenAtFit(ImageScaling.Actual, 1.0, 0, 0));
        Assert.False(GalleryScale.IsOpenAtFit(ImageScaling.Fit, 2.0, 0, 0));
        Assert.False(GalleryScale.IsOpenAtFit(ImageScaling.Fit, 1.0, 40, 0));
        Assert.False(GalleryScale.IsOpenAtFit(ImageScaling.Fit, 1.0, 0, 40));
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
        var wheelPan = GalleryScale.DragPanContent(
            40, 40, 0, GalleryScale.WheelToPanDelta(120), 200, 200, 100, 100);
        Assert.Equal(40, wheelPan.Horizontal, 3);
        Assert.Equal(10, wheelPan.Vertical, 3);
        Assert.Equal(3.0, GalleryScale.PinchZoom(GalleryScale.PinchZoom(1, 2), 1.5), 3);
        // Narrow content (80 < 200) centers on X (offset 0); Y scrolls.
        var contentPan = GalleryScale.DragPanContent(10, -5, 0, -20, 80, 400, 200, 100);
        Assert.Equal(0, contentPan.Horizontal, 3);
        Assert.Equal(15, contentPan.Vertical, 3); // -5 - (-20) = 15, inside 0…300
        var contentLow = GalleryScale.DragPanContent(0, 10, 0, 40, 80, 400, 200, 100);
        Assert.Equal(0, contentLow.Horizontal, 3);
        Assert.Equal(0, contentLow.Vertical, 3); // 10 - 40 = -30 → clamp 0
        var contentMid = GalleryScale.DragPanContent(0, 100, 0, -50, 80, 400, 200, 100);
        Assert.Equal(0, contentMid.Horizontal, 3);
        Assert.Equal(150, contentMid.Vertical, 3);
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

    [Fact]
    public void FitContainDipSize_LetterboxesImageNotViewport()
    {
        // Tall image in wide viewport: height matches, width narrower (not full view).
        var tall = GalleryScale.FitContainDipSize(100, 200, 400, 200);
        Assert.Equal(100, tall.Width, 3);
        Assert.Equal(200, tall.Height, 3);
        // Wide image in tall viewport: width matches, height shorter.
        var wide = GalleryScale.FitContainDipSize(200, 100, 200, 400);
        Assert.Equal(200, wide.Width, 3);
        Assert.Equal(100, wide.Height, 3);
        // Same aspect → fills viewport.
        var match = GalleryScale.FitContainDipSize(1920, 1080, 1920, 1080);
        Assert.Equal(1920, match.Width, 3);
        Assert.Equal(1080, match.Height, 3);
        // Opposite of Fill cover on a mismatched aspect.
        var fill = GalleryScale.FillCoverDipSize(100, 200, 400, 200);
        Assert.True(fill.Width > tall.Width);
    }

    [Theory]
    // Pregmata_SteamAVIF.exr / HDR AVIF / HDR JXL — same SDR scRGB Fit path.
    [InlineData("exr", 3840, 2160)]
    [InlineData("avif", 3840, 2160)]
    [InlineData("jxl", 4096, 2304)]
    public void Fit_SdrHdrPresent_ShowsFullFrame_PerFormat(string format, int nativeW, int nativeH)
    {
        _ = format;
        // Laptop viewports + CompositionScale 1× / 1.25× / 1.5× (DIP vs physical).
        foreach (var scale in new[] { 1.0, 1.25, 1.5 })
        {
            Assert.True(
                GalleryPresent.FitShowsFullFrame(nativeW, nativeH, 1280, 720, scale, scale),
                $"Fit must show full {nativeW}×{nativeH} at compScale={scale}");
            Assert.True(
                GalleryPresent.FitShowsFullFrame(nativeW, nativeH, 1920, 1080, scale, scale));
        }

        // Physical buffer = DIP × CompositionScale (not DIP alone).
        var (dipW, dipH) = GalleryScale.FitContainDipSize(nativeW, nativeH, 1280, 720);
        var (pxW, pxH) = GalleryPresent.SwapchainPhysicalSize(dipW, dipH, 1.5, 1.5);
        Assert.Equal((int)Math.Round(dipW * 1.5), pxW);
        Assert.Equal((int)Math.Round(dipH * 1.5), pxH);
    }
}
