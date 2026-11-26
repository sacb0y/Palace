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

    /// <summary>WIC / strip CopyPixels band height (rows). Peak RAM ≪ w×h×8.</summary>
    public const int DefaultStripRows = 256;

    /// <summary>How to open a source given its pixel count and strip capability.</summary>
    public enum WorkingRoute
    {
        /// <summary>Fits float Working budget — DecodeAsync / DecodePathAsync.</summary>
        WithinBudget = 0,
        /// <summary>Over budget and a strip/tile sampler is available — DecodeStripToRgba16FAsync.</summary>
        Strip = 1,
        /// <summary>Over budget and strip cannot handle this format — keep explicit error (no silent synthetic/downscale).</summary>
        UnsupportedOverBudget = 2
    }

    /// <summary>One WICRect-style horizontal strip for streamed decode.</summary>
    public readonly record struct StripRect(int X, int Y, int Width, int Height);

    /// <summary>
    /// Over-budget routing. Strip only when a sampler is available (synthetic SFW /
    /// strip-capable decode). Otherwise callers must show the existing error state.
    /// </summary>
    public static WorkingRoute ClassifyRoute(int width, int height, bool stripSamplerAvailable)
    {
        if (width < 1 || height < 1)
        {
            return WorkingRoute.UnsupportedOverBudget;
        }

        if (FitsSmoothWorkingBudget(width, height))
        {
            return WorkingRoute.WithinBudget;
        }

        return stripSamplerAvailable ? WorkingRoute.Strip : WorkingRoute.UnsupportedOverBudget;
    }

    /// <summary>
    /// File routing: WIC-friendly stills (PNG/JPEG/BMP/TIFF) can strip when over
    /// budget. HDR/container formats without a strip path stay explicit-error.
    /// </summary>
    public static bool IsStripCapablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Classify a file by pixel size + extension strip capability.</summary>
    public static WorkingRoute ClassifyFileRoute(string path, int width, int height) =>
        ClassifyRoute(width, height, IsStripCapablePath(path));

    /// <summary>
    /// Horizontal strip rectangles covering the image (WICRect iteration).
    /// Last strip may be shorter than <paramref name="stripRows"/>.
    /// </summary>
    public static IEnumerable<StripRect> EnumerateStripRects(
        int width,
        int height,
        int stripRows = DefaultStripRows)
    {
        if (width < 1 || height < 1 || stripRows < 1)
        {
            yield break;
        }

        for (var y = 0; y < height; y += stripRows)
        {
            var h = Math.Min(stripRows, height - y);
            yield return new StripRect(0, y, width, h);
        }
    }

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

    /// <summary>
    /// ≥64MP streamed path: <paramref name="fillStrip"/> copies one horizontal
    /// band (≤ <see cref="DefaultStripRows"/> rows) of float RGBA into
    /// <c>dest</c> (length ≥ width×rows×4). No full-image float buffer.
    /// </summary>
    public Task<SmoothWorkingDecodeResult?> DecodeStripToRgba16FAsync(
        int width,
        int height,
        Action<int, int, float[]> fillStrip,
        CancellationToken external = default)
    {
        ArgumentNullException.ThrowIfNull(fillStrip);
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
            () => DecodeStripCoreFilled(width, height, fillStrip, gen, token),
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
        // Bridge per-pixel sampler → strip filler (synthetic / tests).
        return DecodeStripCoreFilled(
            width,
            height,
            (y0, rows, dest) =>
            {
                for (var row = 0; row < rows; row++)
                {
                    var y = y0 + row;
                    var o = row * width * 4;
                    for (var x = 0; x < width; x++)
                    {
                        var (r, g, b, a) = sample(x, y);
                        var i = o + (x * 4);
                        dest[i] = r;
                        dest[i + 1] = g;
                        dest[i + 2] = b;
                        dest[i + 3] = a;
                    }
                }
            },
            gen,
            token);
    }

    private SmoothWorkingDecodeResult? DecodeStripCoreFilled(
        int width,
        int height,
        Action<int, int, float[]> fillStrip,
        int gen,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (width < 1 || height < 1)
        {
            return null;
        }

        // Prefer RGBA16F L0 tiles — never allocate width*height*4 floats.
        // Peak working buffer ≈ width × max(strip, tile band) × 16 bytes.
        var (nx, ny) = SmoothTileMath.TileCounts(width, height);
        var tiles = new Dictionary<SmoothTileId, byte[]>(nx * ny);
        var stripRows = DefaultStripRows;
        var stripBuf = new float[width * stripRows * 4];
        var bandRows = SmoothTileMath.ContentSize + (2 * SmoothTileMath.Border);
        var band = new float[width * bandRows * 4];

        for (var ty = 0; ty < ny; ty++)
        {
            token.ThrowIfCancellationRequested();
            // Y extent from tile row ty (tx=0); X gutters are sampled per-tile below.
            var (_, oy, _, ch) = SmoothTileMath.ContentRect(0, ty, width, height);
            var y0 = Math.Max(0, oy - SmoothTileMath.Border);
            var y1 = Math.Min(height, oy + Math.Max(1, ch) + SmoothTileMath.Border);
            var needH = y1 - y0;
            if (needH > bandRows)
            {
                bandRows = needH;
                band = new float[width * bandRows * 4];
            }

            // Fill band via WICRect-sized strips — no full-image float.
            for (var y = y0; y < y1; y += stripRows)
            {
                token.ThrowIfCancellationRequested();
                var rows = Math.Min(stripRows, y1 - y);
                fillStrip(y, rows, stripBuf);
                var destOff = (y - y0) * width * 4;
                var count = rows * width * 4;
                Array.Copy(stripBuf, 0, band, destOff, count);
            }

            for (var tx = 0; tx < nx; tx++)
            {
                var id = new SmoothTileId(0, tx, ty);
                tiles[id] = TileFromBand(band, y0, width, height, tx, ty);
            }
        }

        lock (_gate)
        {
            if (gen != _generation)
            {
                throw new OperationCanceledException(token);
            }
        }

        // Coarse subsample for Analyze-compatible ImageStats (Lc ≈ paper).
        // Re-sample via thin strips so we never hold the full float frame.
        var step = Math.Max(1, (int)Math.Sqrt((width * (long)height) / 4096.0));
        float maxRgb = 0, sumY = 0;
        var n = 0;
        for (var y = 0; y < height; y += step)
        {
            token.ThrowIfCancellationRequested();
            fillStrip(y, 1, stripBuf);
            for (var x = 0; x < width; x += step)
            {
                var i = x * 4;
                var r = stripBuf[i];
                var g = stripBuf[i + 1];
                var b = stripBuf[i + 2];
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

    private static byte[] TileFromBand(
        float[] band,
        int bandOriginY,
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
            var bandRow = iy - bandOriginY;
            for (var sx = 0; sx < slot; sx++)
            {
                var cx = sx - border;
                var ix = Math.Clamp(ox + cx, 0, width - 1);
                var si = ((bandRow * width) + ix) * 4;
                var o = ((sy * slot) + sx) * 8;
                WriteHalf(bytes, o, band[si]);
                WriteHalf(bytes, o + 2, band[si + 1]);
                WriteHalf(bytes, o + 4, band[si + 2]);
                WriteHalf(bytes, o + 6, band[si + 3]);
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
