using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public class HdrJxlPresentTests
{
    [Fact]
    public void ShouldPresentViaLibjxl_OnlyPresentableHdrJxl()
    {
        Assert.True(HdrJxlPresent.ShouldPresentViaLibjxl(new HdrProbe(HdrKind.HdrJxl, 9, 16, null)));
        Assert.True(HdrJxlPresent.ShouldPresentViaLibjxl(new HdrProbe(HdrKind.HdrJxl, 9, 18, null)));
        // Gain-map / wide-gamut / SDR base: libjxl still decodes the base, but
        // present stays BitmapImage — never the HDR scRGB swapchain path.
        Assert.False(HdrJxlPresent.ShouldPresentViaLibjxl(new HdrProbe(HdrKind.WideGamutJxl, 11, 13, null)));
        Assert.False(HdrJxlPresent.ShouldPresentViaLibjxl(new HdrProbe(HdrKind.HdrAvif, 9, 16, 1000)));
        Assert.False(HdrJxlPresent.ShouldPresentViaLibjxl(HdrProbe.None));
    }

    [Fact]
    public void ScrgbScaleFromIntensityTarget_RoutesOnPqHlgTransfer()
    {
        // PQ intensity target 10 000 nits → scRGB 1.0 = 80 nits.
        Assert.Equal(
            10_000f / GalleryPresent.ScrgbNits,
            HdrJxlPresent.ScrgbScaleFromIntensityTarget(10_000f, HdrTransfer.Pq),
            3);
        Assert.Equal(
            1600f / GalleryPresent.ScrgbNits,
            HdrJxlPresent.ScrgbScaleFromIntensityTarget(1600f, HdrTransfer.Hlg),
            3);
        // Missing IntensityTarget: PQ → 10 000, HLG → 1000 (not PQ's 10k).
        Assert.Equal(10_000f / GalleryPresent.ScrgbNits, HdrJxlPresent.ScrgbScaleFromIntensityTarget(0f, HdrTransfer.Pq), 3);
        Assert.Equal(1000f / GalleryPresent.ScrgbNits, HdrJxlPresent.ScrgbScaleFromIntensityTarget(0f, HdrTransfer.Hlg), 3);
        // SDR / sRGB transfer stays relative 0..1 — even if IntensityTarget looks high.
        Assert.Equal(1f, HdrJxlPresent.ScrgbScaleFromIntensityTarget(10_000f, HdrTransfer.Srgb));
        Assert.Equal(1f, HdrJxlPresent.ScrgbScaleFromIntensityTarget(255f, HdrTransfer.Srgb));
        Assert.Equal(1f, HdrJxlPresent.ScrgbScaleFromIntensityTarget(100f, HdrTransfer.Linear));
        // Never use BT.2408 paper white (203) as the scRGB divisor.
        Assert.NotEqual(10_000f / 203f, HdrJxlPresent.ScrgbScaleFromIntensityTarget(10_000f, HdrTransfer.Pq), 3);
    }

    [Fact]
    public void ApplyIntensityScale_Stride4_MultipliesRgbLeavesAlpha()
    {
        Span<float> rgba = stackalloc float[] { 0.5f, 1f, 2f, 0.25f, 0.1f, 0.2f, 0.3f, 1f };
        HdrJxlPresent.ApplyIntensityScale(rgba, 2f);
        Assert.Equal(1f, rgba[0], 4);
        Assert.Equal(2f, rgba[1], 4);
        Assert.Equal(4f, rgba[2], 4);
        Assert.Equal(0.25f, rgba[3], 4);
        Assert.Equal(0.2f, rgba[4], 4);
        Assert.Equal(0.4f, rgba[5], 4);
        Assert.Equal(0.6f, rgba[6], 4);
        Assert.Equal(1f, rgba[7], 4);

        HdrJxlPresent.ApplyIntensityScale(rgba, 1f); // no-op
        Assert.Equal(1f, rgba[0], 4);
    }

    [Fact]
    public void BuildPresentFrame_KeepsNativeStatsAndBoxScalesPixels()
    {
        // 4×4 with one highlight; PresentDecodeSize needs viewport ≥2 to Fit-downscale.
        const float highlight = 1000f / GalleryPresent.ScrgbNits;
        var rgba = new float[4 * 4 * 4];
        for (var i = 0; i < 16; i++)
        {
            rgba[i * 4] = 1f;
            rgba[i * 4 + 1] = 1f;
            rgba[i * 4 + 2] = 1f;
            rgba[i * 4 + 3] = 1f;
        }

        rgba[0] = highlight;
        rgba[1] = 0f;
        rgba[2] = 0f;

        var frame = HdrJxlPresent.BuildPresentFrame(
            rgba, 4, 4, viewportPixelWidth: 2, viewportPixelHeight: 2, ImageScaling.Fit);
        Assert.NotNull(frame);
        Assert.Equal(2, frame!.Width);
        Assert.Equal(2, frame.Height);
        Assert.Equal(16, frame.ScrgbRgba.Length);
        // Native MaxCLL / MaxNits survive the Fit box-scale (same as EXR).
        Assert.Equal(highlight, frame.MaxScrgb, 3);
        Assert.Equal('R', frame.MaxScrgbChannel);
        Assert.True(frame.MaxNits > GalleryPresent.SdrReferenceNits);
    }

    [Fact]
    public void BuildPresentFrame_RejectsBadBuffers()
    {
        Assert.Null(HdrJxlPresent.BuildPresentFrame(null!, 1, 1, 0, 0, ImageScaling.Fit));
        Assert.Null(HdrJxlPresent.BuildPresentFrame(new float[4], 0, 1, 0, 0, ImageScaling.Fit));
        Assert.Null(HdrJxlPresent.BuildPresentFrame(new float[4], 2, 2, 0, 0, ImageScaling.Fit));
    }

    [Fact]
    public void BuildPresentFrame_PqHighlight_SdrTonemapHasDataForInfo()
    {
        // Simulate post-Normalize PQ white at ~203 nits (paper white) in scRGB.
        var paper = GalleryPresent.SdrReferenceNits / GalleryPresent.ScrgbNits;
        var rgba = new float[] { paper, paper, paper, 1f };
        var frame = HdrJxlPresent.BuildPresentFrame(rgba, 1, 1, 0, 0, ImageScaling.Fit);
        Assert.NotNull(frame);
        Assert.Equal(paper, frame!.MaxScrgb, 3);
        Assert.Equal(GalleryPresent.SdrReferenceNits, frame.MaxNits, 1);
        Assert.True(frame.AvgNits > 0);
        // SDR PresentMap needs a positive content MaxCLL — heatmap / Info use this.
        var map = GalleryPresent.PresentMap(false, frame.MaxNits, 0);
        Assert.True(map.Scale > 0);
        Assert.True(map.Scale <= 1f);
    }

    [Fact]
    public void BuildPresentFrame_PreservesOutOf709Negatives()
    {
        // After BT.2020 → BT.709, a channel can go negative — keep for gamut.
        var rgba = new float[] { 0.1f, -0.05f, 0.2f, 1f };
        var frame = HdrJxlPresent.BuildPresentFrame(rgba, 1, 1, 0, 0, ImageScaling.Fit);
        Assert.NotNull(frame);
        Assert.Equal(-0.05f, frame!.ScrgbRgba[1], 4);
    }

    [Fact]
    public void BuildPresentFrame_HonorsCancellationBeforeBoxScale()
    {
        var rgba = new float[4 * 4 * 4];
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            HdrJxlPresent.BuildPresentFrame(rgba, 4, 4, 2, 2, ImageScaling.Fit, cts.Token));
    }

    [Fact]
    public void PixelCaps_Present64M_Thumb120M()
    {
        Assert.Equal(64_000_000, HdrJxlPresent.PresentMaxPixels);
        Assert.Equal(120_000_000, HdrJxlPresent.ThumbMaxPixels);
        Assert.True(HdrJxlPresent.ThumbMaxPixels > HdrJxlPresent.PresentMaxPixels);
    }

    [Fact]
    public void ToneMap_EightyNitScale_PullsHotPaperWhiteDown()
    {
        // Post-Normalize PQ paper white at 203/80 ≈ 2.54 scRGB — ToneMap must
        // expose down (not leave thumbs ~2.5× hotter than the old ÷203 path).
        var paper = GalleryPresent.SdrReferenceNits / GalleryPresent.ScrgbNits;
        Span<float> rgba = stackalloc float[] { paper, paper, paper, 1f };
        var (exposure, white) = ToneMap.Analyze(rgba, 1);
        Assert.True(exposure < 1f, $"expected exposure < 1 for hot paper white, got {exposure}");
        Assert.True(white >= 1f);
        var bgra = ToneMap.ScrgbToBgra8(rgba, 1, 1);
        Assert.InRange(bgra[0], 1, 254); // not clipped black or full white wash
    }
}
