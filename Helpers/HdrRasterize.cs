namespace Palace.Helpers;

/// <summary>
/// scRGB float → half-float swapchain pixels. Pure CPU work, safe on any
/// thread. Rows run in parallel and honor the token between rows.
/// </summary>
internal static class HdrRasterize
{
    public static int HalfLength(int width, int height) => checked(width * height * 4);

    /// <summary>
    /// Fills the first <c>vw * vh * 4</c> halves of <paramref name="dest"/>
    /// (letterbox is zeroed, so a reused buffer needs no clear). RGB is
    /// scaled then clamped to <paramref name="clip"/>; alpha is passed
    /// through. <paramref name="scale"/> is 1 on HDR; SDR map-CLL uses
    /// <see cref="GalleryPresent.PresentMap"/>.
    /// </summary>
    public static void Fill(
        float[] src,
        int sw,
        int sh,
        ImageScaling scaling,
        int vw,
        int vh,
        float clip,
        ushort[] dest,
        CancellationToken cancellation,
        float scale = 1f)
    {
        if (sw <= 0 || sh <= 0 || vw <= 0 || vh <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vw));
        }

        if (src.Length < sw * sh * 4 || dest.Length < HalfLength(vw, vh))
        {
            throw new ArgumentException("Buffer too small.");
        }

        var (dx, dy, dw, dh) = GalleryPresent.DestRect(scaling, sw, sh, vw, vh);
        var options = new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        if (sw == vw && sh == vh && Math.Abs(dx) < 0.5f && Math.Abs(dy) < 0.5f
            && Math.Abs(dw - vw) < 0.5f && Math.Abs(dh - vh) < 0.5f)
        {
            Parallel.For(0, vh, options, y =>
            {
                var start = y * vw * 4;
                ConvertRun(src, dest, start, vw, clip, scale);
            });
            return;
        }

        var columns = new int[vw];
        for (var x = 0; x < vw; x++)
        {
            var u = (x + 0.5f - dx) / dw;
            columns[x] = u < 0 || u >= 1 ? -1 : Math.Clamp((int)(u * sw), 0, sw - 1);
        }

        Parallel.For(0, vh, options, y =>
        {
            var v = (y + 0.5f - dy) / dh;
            var rowStart = y * vw * 4;
            if (v < 0 || v >= 1)
            {
                Array.Clear(dest, rowStart, vw * 4);
                return;
            }

            var sy = Math.Clamp((int)(v * sh), 0, sh - 1);
            var srcRow = sy * sw;
            for (var x = 0; x < vw; x++)
            {
                var di = rowStart + (x * 4);
                var sx = columns[x];
                if (sx < 0)
                {
                    dest[di] = 0;
                    dest[di + 1] = 0;
                    dest[di + 2] = 0;
                    dest[di + 3] = 0;
                    continue;
                }

                var si = (srcRow + sx) * 4;
                dest[di] = GalleryPresent.FloatToHalf(Math.Clamp(src[si] * scale, 0, clip));
                dest[di + 1] = GalleryPresent.FloatToHalf(Math.Clamp(src[si + 1] * scale, 0, clip));
                dest[di + 2] = GalleryPresent.FloatToHalf(Math.Clamp(src[si + 2] * scale, 0, clip));
                dest[di + 3] = GalleryPresent.FloatToHalf(src[si + 3]);
            }
        });
    }

    private static void ConvertRun(float[] src, ushort[] dest, int start, int pixels, float clip, float scale)
    {
        var end = start + (pixels * 4);
        for (var i = start; i < end; i += 4)
        {
            dest[i] = GalleryPresent.FloatToHalf(Math.Clamp(src[i] * scale, 0, clip));
            dest[i + 1] = GalleryPresent.FloatToHalf(Math.Clamp(src[i + 1] * scale, 0, clip));
            dest[i + 2] = GalleryPresent.FloatToHalf(Math.Clamp(src[i + 2] * scale, 0, clip));
            dest[i + 3] = GalleryPresent.FloatToHalf(src[i + 3]);
        }
    }
}

/// <summary>
/// Single-owner half-float scratch. Reused across re-presents (resize / peak
/// / scale) so a 4K window does not allocate ~66 MB per step; trimmed when
/// the last request was much smaller.
/// </summary>
internal sealed class HdrHalfBuffer
{
    private ushort[]? _buffer;

    public int Capacity => _buffer?.Length ?? 0;

    public ushort[] Acquire(int length)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (_buffer is null || _buffer.Length < length || _buffer.Length / 2 > length)
        {
            _buffer = new ushort[length + (length / 4)];
        }

        return _buffer;
    }

    public void Release() => _buffer = null;
}
