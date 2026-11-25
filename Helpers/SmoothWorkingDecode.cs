using Palace.Imaging;

namespace Palace.Helpers;

/// <summary>
/// V2: Palace.Imaging Working-space decode (BT.2020 linear, 1.0 = 203 nits,
/// premultiplied) on a background Task. Cancels the prior job when the image
/// changes. EXIF is never baked into tiles (<see cref="SmoothTileDecodeGate"/>).
/// Smooth-path budget is <see cref="MaxSmoothWorkingPixels"/> (≥64MP); gutters
/// sample neighbours via Clamp(ox+cx, 0, w-1).
/// </summary>
public sealed class SmoothWorkingDecode : IDisposable
{
    /// <summary>Smooth-path Working budget (Geo ≥64MP). Distinct from Imaging's 16MP eager cap.</summary>
    public const long MaxSmoothWorkingPixels = 64L * 1000L * 1000L;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private int _generation;

    public int Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    public static bool FitsSmoothWorkingBudget(int width, int height) =>
        width > 0 && height > 0 && (long)width * height <= MaxSmoothWorkingPixels;

    /// <summary>
    /// Box-fit so width×height ≤ <see cref="MaxSmoothWorkingPixels"/> before
    /// normalize (provisional strip/tile path for ≥64MP sources).
    /// </summary>
    public static (int Width, int Height) FitSmoothWorkingSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        var pixels = (long)width * height;
        if (pixels <= MaxSmoothWorkingPixels)
        {
            return (width, height);
        }

        var scale = Math.Sqrt(MaxSmoothWorkingPixels / (double)pixels);
        var w = Math.Max(1, (int)Math.Floor(width * scale));
        var h = Math.Max(1, (int)Math.Floor(height * scale));
        while ((long)w * h > MaxSmoothWorkingPixels)
        {
            if (w >= h && w > 1)
            {
                w--;
            }
            else if (h > 1)
            {
                h--;
            }
            else
            {
                break;
            }
        }

