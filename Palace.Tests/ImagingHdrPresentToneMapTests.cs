using Palace.Helpers;
using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

public class ImagingHdrPresentToneMapTests
{
    [Fact]
    public void DisplayForPresent_UnknownPeak_Uses10000()
    {
        var d = ImagingHdrPresentToneMap.DisplayForPresent(0f);
        Assert.True(d.IsHdrOutput);
        Assert.Equal(10_000f, d.PeakNits);
        Assert.Equal(10_000f / ImagingConstants.RefWhiteNits, d.Ld, 4);
    }

    [Fact]
    public void DisplayForPresent_UsesProbedPeak()
    {
        var d = ImagingHdrPresentToneMap.DisplayForPresent(1000f);
        Assert.Equal(1000f, d.PeakNits);
        Assert.Equal(1000f / ImagingConstants.RefWhiteNits, d.Ld, 4);
    }

    [Fact]
    public void PresentMapImagingHdr_LiftsPaperAndClipsAtPeak()
    {
        var map = GalleryPresent.PresentMapImagingHdr(1000f);
        Assert.Equal(0f, map.ReinhardWhite);
        Assert.True(map.SoftNegatives);
        Assert.Equal(GalleryPresent.SdrReferenceNits / GalleryPresent.ScrgbNits, map.OutputScale, 4);
        Assert.Equal(GalleryPresent.RasterizeClipScrgb(1000f), map.ClipScrgb, 4);
        Assert.Equal(1f, map.Scale);
    }

    [Fact]
    public void TryMapToPresentScrgb_RejectsSdrDisplay()
    {
        var working = FlatWorking(0.5f);
        var mapped = ImagingHdrPresentToneMap.TryMapToPresentScrgb(
            working, DisplayTarget.Sdr203);
        Assert.Null(mapped);
    }

    [Fact]
    public void TryMapToPresentScrgb_FitsDisplay_NearIdentityAfterLift()
    {
        // Working paper white on a 1000-nit panel (Lc ≪ Ld) stays near 1.0;
        // PresentMapImagingHdr then lifts ×203/80 into absolute scRGB.
        var working = FlatWorking(1f);
        var display = DisplayTarget.Hdr(1000f);
        var mapped = ImagingHdrPresentToneMap.TryMapToPresentScrgb(working, display);
        Assert.NotNull(mapped);
        Assert.InRange(mapped![0], 0.90f, 1.05f);
        Assert.InRange(mapped[1], 0.90f, 1.05f);
        Assert.InRange(mapped[2], 0.90f, 1.05f);

        var map = GalleryPresent.PresentMapImagingHdr(1000f);
        var absolute = mapped[0] * map.OutputScale;
        Assert.InRange(absolute, 2.2f, 2.7f); // ~203/80
        Assert.True(absolute <= map.ClipScrgb + 1e-3f);
    }

    [Fact]
    public void TryMapToPresentScrgb_HotContent_ClampsAtLdInWorking()
    {
        var working = FlatWorking(20f); // ≫ Ld for 400-nit panel
        var display = DisplayTarget.Hdr(400f);
        var mapped = ImagingHdrPresentToneMap.TryMapToPresentScrgb(working, display);
        Assert.NotNull(mapped);
        Assert.True(mapped![0] <= display.Ld + 1e-3f, $"R={mapped[0]} Ld={display.Ld}");
        Assert.True(mapped[0] > 1f, "hot HDR should stay above paper");
    }

    [Fact]
    public void TryMapFrameToPresentScrgb_FromAbsoluteScrgb_Succeeds()
    {
        float[] scrgb =
        [
            8f, 8f, 8f, 1f,
            0.5f, 0.5f, 0.5f, 1f,
            0.5f, 0.5f, 0.5f, 1f,
            0.5f, 0.5f, 0.5f, 1f,
        ];
        var mapped = ImagingHdrPresentToneMap.TryMapFrameToPresentScrgb(
            working: null, scrgb, 2, 2, displayPeakNits: 1000f);
        Assert.NotNull(mapped);
        Assert.Equal(16, mapped!.Length);
        Assert.True(mapped[0] < 8f, "hot highlight should compress");
    }

    [Fact]
    public void PresentMatchesThumbToneMap_HdrPq_False()
    {
        Assert.False(GalleryPresent.PresentMatchesThumbToneMap(true, JxlBufferTransfer.Pq));
        Assert.False(GalleryPresent.PresentMatchesThumbToneMap(true, JxlBufferTransfer.Hlg));
        Assert.True(GalleryPresent.PresentMatchesThumbToneMap(true, JxlBufferTransfer.Linear));
    }

    private static NormalizedFrame FlatWorking(float v) =>
        new()
        {
            Rgba = [v, v, v, 1f],
            Width = 1,
            Height = 1,
            SourceClipped = false,
            SourceClipRatio = 0f,
            MetadataPeakNits = null,
        };
}
