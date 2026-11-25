namespace Palace.Helpers;

/// <summary>
/// CPU reference for Geo <c>SmoothMipDownsampleCS</c> box filter (Params.z=0,
/// Border=2, StraightAlpha=0, AntiRing unused for box). Used for GPU-vs-CPU
/// evidence on edge tiles and odd sizes.
/// </summary>
public static class SmoothMipDownsampleCpu
{
    /// <summary>
    /// Downsamples a full level-L Working RGBA32F image to level L+1
    /// (ceil/2) with the same 2×2 box + edge clamp as the HLSL path.
    /// </summary>
    public static float[] BoxDownsampleFull(
        ReadOnlySpan<float> srcRgba,
        int srcW,
        int srcH,
        out int dstW,
        out int dstH)
    {
        if (srcW < 1 || srcH < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(srcW));
        }

        if (srcRgba.Length < srcW * srcH * 4)
        {
            throw new ArgumentException("Source buffer too small.", nameof(srcRgba));
        }

        dstW = (srcW + 1) / 2;
        dstH = (srcH + 1) / 2;
        var dst = new float[dstW * dstH * 4];
        for (var y = 0; y < dstH; y++)
        {
            for (var x = 0; x < dstW; x++)
            {
                var s0x = Math.Min(x * 2, srcW - 1);
                var s0y = Math.Min(y * 2, srcH - 1);
                var s1x = Math.Min(s0x + 1, srcW - 1);
                var s1y = Math.Min(s0y + 1, srcH - 1);
                AccumulateBox(srcRgba, srcW, s0x, s0y, s1x, s1y, dst, (y * dstW + x) * 4);
            }
        }

        return dst;
    }

    /// <summary>
    /// Writes one 512² destination slot (float32 Working, will convert to F16
    /// on upload) including Border=2 gutter, matching the CS slot mapping.
    /// </summary>
    public static float[] BoxDownsampleTileSlot(
        ReadOnlySpan<float> srcRgba,
        int srcW,
        int srcH,
        int parentContentOriginX,
        int parentContentOriginY)
    {
        var slot = SmoothTileMath.SlotSize;
        var border = SmoothTileMath.Border;
        var dst = new float[slot * slot * 4];
        var originX = parentContentOriginX * 2; // level-L px of first child content
        var originY = parentContentOriginY * 2;

        for (var sy = 0; sy < slot; sy++)
        {
            for (var sx = 0; sx < slot; sx++)
            {
                var dX = sx - border;
                var dY = sy - border;
                var s0x = originX + 2 * dX;
                var s0y = originY + 2 * dY;
                var a = FetchClamped(srcRgba, srcW, srcH, s0x, s0y);
                var b = FetchClamped(srcRgba, srcW, srcH, s0x + 1, s0y);
                var c = FetchClamped(srcRgba, srcW, srcH, s0x, s0y + 1);
                var e = FetchClamped(srcRgba, srcW, srcH, s0x + 1, s0y + 1);
                var o = (sy * slot + sx) * 4;
                dst[o] = (a.R + b.R + c.R + e.R) * 0.25f;
                dst[o + 1] = (a.G + b.G + c.G + e.G) * 0.25f;
                dst[o + 2] = (a.B + b.B + c.B + e.B) * 0.25f;
                dst[o + 3] = (a.A + b.A + c.A + e.A) * 0.25f;
            }
        }

        return dst;
    }

    private static void AccumulateBox(
        ReadOnlySpan<float> src,
        int srcW,
        int x0,
        int y0,
        int x1,
        int y1,
        float[] dst,
        int dstOffset)
    {
        var a = Fetch(src, srcW, x0, y0);
        var b = Fetch(src, srcW, x1, y0);
        var c = Fetch(src, srcW, x0, y1);
        var e = Fetch(src, srcW, x1, y1);
        dst[dstOffset] = (a.R + b.R + c.R + e.R) * 0.25f;
        dst[dstOffset + 1] = (a.G + b.G + c.G + e.G) * 0.25f;
        dst[dstOffset + 2] = (a.B + b.B + c.B + e.B) * 0.25f;
        dst[dstOffset + 3] = (a.A + b.A + c.A + e.A) * 0.25f;
    }

    private static (float R, float G, float B, float A) FetchClamped(
        ReadOnlySpan<float> src,
        int w,
        int h,
        int x,
        int y)
    {
        x = Math.Clamp(x, 0, w - 1);
        y = Math.Clamp(y, 0, h - 1);
        return Fetch(src, w, x, y);
    }

    private static (float R, float G, float B, float A) Fetch(ReadOnlySpan<float> src, int w, int x, int y)
    {
        var i = (y * w + x) * 4;
        return (src[i], src[i + 1], src[i + 2], src[i + 3]);
    }
}