        return (w, h);
    }

    /// <summary>Cancel any in-flight decode (image change / dispose).</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }

            _cts?.Dispose();
            _cts = null;
            _generation++;
        }
    }

    public void Dispose() => Cancel();

    /// <summary>
    /// Start a Working normalize + L0 tile extract. Cancels any prior job.
    /// Returns null when the frame is over the smooth budget or normalize fails.
    /// </summary>
    public Task<SmoothWorkingDecodeResult?> DecodeAsync(
        float[] sourceRgba,
        int width,
        int height,
        NormalizeOptions options,
        CancellationToken external = default)
    {
        CancellationTokenSource linked;
        int gen;
        lock (_gate)
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }

            _cts?.Dispose();
            _generation++;
            gen = _generation;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
            linked = _cts;
        }

        var token = linked.Token;
        return Task.Run(
            () => DecodeCore(sourceRgba, width, height, options, gen, token),
            token);
    }

    /// <summary>
    /// Path-gated decode entry. Refuses online-only / missing paths. The actual
    /// file→float decode is supplied by <paramref name="loadSource"/> so WinUI
    /// presenters stay out of this helper (Linux tests inject buffers).
    /// Prefer <see cref="SmoothSourceLoad.LoadLocalFile"/> as the loader.
    /// </summary>
    public Task<SmoothWorkingDecodeResult?> DecodePathAsync(
        string path,
        Func<CancellationToken, Task<(float[] Rgba, int W, int H, NormalizeOptions Opts)?>> loadSource,
        CancellationToken external = default)
    {
        if (!SmoothTileDecodeGate.MayDecodePath(path))
        {
            throw new InvalidOperationException("Decode refused by SmoothTileDecodeGate.");
        }

        CancellationTokenSource linked;
        int gen;
        lock (_gate)
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }

            _cts?.Dispose();
            _generation++;
            gen = _generation;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
            linked = _cts;
        }

        var token = linked.Token;
        return Task.Run(
            async () =>
            {
                token.ThrowIfCancellationRequested();
                var loaded = await loadSource(token).ConfigureAwait(false);
                if (loaded is null)
                {
                    return null;
                }

                var (rgba, w, h, opts) = loaded.Value;
                return DecodeCore(rgba, w, h, opts, gen, token);
            },
            token);
    }

    /// <summary>
    /// Extract one 512² RGBA16F Working tile (Border=2). Gutters sample
    /// neighbours via <c>Clamp(ox+cx, 0, w-1)</c> — replicate only at the
    /// image border, never from empty tile space.
    /// </summary>
    public static byte[] TileFromWorkingLevel0(
        ReadOnlySpan<float> rgba,
        int width,
        int height,
        int tileX,
        int tileY)
    {
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var (ox, oy, _, _) = SmoothTileMath.ContentRect(tileX, tileY, width, height);
        var slot = SmoothTileMath.SlotSize;
        var border = SmoothTileMath.Border;
        var bytes = new byte[slot * slot * 8];

        for (var sy = 0; sy < slot; sy++)
        {
            var cy = sy - border;
            var iy = Math.Clamp(oy + cy, 0, height - 1);
            for (var sx = 0; sx < slot; sx++)
            {
                var cx = sx - border;
                var ix = Math.Clamp(ox + cx, 0, width - 1);
                var si = ((iy * width) + ix) * 4;
                var o = ((sy * slot) + sx) * 8;
                WriteHalf(bytes, o, rgba[si]);
                WriteHalf(bytes, o + 2, rgba[si + 1]);
                WriteHalf(bytes, o + 4, rgba[si + 2]);
                WriteHalf(bytes, o + 6, rgba[si + 3]);
            }
        }

        return bytes;
    }

    /// <summary>
    /// Materialize a tile at any pyramid level from retained L0 Working floats
    /// (box average of the 2^level footprint). Gutters use the same Clamp rule
    /// in level space, then map into L0.
    /// </summary>
    public static byte[] TileFromWorkingAtLevel(
        ReadOnlySpan<float> level0Rgba,
        int fullWidth,
        int fullHeight,
        int level,
        int tileX,
        int tileY)
    {
        if (level <= 0)
        {
            return TileFromWorkingLevel0(level0Rgba, fullWidth, fullHeight, tileX, tileY);
        }

        var lw = SmoothTileMath.LevelWidth(fullWidth, level);
        var lh = SmoothTileMath.LevelHeight(fullHeight, level);
        var (ox, oy, _, _) = SmoothTileMath.ContentRect(tileX, tileY, lw, lh);
        var scale = 1 << level;
        var slot = SmoothTileMath.SlotSize;
        var border = SmoothTileMath.Border;
        var bytes = new byte[slot * slot * 8];

        for (var sy = 0; sy < slot; sy++)
        {
            var cy = sy - border;
            var ly = Math.Clamp(oy + cy, 0, lh - 1);
            for (var sx = 0; sx < slot; sx++)
            {
                var cx = sx - border;
                var lx = Math.Clamp(ox + cx, 0, lw - 1);
                SampleBoxAverage(
                    level0Rgba,
                    fullWidth,
                    fullHeight,
                    lx * scale,
                    ly * scale,
                    scale,
                    out var r,
                    out var g,
                    out var b,
                    out var a);
                var o = ((sy * slot) + sx) * 8;
                WriteHalf(bytes, o, r);
                WriteHalf(bytes, o + 2, g);
                WriteHalf(bytes, o + 4, b);
                WriteHalf(bytes, o + 6, a);
            }
        }

        return bytes;
    }

    private static void SampleBoxAverage(
        ReadOnlySpan<float> rgba,
        int w,
        int h,
        int x0,
        int y0,
        int extent,
        out float r,
        out float g,
        out float b,
        out float a)
    {
        double sr = 0, sg = 0, sb = 0, sa = 0;
        var n = 0;
        var x1 = Math.Min(w, x0 + Math.Max(1, extent));
        var y1 = Math.Min(h, y0 + Math.Max(1, extent));
        var xs = Math.Clamp(x0, 0, w - 1);
        var ys = Math.Clamp(y0, 0, h - 1);
        if (x1 <= xs)
        {
            x1 = xs + 1;
        }

        if (y1 <= ys)
        {
            y1 = ys + 1;
        }

        for (var y = ys; y < y1; y++)
        {
            for (var x = xs; x < x1; x++)
            {
                var si = ((y * w) + x) * 4;
                sr += rgba[si];
                sg += rgba[si + 1];
                sb += rgba[si + 2];
                sa += rgba[si + 3];
                n++;
            }
        }

        if (n < 1)
        {
            r = g = b = 0f;
            a = 1f;
            return;
        }

        var inv = 1.0 / n;
        r = (float)(sr * inv);
        g = (float)(sg * inv);
        b = (float)(sb * inv);
        a = (float)(sa * inv);
    }

    private SmoothWorkingDecodeResult? DecodeCore(
        float[] sourceRgba,
        int width,
        int height,
        NormalizeOptions options,
        int gen,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (width < 1 || height < 1 || sourceRgba.Length < width * height * 4)
        {
            return null;
        }

        var src = sourceRgba;
        var w = width;
        var h = height;
        if (!FitsSmoothWorkingBudget(w, h))
        {
            var (fw, fh) = FitSmoothWorkingSize(w, h);
            src = BoxResize(src, w, h, fw, fh);
            w = fw;
            h = fh;
        }

        // Smooth path allows up to 64MP — bypass Imaging's 16MP eager TryRun cap.
        NormalizedFrame? frame;
        try
        {
            frame = ImagingStillNormalize.Run(src, w, h, options);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        token.ThrowIfCancellationRequested();
        if (frame is null)
        {
            return null;
        }

        lock (_gate)
        {
            if (gen != _generation)
            {
                throw new OperationCanceledException(token);
            }
        }

        // Keep Working floats for on-demand finer tiles; do not eagerly allocate
        // every L0 RGBA16F slot for large images (session materializes via
        // TileFromWorkingLevel0 / TileFromWorkingAtLevel).
        var stats = Analyze.Run(frame);
        var working = frame.Rgba;
        // Detach NormalizedFrame payload ownership into the result — callers
        // may ClearNormalizedFrame after the session retains WorkingRgba.
        var result = new SmoothWorkingDecodeResult(frame, stats, working, gen);

        lock (_gate)
        {
            if (gen != _generation)
            {
                throw new OperationCanceledException(token);
            }
        }

        return result;
    }

    private static float[] BoxResize(float[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new float[dw * dh * 4];
        for (var y = 0; y < dh; y++)
        {
            var sy0 = y * sh / dh;
            var sy1 = Math.Max(sy0 + 1, (y + 1) * sh / dh);
            sy1 = Math.Min(sy1, sh);
            for (var x = 0; x < dw; x++)
            {
                var sx0 = x * sw / dw;
                var sx1 = Math.Max(sx0 + 1, (x + 1) * sw / dw);
                sx1 = Math.Min(sx1, sw);
                double r = 0, g = 0, b = 0, a = 0;
                var n = 0;
                for (var sy = sy0; sy < sy1; sy++)
                {
                    for (var sx = sx0; sx < sx1; sx++)
                    {
                        var si = ((sy * sw) + sx) * 4;
                        r += src[si];
                        g += src[si + 1];
                        b += src[si + 2];
                        a += src[si + 3];
                        n++;
                    }
                }

                var di = ((y * dw) + x) * 4;
                if (n < 1)
                {
                    continue;
                }

                var inv = 1.0 / n;
                dst[di] = (float)(r * inv);
                dst[di + 1] = (float)(g * inv);
                dst[di + 2] = (float)(b * inv);
                dst[di + 3] = (float)(a * inv);
            }
        }

        return dst;
    }

    private static void WriteHalf(byte[] dest, int offset, float value)
    {
        var h = SmoothTileSession.Float32ToFloat16Bits(value);
        dest[offset] = (byte)(h & 0xff);
        dest[offset + 1] = (byte)(h >> 8);
    }
}

/// <summary>One successful V2 Working decode. Session retains <see cref="WorkingRgba"/> for streaming.</summary>
public sealed class SmoothWorkingDecodeResult
{
    private NormalizedFrame? _frame;

    public SmoothWorkingDecodeResult(
        NormalizedFrame frame,
        ImageStats stats,
        float[] workingRgba,
        int generation)
    {
        _frame = frame;
        Stats = stats;
        WorkingRgba = workingRgba;
        Generation = generation;
        Width = frame.Width;
        Height = frame.Height;
    }

    /// <summary>Legacy ctor used by older call sites that still pass L0 tile maps.</summary>
    public SmoothWorkingDecodeResult(
        NormalizedFrame frame,
        ImageStats stats,
        IReadOnlyDictionary<SmoothTileId, byte[]> level0Tiles,
        int generation)
        : this(frame, stats, frame.Rgba, generation)
    {
        Level0Tiles = level0Tiles;
    }

    public NormalizedFrame? Frame => _frame;
    public ImageStats Stats { get; }
    public float[] WorkingRgba { get; }
    public IReadOnlyDictionary<SmoothTileId, byte[]> Level0Tiles { get; } =
        new Dictionary<SmoothTileId, byte[]>();
    public int Generation { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Drop the <see cref="NormalizedFrame"/> wrapper after the session has
    /// retained <see cref="WorkingRgba"/> (should-fix: free after extract).
    /// </summary>
    public void ClearNormalizedFrame() => _frame = null;
}
