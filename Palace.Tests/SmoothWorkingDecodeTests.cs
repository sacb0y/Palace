using Palace.Helpers;
using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothWorkingDecodeTests
{
    [Fact]
    public async Task DecodeAsync_ProducesWorkingRgbaAndStats()
    {
        const int w = 64;
        const int h = 48;
        var src = new float[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            src[i * 4] = 0.5f;
            src[i * 4 + 1] = 0.25f;
            src[i * 4 + 2] = 0.125f;
            src[i * 4 + 3] = 1f;
        }

        using var decode = new SmoothWorkingDecode();
        var opts = ImagingStillNormalize.OptionsForRadiance();
        var result = await decode.DecodeAsync(src, w, h, opts);
        Assert.NotNull(result);
        Assert.Equal(w, result!.Width);
        Assert.Equal(h, result.Height);
        Assert.Equal(w * h * 4, result.WorkingRgba.Length);
        Assert.True(result.Stats.Lc > 0f);

        var tile = SmoothWorkingDecode.TileFromWorkingLevel0(
            result.WorkingRgba, result.Width, result.Height, 0, 0);
        Assert.Equal(SmoothTileMath.BytesPerTileRgba16F(), tile.Length);
    }

    [Fact]
    public async Task Cancel_OnImageChange_ThrowsOrNullsPrior()
    {
        const int w = 256;
        const int h = 256;
        var src = new float[w * h * 4];
        for (var i = 0; i < src.Length; i += 4)
        {
            src[i] = src[i + 1] = src[i + 2] = 0.2f;
            src[i + 3] = 1f;
        }

        using var decode = new SmoothWorkingDecode();
        var opts = ImagingStillNormalize.OptionsForRadiance();
        var first = decode.DecodeAsync(src, w, h, opts);
        // Immediate second decode cancels the first generation.
        var second = decode.DecodeAsync(src, w, h, opts);
        var secondResult = await second;
        Assert.NotNull(secondResult);

        try
        {
            await first;
        }
        catch (OperationCanceledException)
        {
            // expected when the first job lost the race
        }
    }

    [Fact]
    public async Task BuildFromWorking_RetainsSourceForFinerTiles()
    {
        const int w = 600;
        const int h = 400;
        var src = new float[w * h * 4];
        for (var i = 0; i < src.Length; i += 4)
        {
            src[i] = 0.4f;
            src[i + 1] = 0.3f;
            src[i + 2] = 0.2f;
            src[i + 3] = 1f;
        }

        using var decode = new SmoothWorkingDecode();
        var working = await decode.DecodeAsync(src, w, h, ImagingStillNormalize.OptionsForRadiance());
        Assert.NotNull(working);

        var session = new SmoothTileSession(w, h, availableVramBytes: 1L << 28);
        var ms = await session.BuildFromWorkingAsync(working!, CancellationToken.None);
        Assert.True(ms >= 0);
        Assert.True(session.UsedWorkingDecode);
        Assert.True(session.HasRetainedWorking);
        Assert.StartsWith("V2", session.StatusLine(), StringComparison.Ordinal);
        Assert.True(session.CoarseTileCount > 0);

        // After pyramid, L0 is gone from Cpu — TryEnsureCpuTile rematerializes.
        var l0 = new SmoothTileId(0, 0, 0);
        Assert.True(session.TryEnsureCpuTile(l0));
        Assert.True(session.Cpu.TryGet(l0, out var bytes));
        Assert.Equal(SmoothTileMath.BytesPerTileRgba16F(), bytes.Length);
    }

    [Fact]
    public void TileFromWorking_WritesBorderGutter()
    {
        const int w = 16;
        const int h = 16;
        var src = new float[w * h * 4];
        src[0] = 1f;
        src[1] = 0f;
        src[2] = 0f;
        src[3] = 1f;
        var tile = SmoothWorkingDecode.TileFromWorkingLevel0(src, w, h, tileX: 0, tileY: 0);
        Assert.Equal(SmoothTileMath.BytesPerTileRgba16F(), tile.Length);
        // Slot (Border,Border) is content (0,0) — non-zero red half bits.
        var o = (SmoothTileMath.Border * SmoothTileMath.SlotSize + SmoothTileMath.Border) * 8;
        Assert.NotEqual(0, tile[o] | tile[o + 1]);
    }

    [Fact]
    public void TileGutter_1016x1_Ramp_RightGutterMatchesNeighbour()
    {
        // Two content tiles wide: 508 + 508 = 1016. Ramp so tile0 right gutter
        // must equal tile1 first content pixel (neighbour sample, not edge replicate).
        const int w = 1016;
        const int h = 1;
        var src = new float[w * h * 4];
        for (var x = 0; x < w; x++)
        {
            var t = x / (float)(w - 1);
            src[x * 4] = t;
            src[x * 4 + 1] = t * 0.5f;
            src[x * 4 + 2] = 1f - t;
            src[x * 4 + 3] = 1f;
        }

        var tile0 = SmoothWorkingDecode.TileFromWorkingLevel0(src, w, h, tileX: 0, tileY: 0);
        var tile1 = SmoothWorkingDecode.TileFromWorkingLevel0(src, w, h, tileX: 1, tileY: 0);

        // tile0 right gutter: first gutter column past content = slot x = Border + ContentSize
        // (= 2 + 508 = 510). Content of tile1 starts at image x=508.
        // tile0 gutter at local cx = ContentSize (=508) → ix = 0+508 = 508.
        // tile1 content (0,0) → ix = 508.
        var gutterSx = SmoothTileMath.Border + SmoothTileMath.ContentSize; // 510
        var contentSx = SmoothTileMath.Border; // 2
        var sy = SmoothTileMath.Border; // y=0 content row in slot
        var g0 = (sy * SmoothTileMath.SlotSize + gutterSx) * 8;
        var c1 = (sy * SmoothTileMath.SlotSize + contentSx) * 8;

        Assert.Equal(tile0[g0], tile1[c1]);
        Assert.Equal(tile0[g0 + 1], tile1[c1 + 1]);
        Assert.Equal(tile0[g0 + 2], tile1[c1 + 2]);
        Assert.Equal(tile0[g0 + 3], tile1[c1 + 3]);
        // Also second gutter column (cx = ContentSize + 1 → ix = 509).
        var g0b = (sy * SmoothTileMath.SlotSize + gutterSx + 1) * 8;
        var c1b = (sy * SmoothTileMath.SlotSize + contentSx + 1) * 8;
        Assert.Equal(tile0[g0b], tile1[c1b]);
    }

    [Fact]
    public void FitsSmoothWorkingBudget_Allows64MP_NoBoxDownscale()
    {
        Assert.True(SmoothWorkingDecode.FitsSmoothWorkingBudget(8000, 8000)); // 64MP
        Assert.False(SmoothWorkingDecode.FitsSmoothWorkingBudget(9000, 9000));
        // FitSmoothWorkingSize no longer shrinks — over-budget uses strip/tile path.
        var (fw, fh) = SmoothWorkingDecode.FitSmoothWorkingSize(12000, 8000);
        Assert.Equal(12000, fw);
        Assert.Equal(8000, fh);
    }

    [Fact]
    public void ClassifyRoute_OverBudget_StripVsExplicitError()
    {
        Assert.Equal(
            SmoothWorkingDecode.WorkingRoute.WithinBudget,
            SmoothWorkingDecode.ClassifyRoute(8000, 8000, stripSamplerAvailable: false));
        Assert.Equal(
            SmoothWorkingDecode.WorkingRoute.Strip,
            SmoothWorkingDecode.ClassifyRoute(10000, 7000, stripSamplerAvailable: true));
        Assert.Equal(
            SmoothWorkingDecode.WorkingRoute.UnsupportedOverBudget,
            SmoothWorkingDecode.ClassifyRoute(10000, 7000, stripSamplerAvailable: false));
        Assert.Equal(
            SmoothWorkingDecode.WorkingRoute.UnsupportedOverBudget,
            SmoothWorkingDecode.ClassifyRoute(0, 0, stripSamplerAvailable: true));
    }

    [Fact]
    public void ClassifyFileRoute_PngStrip_Vs_ExrExplicitError()
    {
        Assert.True(SmoothWorkingDecode.IsStripCapablePath(@"C:\tmp\checker-10000x7000.png"));
        Assert.False(SmoothWorkingDecode.IsStripCapablePath(@"C:\tmp\huge.exr"));
        Assert.Equal(
            SmoothWorkingDecode.WorkingRoute.Strip,
            SmoothWorkingDecode.ClassifyFileRoute(@"C:\tmp\a.png", 10000, 7000));
        Assert.Equal(
            SmoothWorkingDecode.WorkingRoute.WithinBudget,
            SmoothWorkingDecode.ClassifyFileRoute(@"C:\tmp\a.png", 4000, 3000));
        Assert.Equal(
            SmoothWorkingDecode.WorkingRoute.UnsupportedOverBudget,
            SmoothWorkingDecode.ClassifyFileRoute(@"C:\tmp\huge.exr", 10000, 7000));
    }

    [Fact]
    public void EnumerateStripRects_CoversImage_LastShorter()
    {
        var rects = SmoothWorkingDecode.EnumerateStripRects(1000, 700, stripRows: 256).ToList();
        Assert.Equal(3, rects.Count);
        Assert.Equal(new SmoothWorkingDecode.StripRect(0, 0, 1000, 256), rects[0]);
        Assert.Equal(new SmoothWorkingDecode.StripRect(0, 256, 1000, 256), rects[1]);
        Assert.Equal(new SmoothWorkingDecode.StripRect(0, 512, 1000, 188), rects[2]);
        Assert.Equal(700, rects.Sum(r => r.Height));
        Assert.Empty(SmoothWorkingDecode.EnumerateStripRects(0, 10));
        Assert.Empty(SmoothWorkingDecode.EnumerateStripRects(10, 10, stripRows: 0));
    }

    [Fact]
    public async Task DecodeStrip_FillStrip_NoFullFloatBuffer()
    {
        const int w = 600;
        const int h = 400;
        using var decode = new SmoothWorkingDecode();
        var strips = 0;
        var singleRowFills = 0;
        var result = await decode.DecodeStripToRgba16FAsync(
            w,
            h,
            (y0, rows, dest) =>
            {
                strips++;
                if (rows == 1)
                {
                    singleRowFills++;
                }

                Assert.True(rows <= SmoothWorkingDecode.DefaultStripRows);
                Assert.True(dest.Length >= w * rows * 4);
                for (var i = 0; i < w * rows; i++)
                {
                    dest[i * 4] = 0.5f;
                    dest[i * 4 + 1] = 0.25f;
                    dest[i * 4 + 2] = 0.125f;
                    dest[i * 4 + 3] = 1f;
                }
            });
        Assert.NotNull(result);
        Assert.False(result!.HasFloatWorking);
        Assert.True(strips >= 1);
        Assert.NotEmpty(result.Level0Tiles);
        // Stats from Analyze.Accumulator during tile bands — no fillStrip(y, 1).
        Assert.Equal(0, singleRowFills);
        Assert.True(result.Stats.Lc > 0f);
    }

    /// <summary>
    /// Wimbly #212: strip ImageStats ≡ DecodeAsync (Analyze.Run) within float eps.
    /// Mean-P50 / maxRgb-as-Lc was muting tone vs normal.
    /// </summary>
    [Fact]
    public async Task DecodeStrip_ImageStats_MatchDecodeAsync()
    {
        const int w = 64;
        const int h = 48;
        var src = new float[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var o = ((y * w) + x) * 4;
                var t = x / (float)(w - 1);
                var u = y / (float)(h - 1);
                src[o] = 0.15f + 0.7f * t;
                src[o + 1] = 0.55f + 0.3f * u;
                src[o + 2] = 0.85f - 0.5f * t * u;
                src[o + 3] = 1f;
            }
        }

        var opts = ImagingStillNormalize.OptionsForRadiance();
        using var decode = new SmoothWorkingDecode();
        var normal = await decode.DecodeAsync(src, w, h, opts);
        Assert.NotNull(normal);

        // Working floats after DecodeAsync normalize — feed strip the same Working.
        var working = normal!.WorkingRgba;
        using var decodeStrip = new SmoothWorkingDecode();
        var strip = await decodeStrip.DecodeStripToRgba16FAsync(
            w,
            h,
            (y0, rows, dest) =>
            {
                for (var row = 0; row < rows; row++)
                {
                    var y = y0 + row;
                    Array.Copy(working, y * w * 4, dest, row * w * 4, w * 4);
                }
            });
        Assert.NotNull(strip);

        // Max/MaxRgb exact; P50/P99995/Lc within one Analyze PQ bin (fine-hist rebin).
        Assert.Equal(normal.Stats.Max, strip!.Stats.Max);
        Assert.Equal(normal.Stats.MaxRgb, strip.Stats.MaxRgb);
        Assert.Equal(normal.Stats.RefWhite, strip.Stats.RefWhite);
        var w50 = Analyze.OneAnalyzeBinWidthWorking(normal.Stats.Max, normal.Stats.P50);
        var w99 = Analyze.OneAnalyzeBinWidthWorking(normal.Stats.Max, normal.Stats.P99995);
        var wLc = Analyze.OneAnalyzeBinWidthWorking(normal.Stats.Max, normal.Stats.Lc);
        Assert.True(Math.Abs(normal.Stats.P50 - strip.Stats.P50) <= w50 + 1e-6f);
        Assert.True(Math.Abs(normal.Stats.P99995 - strip.Stats.P99995) <= w99 + 1e-6f);
        Assert.True(Math.Abs(normal.Stats.Lc - strip.Stats.Lc) <= wLc + 1e-6f);
    }

    [Fact]
    public async Task DecodeStrip_ProducesRgba16FTiles_WithoutFullFloat()
    {
        const int w = 600;
        const int h = 400;
        using var decode = new SmoothWorkingDecode();
        var result = await decode.DecodeStripToRgba16FAsync(
            w,
            h,
            (x, y) => (0.5f, 0.25f, 0.125f, 1f));
        Assert.NotNull(result);
        Assert.False(result!.HasFloatWorking);
        Assert.NotEmpty(result.Level0Tiles);
        Assert.Equal(SmoothTileMath.BytesPerTileRgba16F(), result.Level0Tiles[new SmoothTileId(0, 0, 0)].Length);
    }

    [Fact]
    public void TryAllocStripFloats_RejectsOverflow()
    {
        // Doktor #212: checked width×rows×4 — never wrap to a tiny buffer.
        Assert.False(SmoothWorkingDecode.TryAllocStripFloats(int.MaxValue, int.MaxValue, out var huge));
        Assert.Null(huge);
        Assert.False(SmoothWorkingDecode.TryAllocStripFloats(0, 10, out _));
        Assert.False(SmoothWorkingDecode.TryAllocStripFloats(10, 0, out _));
        Assert.True(SmoothWorkingDecode.TryAllocStripFloats(4, 2, out var ok));
        Assert.NotNull(ok);
        Assert.Equal(4 * 2 * 4, ok!.Length);
    }

    [Fact]
    public void ResolveDisplayTarget_Sdr_IgnoresSdrWhite80_PeakNormalizedOne()
    {
        // Wimbly #212: displayIsHdr==false + CCD SdrWhiteLevel 80 must NOT
        // pick Sdr80 (PeakNormalized 80/203 → absolute red ~145 from 220).
        var d = SmoothToneGpu.ResolveDisplayTarget(
            displayIsHdr: false,
            displayPeakNits: 0f,
            sdrWhiteNits: ImagingConstants.ScrgbNits);
        Assert.False(d.IsHdrOutput);
        Assert.Equal(1f, d.PeakNormalized, precision: 5);
        Assert.Equal(ImagingConstants.RefWhiteNits, d.PeakNits);
    }

    [Fact]
    public void FromStats_SdrAbsolute_MidGray128_AndRed220()
    {
        // Encoded mid-gray 128 + red (220,40,40) → working → FromStats after
        // ResolveDisplayTarget(false,80). PeakNormalized must stay 1 so Option A
        // does not apply the 80/203 mute (Wimbly PrintWindow ~145).
        var midLin = ColorMath.SrgbEotf(128f / 255f);
        var rLin = ColorMath.SrgbEotf(220f / 255f);
        var gLin = ColorMath.SrgbEotf(40f / 255f);
        var bLin = ColorMath.SrgbEotf(40f / 255f);
        var stats = new ImageStats(
            P50: midLin,
            P99995: rLin,
            Max: rLin,
            MaxRgb: rLin,
            Lc: 1f,
            RefWhite: 203f,
            MetadataPeak: null,
            SourceClipped: false,
            SourceClipRatio: 0f);
        var display = SmoothToneGpu.ResolveDisplayTarget(
            displayIsHdr: false, displayPeakNits: 0f, sdrWhiteNits: 80f);
        var packed = SmoothToneGpu.FromStats(stats, display);
        Assert.Equal(1f, packed.ToneCb[2], precision: 5); // PeakNormalized
        Assert.Equal(0f, packed.ToneCb[6]); // UseHdrPath

        var p = new ToneMapParams(stats, display, ExposureEv: 0f);
        ToneMapper.MapPixel(midLin, midLin, midLin, in p, out var mr, out _, out _);
        ToneMapper.MapPixel(rLin, gLin, bLin, in p, out var or, out var og, out var ob);

        // Encoded 8-bit after OETF — linear-space tol via small code delta.
        var midEnc = ColorMath.SrgbOetf(mr) * 255f;
        var rEnc = ColorMath.SrgbOetf(or) * 255f;
        var gEnc = ColorMath.SrgbOetf(og) * 255f;
        var bEnc = ColorMath.SrgbOetf(ob) * 255f;
        var muted80 = ColorMath.SrgbOetf(rLin * (ImagingConstants.ScrgbNits / ImagingConstants.RefWhiteNits)) * 255f;
        Assert.InRange(midEnc, 128f - 8f, 128f + 8f);
        Assert.InRange(rEnc, 220f - 8f, 220f + 8f);
        Assert.InRange(gEnc, 40f - 8f, 40f + 8f);
        Assert.InRange(bEnc, 40f - 8f, 40f + 8f);
        // Must not land on the Sdr80 mute (~145).
        Assert.True(Math.Abs(rEnc - 220f) < Math.Abs(rEnc - muted80));
    }

    [Fact]
    public void SmoothToneGpu_FromStats_HdrPresentParams()
    {
        var stats = new ImageStats(
            P50: 0.5f, P99995: 1f, Max: 1f, MaxRgb: 1f, Lc: 1f,
            RefWhite: 203f, MetadataPeak: null, SourceClipped: false, SourceClipRatio: 0f);
        var packed = SmoothToneGpu.FromStats(stats, DisplayTarget.Hdr(1000f));
        Assert.Equal(16, packed.ToneCb.Length);
        Assert.Equal(SmoothToneGpu.DefaultLutSize, packed.LutSamples.Length);
        // HDR OutputScale = 203/80
        Assert.Equal(
            ImagingConstants.RefWhiteNits / ImagingConstants.ScrgbNits,
            packed.ToneCb[12],
            precision: 4);
        Assert.Equal(1f, packed.ToneCb[11]); // HasLut
        Assert.Equal(1f, packed.ToneCb[14]); // SoftNegatives
        // ToneA.z = display PeakNormalized (1000/203), not image MaxRgb.
        Assert.Equal(1000f / ImagingConstants.RefWhiteNits, packed.ToneCb[2], precision: 4);
    }

    [Fact]
    public void SmoothToneGpu_FromStats_SdrOutputScaleOne()
    {
        var stats = new ImageStats(
            P50: 0.5f, P99995: 1f, Max: 1f, MaxRgb: 1f, Lc: 1f,
            RefWhite: 203f, MetadataPeak: null, SourceClipped: false, SourceClipRatio: 0f);
        var packed = SmoothToneGpu.FromStats(stats, DisplayTarget.Sdr203);
        Assert.Equal(1f, packed.ToneCb[12]);
        Assert.Equal(0f, packed.ToneCb[14]);
    }

    [Fact]
    public void SmoothToneGpu_FromStats_FullOnHdrPanel_SdrWhiteOver80()
    {
        var stats = new ImageStats(
            P50: 0.5f, P99995: 1f, Max: 1f, MaxRgb: 1f, Lc: 1f,
            RefWhite: 203f, MetadataPeak: null, SourceClipped: false, SourceClipRatio: 0f);
        var packed = SmoothToneGpu.FromStats(
            stats, DisplayTarget.Sdr203, fullOnHdrPanel: true);
        Assert.Equal(
            ImagingConstants.RefWhiteNits / ImagingConstants.ScrgbNits,
            packed.ToneCb[12],
            precision: 4);
    }
}
