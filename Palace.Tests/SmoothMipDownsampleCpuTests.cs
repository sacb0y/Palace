using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothMipDownsampleCpuTests
{
    [Fact]
    public void BoxDownsample_EvenSize_Averages2x2()
    {
        // 2x2 solid colors → 1 pixel average
        var src = new float[]
        {
            1, 0, 0, 1,  0, 1, 0, 1,
            0, 0, 1, 1,  1, 1, 0, 1,
        };
        var dst = SmoothMipDownsampleCpu.BoxDownsampleFull(src, 2, 2, out var dw, out var dh);
        Assert.Equal(1, dw);
        Assert.Equal(1, dh);
        Assert.Equal(0.5f, dst[0], 5);
        Assert.Equal(0.5f, dst[1], 5);
        Assert.Equal(0.25f, dst[2], 5);
        Assert.Equal(1f, dst[3], 5);
    }

    [Fact]
    public void BoxDownsample_OddSize_ReusesEdge()
    {
        // 3x1 → ceil = 2 wide; last column reuses edge texel
        var src = new float[]
        {
            2, 0, 0, 1,
            4, 0, 0, 1,
            6, 0, 0, 1,
        };
        var dst = SmoothMipDownsampleCpu.BoxDownsampleFull(src, 3, 1, out var dw, out var dh);
        Assert.Equal(2, dw);
        Assert.Equal(1, dh);
        // x0: avg(2,4,2,4) wait — 2x2 footprint with y clamped: (2,4) and (2,4) → avg of cols 0,1
        Assert.Equal(3f, dst[0], 5); // (2+4+2+4)/4
        // x1: s0x=min(2,2)=2, s1x=2 → all 6
        Assert.Equal(6f, dst[4], 5);
    }

    [Fact]
    public void TileSlot_IncludesBorderMapping()
    {
        const int w = 20;
        const int h = 20;
        var src = new float[w * h * 4];
        for (var i = 0; i < src.Length; i += 4)
        {
            src[i] = 1f;
            src[i + 3] = 1f;
        }

        var slot = SmoothMipDownsampleCpu.BoxDownsampleTileSlot(src, w, h, parentContentOriginX: 0, parentContentOriginY: 0);
        Assert.Equal(512 * 512 * 4, slot.Length);
        // Content (0,0) → slot (2,2)
        var o = (2 * 512 + 2) * 4;
        Assert.Equal(1f, slot[o], 5);
        Assert.Equal(1f, slot[o + 3], 5);
    }

    [Fact]
    public void DecodeGate_RefusesMissingAndOnlineOnlyPolicy()
    {
        Assert.False(SmoothTileDecodeGate.MayDecodePath(null));
        Assert.False(SmoothTileDecodeGate.MayDecodePath(""));
        Assert.False(SmoothTileDecodeGate.MayDecodePath("/no/such/palace-v1-tile-file.bin"));
        Assert.False(SmoothTileDecodeGate.BakeExifRotationIntoTiles);
    }

    [Fact]
    public void GpuVsCpu_BoxDownsampleTileSlot_MatchesFullCrop()
    {
        // Reference: full-image box then crop content vs TileSlot path (CPU stand-in
        // for SmoothMipDownsampleCS; Fenrir compares the same against real GPU).
        const int w = 32;
        const int h = 32;
        var src = new float[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                src[i] = x * 0.01f;
                src[i + 1] = y * 0.01f;
                src[i + 2] = 0.5f;
                src[i + 3] = 1f;
            }
        }

        var full = SmoothMipDownsampleCpu.BoxDownsampleFull(src, w, h, out var dw, out var dh);
        var slot = SmoothMipDownsampleCpu.BoxDownsampleTileSlot(src, w, h, parentContentOriginX: 0, parentContentOriginY: 0);
        Assert.Equal((w + 1) / 2, dw);
        Assert.Equal((h + 1) / 2, dh);

        // Content (0,0) at L+1 lives at slot (Border,Border)
        var so = (2 * 512 + 2) * 4;
        Assert.Equal(full[0], slot[so], 5);
        Assert.Equal(full[1], slot[so + 1], 5);
        Assert.Equal(full[2], slot[so + 2], 5);
        Assert.Equal(full[3], slot[so + 3], 5);
    }
}
