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
            "HDR PNG · presenting scRGB",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrPng, 9, 16, 1000), true, false));
        Assert.Equal(
            "HDR AVIF · presenting scRGB",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrAvif, 9, 16, 1000), true, false));
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
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrPng, 9, 16, 1000), true, false, true, 400));
        Assert.Equal(
            "HDR PNG · presenting scRGB",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrPng, 9, 16, 1000), true, false));
        Assert.Equal(
            "HDR AVIF · clip 400 nits (override)",
            GalleryPresent.StatusLine(new HdrProbe(HdrKind.HdrAvif, 9, 16, 1000), true, false, true, 400));
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
        var probe = HdrFile.Probe(avif);
        Assert.Equal(HdrKind.HdrAvif, probe.Kind);
        Assert.True(probe.IsPq);
        Assert.Equal((3840, 2160), AvifFile.TryReadSize(avif));
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
        WriteBox(ipco, "colr", Nclx(primaries, transfer));
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

    private static byte[] AvifWithPrimaryAndThumb()
    {
        using var ipco = new MemoryStream();
        WriteBox(ipco, "ispe", [.. Be32(512), .. Be32(512)], fullBox: true);
        WriteBox(ipco, "colr", Nclx(1, 13));
        WriteBox(ipco, "ispe", [.. Be32(3840), .. Be32(2160)], fullBox: true);
        WriteBox(ipco, "colr", Nclx(9, 16));

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

    private static byte[] Nclx(int primaries, int transfer)
    {
        var nclx = new byte[11];
        Encoding.ASCII.GetBytes("nclx").CopyTo(nclx, 0);
        nclx[4] = (byte)(primaries >> 8);
        nclx[5] = (byte)primaries;
        nclx[6] = (byte)(transfer >> 8);
        nclx[7] = (byte)transfer;
        nclx[9] = 9;
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
