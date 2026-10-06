using System.Text;
using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class HdrFileTests
{
    [Fact]
    public void Probe_Empty_IsNone()
    {
        Assert.Equal(HdrKind.None, HdrFile.Probe([]).Kind);
        Assert.Equal(HdrKind.None, HdrFile.Probe(new byte[] { 1, 2, 3 }).Kind);
    }

    [Fact]
    public void Probe_PqPng_IsHdr()
    {
        var png = PngWithChunks(("cICP", [9, 16, 0, 1]));
        var probe = HdrFile.Probe(png);
        Assert.Equal(HdrKind.HdrPng, probe.Kind);
        Assert.True(probe.CanPresentHdr);
        Assert.True(probe.IsPq);
        Assert.Equal(9, probe.CicpPrimaries);
    }

    [Fact]
    public void Probe_HlgPng_IsHdr()
    {
        var png = PngWithChunks(("cICP", [9, 18, 0, 1]));
        var probe = HdrFile.Probe(png);
        Assert.Equal(HdrKind.HdrPng, probe.Kind);
        Assert.True(probe.IsHlg);
    }

    [Fact]
    public void Probe_ClliPng_IsHdrEvenWithoutCicp()
    {
        var png = PngWithChunks(("cLLi", [0, 0, 0x27, 0x10, 0, 0, 0, 0]));
        var probe = HdrFile.Probe(png);
        Assert.Equal(HdrKind.HdrPng, probe.Kind);
        Assert.Equal(1, probe.MaxCllNits);
    }

    [Fact]
    public void Probe_P3SdrPng_IsWideGamut()
    {
        var png = PngWithChunks(("cICP", [12, 13, 0, 1]));
        var probe = HdrFile.Probe(png);
        Assert.Equal(HdrKind.WideGamutPng, probe.Kind);
        Assert.False(probe.CanPresentHdr);
        Assert.False(probe.IsHdr);
    }

    [Fact]
    public void Probe_OrdinaryPng_IsNone()
    {
        var png = PngWithChunks();
        Assert.Equal(HdrKind.None, HdrFile.Probe(png).Kind);
    }

    [Fact]
    public void Probe_UltraHdrJpeg_FromGainMapXmp()
    {
        var jpeg = JpegWithXmp("hdrgm:Version=\"1.0\"");
        var probe = HdrFile.Probe(jpeg);
        Assert.Equal(HdrKind.UltraHdrJpeg, probe.Kind);
        Assert.True(probe.IsHdr);
        Assert.False(probe.CanPresentHdr);
    }

    [Fact]
    public void Probe_OrdinaryJpeg_IsNone()
    {
        var jpeg = JpegWithXmp("sRGB");
        Assert.Equal(HdrKind.None, HdrFile.Probe(jpeg).Kind);
    }

    [Fact]
    public void ProbePath_SkipsOnlineOnlyWithoutReading()
    {
        Assert.Equal(HdrKind.None, HdrFile.ProbePath(null).Kind);
        Assert.Equal(HdrKind.None, HdrFile.ProbePath("").Kind);
        Assert.Equal(HdrKind.None, HdrFile.ProbePath("/no/such/file.png").Kind);
    }

    [Fact]
    public void ProbePath_ReadsLocalHdrPng()
    {
        var path = Path.Combine(Path.GetTempPath(), "palace-hdr-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(path, PngWithChunks(("cICP", [9, 16, 0, 1])));
            var probe = HdrFile.ProbePath(path);
            Assert.Equal(HdrKind.HdrPng, probe.Kind);
        }
        finally
        {
            try { File.Delete(path); } catch { /* temp */ }
        }
    }

    [Fact]
    public void ShouldAttemptHdrPresent_OnlyLocalHdrPng()
    {
        var hdr = new HdrProbe(HdrKind.HdrPng, 9, 16, 1000);
        var avif = new HdrProbe(HdrKind.HdrAvif, 9, 16, 1000);
        Assert.True(GalleryPresent.ShouldAttemptHdrPresent(AssetKind.Image, false, false, false, true, hdr));
        Assert.True(GalleryPresent.ShouldAttemptHdrPresent(AssetKind.Image, false, false, false, true, avif));
        Assert.True(GalleryPresent.ShouldAttemptHdrPresent(
            AssetKind.Image, false, false, false, true, new HdrProbe(HdrKind.HdrRadiance, 1, null, null)));
        Assert.True(GalleryPresent.ShouldAttemptHdrPresent(
            AssetKind.Image, false, false, false, true, new HdrProbe(HdrKind.HdrJxr, 1, null, null)));
        Assert.True(GalleryPresent.ShouldAttemptHdrPresent(
            AssetKind.Image, false, false, false, true, new HdrProbe(HdrKind.HdrAvif, 9, 16, 1000)));
        Assert.False(GalleryPresent.ShouldAttemptHdrPresent(AssetKind.Image, false, true, false, true, hdr));
        Assert.False(GalleryPresent.ShouldAttemptHdrPresent(AssetKind.Image, false, false, true, true, hdr));
        Assert.False(GalleryPresent.ShouldAttemptHdrPresent(AssetKind.Gif, false, false, false, true, hdr));
        Assert.False(GalleryPresent.ShouldAttemptHdrPresent(
            AssetKind.Image, false, false, false, true, new HdrProbe(HdrKind.UltraHdrJpeg, null, null, null)));
    }

    [Fact]
    public void DetailsAndStatus_FormatHonestLines()
    {
        Assert.Equal(
            "shot.png · 3840×2160 · Image · 1.5 MB",
            GalleryPresent.DetailsLine("shot.png", 3840, 2160, AssetKind.Image, 1_572_864));
        Assert.Equal("12 B", GalleryPresent.FormatSize(12));
        Assert.Equal("size unknown", GalleryPresent.FormatSize(null));
        Assert.Equal(
            "Ultra HDR JPEG — showing the SDR base",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.UltraHdrJpeg, null, null, null), false, true));
        Assert.Equal(
            "HDR PNG · presenting scRGB",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrPng, 9, 16, 1000), true, true));
        Assert.Equal(
            "HDR PNG · tonemap SDR 80 nits",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrPng, 9, 16, 1000), true, false, false, 203));
        Assert.Equal(
            "HDR AVIF · tonemap SDR 80 nits",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrAvif, 9, 16, 1000), true, false));
        Assert.Equal(
            "HDR AVIF · presenting scRGB",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrAvif, 9, 16, 1000), true, true));
        Assert.Equal(
            "HDR JPEG XR · presenting scRGB",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrJxr, 1, null, null), true, true));
        Assert.Equal(
            "HDR JPEG XR — SDR preview",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrJxr, 1, null, null), false, true));
        Assert.Equal(
            "HDR JPEG XR · tonemap SDR 80 nits",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrJxr, 1, null, null), true, false));
        Assert.Null(GalleryPresent.StatusLine(HdrProbe.None, false, false));
    }

    [Fact]
    public void Tonemap_MapsCllToDisplayAndClips()
    {
        Assert.Equal(1f, GalleryPresent.TonemapScale(200, 400));
        Assert.Equal(0.5f, GalleryPresent.TonemapScale(800, 400), 3);
        Assert.Equal(400f, GalleryPresent.MapCllAndClip(800, 800, 400), 2);
        Assert.Equal(0f, GalleryPresent.MapCllAndClip(-4, 800, 400));
    }

    [Fact]
    public void TransferOf_PqOnlyWhenCicp16()
    {
        Assert.Equal(HdrTransfer.Pq, new HdrProbe(HdrKind.HdrPng, 9, 16, 1000).Transfer);
        Assert.Equal(HdrTransfer.Hlg, new HdrProbe(HdrKind.HdrPng, 9, 18, 1000).Transfer);
        Assert.Equal(HdrTransfer.Linear, new HdrProbe(HdrKind.HdrPng, 9, 8, 1000).Transfer);
        Assert.Equal(HdrTransfer.Srgb, new HdrProbe(HdrKind.HdrPng, null, null, 1000).Transfer);
        Assert.Equal(HdrTransfer.Srgb, new HdrProbe(HdrKind.HdrPng, 1, 13, 400).Transfer);
        Assert.False(new HdrProbe(HdrKind.HdrPng, null, null, 1000).IsPq);
    }

    [Fact]
    public void EncodedToNits_UnknownTransferIsNotPq()
    {
        var nits = GalleryPresent.EncodedToNits(1f, HdrTransfer.Srgb);
        Assert.Equal(GalleryPresent.SdrReferenceNits, nits, 2);
        Assert.True(nits < 300);
        Assert.Equal(10000f, GalleryPresent.EncodedToNits(1f, HdrTransfer.Pq), 1);
        Assert.InRange(GalleryPresent.SrgbEotf(0.5f), 0.20f, 0.22f);
    }

    [Fact]
    public void Present_SdrMapsCllToDisplay_NeverPaperWhite()
    {
        Assert.False(GalleryPresent.IsAdvancedColor(0));
        Assert.True(GalleryPresent.IsAdvancedColor(1));
        Assert.True(GalleryPresent.IsAdvancedColor(12));
        Assert.True(GalleryPresent.IsAdvancedColor(13));
        Assert.True(GalleryPresent.IsAdvancedColor(16));
        Assert.True(GalleryPresent.IsAdvancedColor(18));
        Assert.True(GalleryPresent.IsAdvancedColor(20));
        Assert.True(GalleryPresent.IsAdvancedColor(21));
        Assert.False(GalleryPresent.IsAdvancedColor(19));
        Assert.True(GalleryPresent.ColorSpaceSupportsPresent(1));
        Assert.False(GalleryPresent.ColorSpaceSupportsPresent(0));

        // G22 desktop composition while Windows HDR is on (10-bit HDR400).
        Assert.True(GalleryPresent.IsHdrOutput(0, 400, 400, 10));
        Assert.True(GalleryPresent.IsHdrOutput(0, 1499, 1000, 10));
        Assert.True(GalleryPresent.IsHdrOutput(12, 400, 400, 10));
        Assert.True(GalleryPresent.IsHdrOutput(1, 80, 80, 8));
        // Dummy 270 at 8-bit G22 is SDR; 203 is paper white, not a peak.
        Assert.False(GalleryPresent.IsHdrOutput(0, 270, 270, 8));
        Assert.False(GalleryPresent.IsHdrOutput(0, 203, 203, 8));
        Assert.False(GalleryPresent.IsHdrOutput(0, 120, 80, 8));
        Assert.True(GalleryPresent.IsDummySdrLuminance(270));
        Assert.False(GalleryPresent.IsDummySdrLuminance(400));
        // 8-bit G22 with a real HDR peak (not dummy 270) is still HDR.
        Assert.True(GalleryPresent.IsHdrOutput(0, 400, 400, 8));
        var g22Hdr = GalleryPresent.PresentMap(
            GalleryPresent.IsHdrOutput(0, 400, 400, 10), 1000, 400);
        Assert.Equal(1f, g22Hdr.Scale);
        Assert.Equal(400f / 80f, g22Hdr.ClipScrgb, 3);
        var dummySdr = GalleryPresent.PresentMap(
            GalleryPresent.IsHdrOutput(0, 270, 270, 8), 1000, 80);
        Assert.Equal(0.08f, dummySdr.Scale, 3);
        Assert.Equal(1f, dummySdr.ClipScrgb, 3);
        Assert.Equal(80f, GalleryPresent.SdrPresentPeakNits(0, 0));
        Assert.Equal(80f, GalleryPresent.SdrPresentPeakNits(203, 0));
        Assert.Equal(80f, GalleryPresent.SdrPresentPeakNits(270, 270));
        Assert.Equal(80f, GalleryPresent.SdrPresentPeakNits(120, 0));
        Assert.Equal(80f, GalleryPresent.SdrPresentPeakNits(200, 180));
        Assert.Equal(1000f, GalleryPresent.ContentMaxNits(12.5f, 400), 2);
        Assert.Equal(400f, GalleryPresent.ContentMaxNits(0, 400), 2);

        var sdr = GalleryPresent.PresentMap(false, 1000, 0);
        Assert.Equal(0.08f, sdr.Scale, 3);
        Assert.Equal(1f, sdr.ClipScrgb, 3);
        var paper = GalleryPresent.PresentMap(false, 1000, 203);
        Assert.Equal(sdr.Scale, paper.Scale, 3);
        Assert.Equal(sdr.ClipScrgb, paper.ClipScrgb, 3);
        var edid200 = GalleryPresent.PresentMap(false, 1000, 200);
        Assert.Equal(0.08f, edid200.Scale, 3);
        Assert.Equal(1f, edid200.ClipScrgb, 3);

        var hdrUnknown = GalleryPresent.PresentMap(true, 1000, 0);
        Assert.Equal(1f, hdrUnknown.Scale);
        Assert.Equal(125f, hdrUnknown.ClipScrgb, 3);
        var hdrPeak = GalleryPresent.PresentMap(true, 1000, 1499);
        Assert.Equal(1f, hdrPeak.Scale);
        Assert.Equal(1499f / 80f, hdrPeak.ClipScrgb, 3);

        Assert.Equal(1f, HdrColor.MapCllToDisplayScrgb(12.5f, 1000, 80), 3);
        Assert.True(HdrColor.MapCllToDisplayScrgb(
            GalleryPresent.SdrReferenceNits / GalleryPresent.ScrgbNits, 1000, 80) < 0.3f);
    }

    [Fact]
    public void Present_SdrMapPeakDoesNotFollowDecodeSize()
    {
        var viewport = GalleryPresent.ContentMaxNits(5f, 0);
        var native = GalleryPresent.ContentMaxNits(12.5f, 0);
        Assert.Equal(400f, viewport, 2);
        Assert.Equal(1000f, native, 2);
        Assert.NotEqual(
            GalleryPresent.PresentMap(false, viewport, 0).Scale,
            GalleryPresent.PresentMap(false, native, 0).Scale);

        // First Fit (viewport) then 1:1 (native): keep the higher peak.
        var afterNative = GalleryPresent.StickyContentMaxNits(native, viewport);
        Assert.Equal(native, afterNative, 2);
        // Fit again with the lower viewport peak must not shrink the map.
        var fitAgain = GalleryPresent.StickyContentMaxNits(viewport, afterNative);
        Assert.Equal(native, fitAgain, 2);
        Assert.Equal(
            GalleryPresent.PresentMap(false, native, 0).Scale,
            GalleryPresent.PresentMap(false, fitAgain, 0).Scale,
            3);

        // Header cLLi seeds first Fit so the map is not weaker than later 1:1.
        var firstFitWithCll = GalleryPresent.StickyContentMaxNits(5f, 0, 1000, 0);
        Assert.Equal(native, firstFitWithCll, 2);
        Assert.Equal(
            GalleryPresent.PresentMap(false, native, 0).Scale,
            GalleryPresent.PresentMap(false, firstFitWithCll, 0).Scale,
            3);
        // Pin-first (Autofix) would freeze 400 and leave 1:1 overblown.
        Assert.NotEqual(viewport, firstFitWithCll, 2);
        Assert.Equal(0, GalleryPresent.StickyContentMaxNits(-4, -8), 2);

        // Next/prev: a dimmer still must not keep the prior MaxCLL.
        var nextStill = GalleryPresent.StickyContentMaxNitsForStill(
            @"C:\hdr\dim.avif",
            @"C:\hdr\bright.avif",
            5f,
            0,
            null,
            4000f);
        Assert.Equal(viewport, nextStill, 2);
        Assert.NotEqual(4000f, nextStill, 2);
        var sameStill = GalleryPresent.StickyContentMaxNitsForStill(
            @"C:\hdr\bright.avif",
            @"C:\hdr\bright.avif",
            5f,
            0,
            1000,
            4000f);
        Assert.Equal(4000f, sameStill, 2);
        Assert.Equal(
            viewport,
            GalleryPresent.StickyContentMaxNitsForStill(null, @"C:\hdr\bright.avif", 5f, 0, null, 4000f),
            2);
    }

    [Fact]
    public void Present_ClipsPeakWithoutCrushingByMaxCll()
    {
        Assert.Equal(203f, GalleryPresent.ClipToPeak(1000, 203), 2);
        Assert.Equal(100f, GalleryPresent.ClipToPeak(100, 203), 2);
        Assert.Equal(0f, GalleryPresent.ClipToPeak(-4, 203));
        var referenceWhite = GalleryPresent.SdrReferenceNits;
        Assert.Equal(referenceWhite, GalleryPresent.ClipToPeak(referenceWhite, 203), 2);
        Assert.True(GalleryPresent.MapCllAndClip(referenceWhite, 1000, 203) < 50);
    }

    [Fact]
    public void EffectivePeakNits_OverrideIsOptional()
    {
        Assert.Equal(800f, GalleryPresent.EffectivePeakNits(800, false, 400), 2);
        Assert.Equal(0f, GalleryPresent.EffectivePeakNits(0, false, 400), 2);
        Assert.Equal(400f, GalleryPresent.EffectivePeakNits(800, true, 400), 2);
        Assert.Equal(GalleryPresent.MinPeakNits, GalleryPresent.EffectivePeakNits(800, true, 10), 2);
        Assert.Equal(GalleryPresent.MaxPeakNits, GalleryPresent.EffectivePeakNits(800, true, 99999), 2);
        Assert.Equal("400 nits", GalleryPresent.PeakNitsLabel(400));
        Assert.Equal(
            "HDR PNG · clip 400 nits (override)",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrPng, 9, 16, 1000), true, true, true, 400));
        Assert.Equal(
            "HDR PNG · presenting scRGB",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrPng, 9, 16, 1000), true, true));
        Assert.Equal(
            "HDR PNG · tonemap SDR 80 nits",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrPng, 9, 16, 1000), true, false, true, 400));
        Assert.Equal(
            "HDR AVIF · clip 400 nits (override)",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrAvif, 9, 16, 1000), true, true, true, 400));
    }

    [Fact]
    public void ImageInfoText_MatchesSkivStatsNotToolbar()
    {
        var probe = new HdrProbe(HdrKind.HdrPng, 9, 16, 1000);
        Assert.Equal("BT.2020 · PQ", GalleryPresent.ColorLabel(probe));
        var text = GalleryPresent.ImageInfoText(new GalleryImageInfo(
            "shot.png",
            1_572_864,
            3840,
            2160,
            probe,
            "HDR PNG · presenting scRGB",
            1682,
            25.9f,
            0.1f,
            1499,
            32.156f));
        Assert.Contains("Image: shot.png", text);
        Assert.Contains("File size: 1.5 MB", text);
        Assert.Contains("Resolution: 3840×2160", text);
        Assert.Contains("Color: BT.2020 · PQ", text);
        Assert.Contains("HDR: HDR PNG · presenting scRGB", text);
        Assert.Contains("MaxCLL (scRGB): 32.156", text);
        Assert.Contains("Max luminance: 1682 nits", text);
        Assert.Contains("Avg luminance: 25.9 nits", text);
        Assert.Contains("Min luminance: 0.1 nits", text);
        Assert.Contains("Display luminance: 1499 nits", text);
        Assert.DoesNotContain("Display peak: 203", text);
        Assert.DoesNotContain("MaxCLL: 1000 nits", text);
        Assert.DoesNotContain("Save As", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Export", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Clipboard", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("heatmap", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("", GalleryPresent.ImageInfoText(new GalleryImageInfo(
            null, null, null, null, HdrProbe.None, null, null, null, null, null)));
    }

    [Fact]
    public void FilePixelSize_PrefersNativeThenHeaderOverCatalogThumb()
    {
        Assert.Equal((3840, 2160), GalleryPresent.FilePixelSize(3840, 2160, 3840, 2160, 512, 512));
        Assert.Equal((3840, 2160), GalleryPresent.FilePixelSize(null, null, 3840, 2160, 512, 512));
        Assert.Equal((512, 512), GalleryPresent.FilePixelSize(null, null, null, null, 512, 512));
        Assert.Null(GalleryPresent.FilePixelSize(null, null, null, null, null, null));
    }

    [Fact]
    public void LuminanceY_IsCieYNotMaxRgb()
    {
        var maxRgb = 32.156f * GalleryPresent.ScrgbNits;
        Assert.Equal(2572.48f, maxRgb, 1);
        Assert.InRange(GalleryPresent.LuminanceY(maxRgb, maxRgb, maxRgb, true), 2570f, 2575f);
        Assert.True(GalleryPresent.LuminanceY(maxRgb, 0, 0, true) < 800);
        Assert.Equal(
            (0.2126f * 80f) + (0.7152f * 80f) + (0.0722f * 80f),
            GalleryPresent.LuminanceY(80, 80, 80, false),
            2);
    }

    [Fact]
    public void DisplayLuminance_NeverSubstitutesPaperWhite()
    {
        Assert.Equal(0f, GalleryPresent.ProbedDisplayLuminance(0, 0));
        Assert.Equal(0f, GalleryPresent.ProbedDisplayLuminance(203, 0));
        Assert.Equal(1499f, GalleryPresent.ProbedDisplayLuminance(1682, 1499), 0);
        Assert.Equal(800f, GalleryPresent.ProbedDisplayLuminance(800, 20), 0);
        Assert.False(GalleryPresent.IsHdrDisplay(203));
        Assert.True(GalleryPresent.IsHdrDisplay(1499));
        Assert.Equal(10000f / GalleryPresent.ScrgbNits, GalleryPresent.RasterizeClipScrgb(0), 3);
        Assert.Equal(1499f / GalleryPresent.ScrgbNits, GalleryPresent.RasterizeClipScrgb(1499), 3);
        Assert.Equal(1000f, GalleryPresent.ClipToPeak(1000, 0), 2);
    }

    [Fact]
    public void PresentDecodeSize_FitUsesViewportNotNative4k()
    {
        var fit = GalleryPresent.PresentDecodeSize(3840, 2160, 1280, 720, ImageScaling.Fit);
        Assert.Equal((1280, 720), fit);
        var unknown = GalleryPresent.PresentDecodeSize(3840, 2160, 0, 0, ImageScaling.Fit);
        Assert.True(Math.Max(unknown.Width, unknown.Height) <= GalleryPresent.FastPresentLongEdge);
        Assert.Equal((3840, 2160), GalleryPresent.PresentDecodeSize(3840, 2160, 1280, 720, ImageScaling.Actual));
        Assert.Equal(fit, GalleryPresent.MeasureDecodeSize(3840, 2160, 1280, 720));
        var measureHuge = GalleryPresent.MeasureDecodeSize(16384, 16384, 0, 0);
        Assert.True(Math.Max(measureHuge.Width, measureHuge.Height) <= GalleryPresent.FastPresentLongEdge);
        Assert.True(GalleryPresent.NeedsBetterDecode(1280, 720, 3840, 2160));
        Assert.False(GalleryPresent.NeedsBetterDecode(3840, 2160, 1280, 720));
        Assert.True(GalleryPresent.IsNativeDecode(3840, 2160, 3840, 2160));
        Assert.False(GalleryPresent.IsNativeDecode(1280, 720, 3840, 2160));
        Assert.True(GalleryPresent.ReplaceHdrStats(false, false));
        Assert.False(GalleryPresent.ReplaceHdrStats(true, false));
        Assert.True(GalleryPresent.ReplaceHdrStats(true, true));
    }

    [Fact]
    public void LiveTokenSource_ReplacesCancelledCts()
    {
        using var live = new CancellationTokenSource();
        Assert.Same(live, GalleryPresent.LiveTokenSource(live));

        var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var replacement = GalleryPresent.LiveTokenSource(cancelled);
        Assert.NotSame(cancelled, replacement);
        Assert.False(replacement.IsCancellationRequested);

        using var minted = GalleryPresent.LiveTokenSource(null);
        Assert.False(minted.IsCancellationRequested);
    }

    [Fact]
    public void Probe_PqAvif_IsHdrAndPresentable()
    {
        var avif = AvifWith(3840, 2160, primaries: 9, transfer: 16);
        var probe = HdrFile.Probe(avif);
        Assert.Equal(HdrKind.HdrAvif, probe.Kind);
        Assert.True(probe.CanPresentHdr);
        Assert.True(probe.IsPq);
        Assert.Equal(9, probe.CicpPrimaries);
        Assert.Equal((3840, 2160), AvifFile.TryReadSize(avif));
        using var stream = new MemoryStream(avif);
        Assert.Equal((3840, 2160), ImageDimensions.TryRead(stream));
    }

    [Fact]
    public void Probe_P3SdrAvif_IsWideGamut()
    {
        var avif = AvifWith(64, 48, primaries: 12, transfer: 13);
        var probe = HdrFile.Probe(avif);
        Assert.Equal(HdrKind.WideGamutAvif, probe.Kind);
        Assert.False(probe.CanPresentHdr);
        Assert.Equal("Wide-gamut AVIF", GalleryPresent.StatusLine(probe, false, false));
    }

    [Fact]
    public void Probe_AvifPrimaryItem_IgnoresThumbIspeAndNclx()
    {
        var avif = AvifWithPrimaryAndThumb();
        var info = AvifFile.Probe(avif);
        Assert.Equal(3840, info.Width);
        Assert.Equal(2160, info.Height);
        Assert.Equal(9, info.CicpPrimaries);
        Assert.Equal(16, info.CicpTransfer);
        Assert.Equal(9, info.CicpMatrix);
        var probe = HdrFile.Probe(avif);
        Assert.Equal(HdrKind.HdrAvif, probe.Kind);
        Assert.True(probe.IsPq);
        Assert.Equal((3840, 2160), AvifFile.TryReadSize(avif));
    }

    [Fact]
    public void Probe_AvifPrimaryItem_IdentityMatrix_IgnoresThumbNclx()
    {
        var avif = AvifWithPrimaryAndThumb(primaryMatrix: 0, thumbMatrix: 9);
        var info = AvifFile.Probe(avif);
        Assert.Equal(3840, info.Width);
        Assert.Equal(2160, info.Height);
        Assert.Equal(9, info.CicpPrimaries);
        Assert.Equal(16, info.CicpTransfer);
        Assert.Equal(0, info.CicpMatrix);
        Assert.True(info.FullRange);
        var probe = HdrFile.Probe(avif);
        Assert.Equal(HdrKind.HdrAvif, probe.Kind);
        Assert.True(probe.IsPq);
        Assert.Equal(0, probe.CicpMatrix);
        Assert.True(probe.FullRange);
    }

    [Fact]
    public void Probe_SdrAvif_IsNoneHdr()
    {
        var avif = AvifWith(32, 32, primaries: 1, transfer: 13);
        Assert.Equal(HdrKind.None, HdrFile.Probe(avif).Kind);
        Assert.Equal((32, 32), AvifFile.TryReadSize(avif));
    }

    [Fact]
    public void PathSafe_IndexesAvifAsImage()
    {
        Assert.Contains(".avif", PathSafe.ImageExt);
        Assert.True(PathSafe.IsCatalogExt(".avif"));
        Assert.True(PathSafe.IsCatalogExt(".AVIF"));
        Assert.True(PathSafe.IsAvif(".avif"));
        Assert.Equal(AssetKind.Image, PathSafe.KindFromExt(".avif"));
        Assert.True(PathSafe.IsCatalogExt(".jxl"));
        Assert.False(PathSafe.IsCatalogExt(".exr"));
    }

    [Fact]
    public void AvifTryReadSize_StopsAfterIspe_DoesNotReadTrailingBox()
    {
        var core = AvifWith(64, 48, primaries: 1, transfer: 13);
        using var packed = new MemoryStream();
        packed.Write(core);
        WriteBox(packed, "free", new byte[48 * 1024]);
        var bytes = packed.ToArray();
        using var stream = new CountingStream(bytes);
        Assert.Equal((64, 48), AvifFile.TryReadSize(stream));
        Assert.True(stream.BytesRead < bytes.Length - 1024, $"read {stream.BytesRead} of {bytes.Length}");
    }

    [Fact]
    public void PqEotf_ZeroAndPeak()
    {
        Assert.Equal(0f, GalleryPresent.PqEotf(0), 3);
        Assert.InRange(GalleryPresent.PqEotf(1), 0.99f, 1.01f);
        Assert.True(GalleryPresent.PqEotf(0.5f) > 0);
    }

    [Fact]
    public void DestRect_FitFillActual()
    {
        var fit = GalleryPresent.DestRect(ImageScaling.Fit, 200, 100, 100, 100);
        Assert.Equal(100f, fit.W, 2);
        Assert.Equal(50f, fit.H, 2);
        Assert.Equal(0f, fit.X, 2);
        Assert.Equal(25f, fit.Y, 2);

        var fill = GalleryPresent.DestRect(ImageScaling.Fill, 200, 100, 100, 100);
        Assert.Equal(200f, fill.W, 2);
        Assert.Equal(100f, fill.H, 2);
        Assert.Equal(-50f, fill.X, 2);
        var fillCoverBuffer = GalleryPresent.DestRect(ImageScaling.Fill, 200, 100, 200, 100);
        Assert.Equal((0f, 0f, 200f, 100f), fillCoverBuffer);

        var actual = GalleryPresent.DestRect(ImageScaling.Actual, 200, 100, 200, 100);
        Assert.Equal((0f, 0f, 200f, 100f), actual);
        var actualHiDpi = GalleryPresent.DestRect(ImageScaling.Actual, 200, 100, 300, 150);
        Assert.Equal((0f, 0f, 200f, 100f), actualHiDpi);
    }

    [Fact]
    public void HdrPixels_Rgba8IsNotBgra()
    {
        HdrPixels.Read([255, 0, 0, 255], 0, HdrPackedFormat.Rgba8, out var rr, out var rg, out var rb, out var ra);
        Assert.Equal(1f, rr);
        Assert.Equal(0f, rg);
        Assert.Equal(0f, rb);
        Assert.Equal(1f, ra);

        HdrPixels.Read([0, 0, 255, 255], 0, HdrPackedFormat.Bgra8, out var br, out var bg, out var bb, out var ba);
        Assert.Equal(1f, br);
        Assert.Equal(0f, bg);
        Assert.Equal(0f, bb);
        Assert.Equal(1f, ba);

        HdrPixels.Read([0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x80], 0, HdrPackedFormat.Rgba16, out var hr, out var hg, out var hb, out var ha);
        Assert.Equal(0f, hr);
        Assert.Equal(1f, hg);
        Assert.Equal(0f, hb);
        Assert.InRange(ha, 0.49f, 0.51f);

        var half2 = BitConverter.GetBytes((Half)2f);
        var half1 = BitConverter.GetBytes((Half)1f);
        var halfPx = new byte[8];
        half2.CopyTo(halfPx, 0);
        half1.CopyTo(halfPx, 2);
        half1.CopyTo(halfPx, 4);
        half1.CopyTo(halfPx, 6);
        HdrPixels.Read(halfPx, 0, HdrPackedFormat.RgbaHalf, out var rHalf, out var gHalf, out var bHalf, out var aHalf);
        Assert.Equal(2f, rHalf);
        Assert.Equal(1f, gHalf);
        Assert.Equal(1f, bHalf);
        Assert.Equal(1f, aHalf);
        Assert.Equal(2f, HdrPixels.HalfToFloat(half2, 0));

        var f2 = BitConverter.GetBytes(2.5f);
        var f0 = BitConverter.GetBytes(0f);
        var f1 = BitConverter.GetBytes(1f);
        var floatPx = new byte[16];
        f2.CopyTo(floatPx, 0);
        f0.CopyTo(floatPx, 4);
        f0.CopyTo(floatPx, 8);
        f1.CopyTo(floatPx, 12);
        HdrPixels.Read(floatPx, 0, HdrPackedFormat.RgbaFloat, out var rF, out var gF, out var bF, out var aF);
        Assert.Equal(2.5f, rF);
        Assert.Equal(0f, gF);
        Assert.Equal(0f, bF);
        Assert.Equal(1f, aF);
        Assert.True(HdrPixels.HasPackedData(floatPx, HdrPackedFormat.RgbaFloat, 1, 1));
        Assert.True(HdrPixels.HasPackedData(halfPx, HdrPackedFormat.RgbaHalf, 1, 1));
    }

    [Fact]
    public void OrientedSize_SwapsOn90And270()
    {
        Assert.Equal((200, 100), HdrPixels.OrientedSize(200, 100, 1));
        Assert.Equal((200, 100), HdrPixels.OrientedSize(200, 100, 3));
        Assert.Equal((100, 200), HdrPixels.OrientedSize(200, 100, 6));
        Assert.Equal((100, 200), HdrPixels.OrientedSize(200, 100, 8));
        Assert.Equal((0, 0), HdrPixels.OrientedSize(0, 100, 6));
        Assert.Equal((40, 80), HdrPixels.SourceScaleSize(100, 200, 200, 100, 80, 40));
        Assert.Equal((80, 40), HdrPixels.SourceScaleSize(200, 100, 200, 100, 80, 40));
        Assert.Equal((80, 80), HdrPixels.SourceScaleSize(100, 100, 100, 100, 80, 80));
    }

    [Fact]
    public void ExifOrientationFromIrotImir_MatchesLibavifTable()
    {
        Assert.Equal(1u, HdrPixels.ExifOrientationFromIrotImir(false, 0, false, 0));
        Assert.Equal(8u, HdrPixels.ExifOrientationFromIrotImir(true, 1, false, 0));
        Assert.Equal(5u, HdrPixels.ExifOrientationFromIrotImir(true, 1, true, 0));
        Assert.Equal(7u, HdrPixels.ExifOrientationFromIrotImir(true, 1, true, 1));
        Assert.Equal(3u, HdrPixels.ExifOrientationFromIrotImir(true, 2, false, 0));
        Assert.Equal(6u, HdrPixels.ExifOrientationFromIrotImir(true, 3, false, 0));
        Assert.Equal(7u, HdrPixels.ExifOrientationFromIrotImir(true, 3, true, 0));
        Assert.Equal(5u, HdrPixels.ExifOrientationFromIrotImir(true, 3, true, 1));
        Assert.Equal(2u, HdrPixels.ExifOrientationFromIrotImir(false, 0, true, 1));
        Assert.Equal(4u, HdrPixels.ExifOrientationFromIrotImir(false, 0, true, 0));
    }

    [Fact]
    public void TryExifOrientationAt_RejectsMissingTagOffset()
    {
        byte[] payload = [6, 7, 8];
        Assert.False(HdrPixels.TryExifOrientationAt(payload, 3, out var missing));
        Assert.Equal(1, missing);
        Assert.False(HdrPixels.TryExifOrientationAt(payload, 4, out _));
        Assert.True(HdrPixels.TryExifOrientationAt(payload, 0, out var value));
        Assert.Equal(6, value);
        Assert.False(HdrPixels.TryExifOrientationAt([0], 0, out var reserved));
        Assert.Equal(0, reserved);
    }

    [Fact]
    public void OrientScrgbRgba_Rotate90CwMovesCorner()
    {
        // 2×1 stored: left=red, right=green → EXIF 6 (90° CW) is 1×2: top=green, bottom=red.
        var src = new float[]
        {
            1f, 0f, 0f, 1f,
            0f, 1f, 0f, 1f
        };
        var (dst, w, h) = HdrPixels.OrientScrgbRgba(src, 2, 1, 6);
        Assert.Equal(1, w);
        Assert.Equal(2, h);
        Assert.Equal(0f, dst[0]); // top G
        Assert.Equal(1f, dst[1]);
        Assert.Equal(1f, dst[4]); // bottom R
        Assert.Equal(0f, dst[5]);
    }

    [Fact]
    public void OrientScrgbRgba_IrotAngle1IsExif8()
    {
        // irot angle 1 (90° CCW) is EXIF 8: 2×1 left=red right=green → 1×2 top=red, bottom=green.
        var src = new float[]
        {
            1f, 0f, 0f, 1f,
            0f, 1f, 0f, 1f
        };
        Assert.Equal(8u, HdrPixels.ExifOrientationFromIrotImir(true, 1, false, 0));
        var (dst, w, h) = HdrPixels.OrientScrgbRgba(src, 2, 1, 8);
        Assert.Equal(1, w);
        Assert.Equal(2, h);
        Assert.Equal(1f, dst[0]);
        Assert.Equal(0f, dst[1]);
        Assert.Equal(0f, dst[4]);
        Assert.Equal(1f, dst[5]);
    }

    [Fact]
    public void FloatToHalf_RoundTripsCommonValues()
    {
        Assert.Equal(0, GalleryPresent.FloatToHalf(0));
        Assert.Equal(0x3C00, GalleryPresent.FloatToHalf(1f));
        Assert.Equal(0xBC00, GalleryPresent.FloatToHalf(-1f));
    }

    private static byte[] PngWithChunks(params (string Type, byte[] Payload)[] chunks)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(ms, "IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]);
        foreach (var (type, payload) in chunks)
        {
            WriteChunk(ms, type, payload);
        }

        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] payload)
    {
        WriteBe32(stream, payload.Length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(payload);
        stream.Write(new byte[4]);
    }

    private static void WriteBe32(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static byte[] AvifWith(int width, int height, int primaries, int transfer, int? maxCll = null)
    {
        using var ipco = new MemoryStream();
        WriteBox(ipco, "ispe", [.. Be32(width), .. Be32(height)], fullBox: true);
        WriteBox(ipco, "colr", Nclx(primaries, transfer, 9));
        if (maxCll is int cll)
        {
            WriteBox(ipco, "clli", [(byte)(cll >> 8), (byte)cll, 0, 0]);
        }

        using var iprp = new MemoryStream();
        WriteBox(iprp, "ipco", ipco.ToArray());
        using var meta = new MemoryStream();
        WriteBox(meta, "iprp", iprp.ToArray());
        using var ms = new MemoryStream();
        WriteBox(ms, "ftyp", [.. Encoding.ASCII.GetBytes("avif"), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes("avif"), .. Encoding.ASCII.GetBytes("mif1")]);
        WriteBox(ms, "meta", meta.ToArray(), fullBox: true);
        return ms.ToArray();
    }

    private static byte[] AvifWithPrimaryAndThumb(int primaryMatrix = 9, int thumbMatrix = 1)
    {
        using var ipco = new MemoryStream();
        WriteBox(ipco, "ispe", [.. Be32(512), .. Be32(512)], fullBox: true);
        WriteBox(ipco, "colr", Nclx(1, 13, thumbMatrix));
        WriteBox(ipco, "ispe", [.. Be32(3840), .. Be32(2160)], fullBox: true);
        WriteBox(ipco, "colr", Nclx(9, 16, primaryMatrix));

        using var ipma = new MemoryStream();
        WriteBe32(ipma, 2);
        WriteBe16(ipma, 1);
        ipma.WriteByte(2);
        ipma.WriteByte(3);
        ipma.WriteByte(4);
        WriteBe16(ipma, 2);
        ipma.WriteByte(2);
        ipma.WriteByte(1);
        ipma.WriteByte(2);

        using var iprp = new MemoryStream();
        WriteBox(iprp, "ipco", ipco.ToArray());
        WriteBox(iprp, "ipma", ipma.ToArray(), fullBox: true);
        using var meta = new MemoryStream();
        WriteBox(meta, "pitm", Be16(1), fullBox: true);
        WriteBox(meta, "iprp", iprp.ToArray());
        using var ms = new MemoryStream();
        WriteBox(ms, "ftyp", [.. Encoding.ASCII.GetBytes("avif"), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes("avif"), .. Encoding.ASCII.GetBytes("mif1")]);
        WriteBox(ms, "meta", meta.ToArray(), fullBox: true);
        return ms.ToArray();
    }

    private static byte[] Nclx(int primaries, int transfer, int matrix)
    {
        var nclx = new byte[11];
        Encoding.ASCII.GetBytes("nclx").CopyTo(nclx, 0);
        nclx[4] = (byte)(primaries >> 8);
        nclx[5] = (byte)primaries;
        nclx[6] = (byte)(transfer >> 8);
        nclx[7] = (byte)transfer;
        nclx[8] = (byte)(matrix >> 8);
        nclx[9] = (byte)matrix;
        nclx[10] = 0x80;
        return nclx;
    }

    private static void WriteBe16(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static byte[] Be16(int value) =>
        [(byte)(value >> 8), (byte)value];

    private static void WriteBox(Stream stream, string type, byte[] payload, bool fullBox = false)
    {
        var extra = fullBox ? 4 : 0;
        WriteBe32(stream, 8 + extra + payload.Length);
        stream.Write(Encoding.ASCII.GetBytes(type));
        if (fullBox)
        {
            stream.Write(new byte[4]);
        }

        stream.Write(payload);
    }

    private static byte[] Be32(int value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static byte[] JpegWithXmp(string xmp)
    {
        var xmpBytes = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0" + xmp);
        using var ms = new MemoryStream();
        ms.WriteByte(0xFF);
        ms.WriteByte(0xD8);
        ms.WriteByte(0xFF);
        ms.WriteByte(0xE1);
        var len = xmpBytes.Length + 2;
        ms.WriteByte((byte)(len >> 8));
        ms.WriteByte((byte)len);
        ms.Write(xmpBytes);
        ms.WriteByte(0xFF);
        ms.WriteByte(0xD9);
        return ms.ToArray();
    }

    private sealed class CountingStream : MemoryStream
    {
        public int BytesRead { get; private set; }

        public CountingStream(byte[] data)
            : base(data)
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = base.Read(buffer, offset, count);
            BytesRead += n;
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            var n = base.Read(buffer);
            BytesRead += n;
            return n;
        }
    }
}
