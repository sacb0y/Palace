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
    /// Deprecated: do not box-downscale over 64MP — use strip/tile RGBA16F L0
    /// (<see cref="DecodeStripToRgba16FAsync"/>). Kept for size math helpers.
    /// </summary>
    public static (int Width, int Height) FitSmoothWorkingSize(int width, int height) =>
        (Math.Max(0, width), Math.Max(0, height));

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

    /// <summary>
    /// ≥64MP path: no full float Working and no box-downscale. Builds RGBA16F
    /// L0 tiles in content-row strips from a pixel sampler (synthetic / strip
    /// decode). Stats from a coarse subsample.
    /// </summary>
    public Task<SmoothWorkingDecodeResult?> DecodeStripToRgba16FAsync(
        int width,
        int height,
        Func<int, int, (float R, float G, float B, float A)> sample,
        CancellationToken external = default)
    {
        ArgumentNullException.ThrowIfNull(sample);
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
            () => DecodeStripCore(width, height, sample, gen, token),
            token);
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

        // Over budget: refuse full-float path — callers use DecodeStripToRgba16FAsync.
        if (!FitsSmoothWorkingBudget(width, height))
        {
            return null;
        }

        // Smooth path allows up to 64MP — bypass Imaging's 16MP eager TryRun cap.
        NormalizedFrame? frame;
        try
        {
            frame = ImagingStillNormalize.Run(sourceRgba, width, height, options);
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

        var stats = Analyze.Run(frame);
        var working = frame.Rgba;
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

    private SmoothWorkingDecodeResult? DecodeStripCore(
        int width,
        int height,
        Func<int, int, (float R, float G, float B, float A)> sample,
        int gen,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (width < 1 || height < 1)
        {
            return null;
        }

        // Prefer RGBA16F L0 tiles — never allocate width*height*4 floats.
        var (nx, ny) = SmoothTileMath.TileCounts(width, height);
        var tiles = new Dictionary<SmoothTileId, byte[]>(nx * ny);
        var stripH = SmoothTileMath.ContentSize;
        for (var ty = 0; ty < ny; ty++)
        {
            token.ThrowIfCancellationRequested();
            for (var tx = 0; tx < nx; tx++)
            {
                var id = new SmoothTileId(0, tx, ty);
                tiles[id] = TileFromSampler(sample, width, height, tx, ty);
            }

            _ = stripH;
        }

        lock (_gate)
        {
            if (gen != _generation)
            {
                throw new OperationCanceledException(token);
            }
        }

        // Coarse subsample for Analyze-compatible ImageStats (Lc ≈ paper).
        var step = Math.Max(1, (int)Math.Sqrt((width * (long)height) / 4096.0));
        float maxRgb = 0, sumY = 0;
        var n = 0;
        for (var y = 0; y < height; y += step)
        {
            for (var x = 0; x < width; x += step)
            {
                var (r, g, b, _) = sample(x, y);
                maxRgb = Math.Max(maxRgb, Math.Max(r, Math.Max(g, b)));
                sumY += ImagingConstants.Bt2020Y(r, g, b);
                n++;
            }
        }

        var p50 = n > 0 ? sumY / n : 0.5f;
        var lc = Math.Max(1f, maxRgb);
        var stats = new ImageStats(
            P50: p50,
            P99995: maxRgb,
            Max: maxRgb,
            MaxRgb: maxRgb,
            Lc: lc,
            RefWhite: ImagingConstants.RefWhiteNits,
            MetadataPeak: null,
            SourceClipped: false,
            SourceClipRatio: 0f);

        // No NormalizedFrame / WorkingRgba — session uses Level0Tiles + rematerialize.
        return new SmoothWorkingDecodeResult(
            frame: null,
            stats,
            workingRgba: Array.Empty<float>(),
            level0Tiles: tiles,
            width,
            height,
            gen);
    }

    private static byte[] TileFromSampler(
        Func<int, int, (float R, float G, float B, float A)> sample,
        int width,
        int height,
        int tileX,
        int tileY)
    {
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
                var (r, g, b, a) = sample(ix, iy);
                var o = ((sy * slot) + sx) * 8;
                WriteHalf(bytes, o, r);
                WriteHalf(bytes, o + 2, g);
                WriteHalf(bytes, o + 4, b);
                WriteHalf(bytes, o + 6, a);
            }
        }

        return bytes;
    }

    private static void WriteHalf(byte[] dest, int offset, float value)
    {
        var h = SmoothTileSession.Float32ToFloat16Bits(value);
        dest[offset] = (byte)(h & 0xff);
        dest[offset + 1] = (byte)(h >> 8);
    }
}

/// <summary>One successful V2 Working decode. Session retains Working floats and/or RGBA16F L0.</summary>
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
        Level0Tiles = new Dictionary<SmoothTileId, byte[]>();
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

    /// <summary>Strip/tile path (≥64MP): RGBA16F L0 only, no float Working.</summary>
    public SmoothWorkingDecodeResult(
        NormalizedFrame? frame,
        ImageStats stats,
        float[] workingRgba,
        IReadOnlyDictionary<SmoothTileId, byte[]> level0Tiles,
        int width,
        int height,
        int generation)
    {
        _frame = frame;
        Stats = stats;
        WorkingRgba = workingRgba;
        Level0Tiles = level0Tiles;
        Width = width;
        Height = height;
        Generation = generation;
    }

    public NormalizedFrame? Frame => _frame;
    public ImageStats Stats { get; }
    public float[] WorkingRgba { get; }
    public IReadOnlyDictionary<SmoothTileId, byte[]> Level0Tiles { get; }
    public int Generation { get; }
    public int Width { get; }
    public int Height { get; }
    public bool HasFloatWorking => WorkingRgba.Length >= Width * Height * 4;

    /// <summary>
    /// Drop the <see cref="NormalizedFrame"/> wrapper after the session has
    /// retained <see cref="WorkingRgba"/> (should-fix: free after extract).
    /// </summary>
    public void ClearNormalizedFrame() => _frame = null;
}
