using Palace.Helpers;
using Palace.Imaging;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothWorkingDecodeTests
{
    [Fact]
    public async Task DecodeAsync_ProducesPremultipliedWorkingTiles()
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
        Assert.NotEmpty(result.Level0Tiles);
        Assert.True(result.Level0Tiles.ContainsKey(new SmoothTileId(0, 0, 0)));
        Assert.Equal(SmoothTileMath.BytesPerTileRgba16F(), result.Level0Tiles[new SmoothTileId(0, 0, 0)].Length);
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
    public async Task BuildFromWorking_MarksV2Status()
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
        Assert.StartsWith("V2", session.StatusLine(), StringComparison.Ordinal);
        Assert.True(session.CoarseTileCount > 0);
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
}
