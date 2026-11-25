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
