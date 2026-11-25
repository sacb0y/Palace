using Palace.Imaging;

namespace Palace.Helpers;

/// <summary>
/// V2: Palace.Imaging Working-space decode (BT.2020 linear, 1.0 = 203 nits,
/// premultiplied) on a background Task. Cancels the prior job when the image
/// changes. EXIF is never baked into tiles (<see cref="SmoothTileDecodeGate"/>).
/// </summary>
public sealed class SmoothWorkingDecode : IDisposable
{
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
    /// Returns null when the frame is over budget or normalize fails.
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
    /// Extract one 512² RGBA16F Working tile (Border=2 gutter, edge replicate)
    /// from a level-0 Working float buffer. Premultiply if source alpha is straight
    /// is not done here — Imaging Working is already premultiplied.
    /// </summary>
    public static byte[] TileFromWorkingLevel0(
        ReadOnlySpan<float> rgba,
        int width,
        int height,
        int tileX,
        int tileY)
    {
        var (ox, oy, cw, ch) = SmoothTileMath.ContentRect(tileX, tileY, width, height);
        var slot = SmoothTileMath.SlotSize;
        var border = SmoothTileMath.Border;
        var bytes = new byte[slot * slot * 8];

        for (var sy = 0; sy < slot; sy++)
        {
            for (var sx = 0; sx < slot; sx++)
            {
                var cx = sx - border;
                var cy = sy - border;
                var ix = ox + Math.Clamp(cx, 0, Math.Max(0, cw - 1));
                var iy = oy + Math.Clamp(cy, 0, Math.Max(0, ch - 1));
                if (cw < 1 || ch < 1)
                {
                    ix = Math.Clamp(ox, 0, width - 1);
                    iy = Math.Clamp(oy, 0, height - 1);
                }
                else
                {
                    ix = Math.Clamp(ix, 0, width - 1);
                    iy = Math.Clamp(iy, 0, height - 1);
                }

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

        if (!ImagingStillNormalize.FitsWorkingBudget(width, height))
        {
            return null;
        }

        var frame = ImagingStillNormalize.TryRun(sourceRgba, width, height, options);
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

        var (nx, ny) = SmoothTileMath.TileCounts(frame.Width, frame.Height);
        var tiles = new Dictionary<SmoothTileId, byte[]>(nx * ny);
        for (var ty = 0; ty < ny; ty++)
        {
            for (var tx = 0; tx < nx; tx++)
            {
                token.ThrowIfCancellationRequested();
                var id = new SmoothTileId(0, tx, ty);
                tiles[id] = TileFromWorkingLevel0(frame.Rgba, frame.Width, frame.Height, tx, ty);
            }
        }

        lock (_gate)
        {
            if (gen != _generation)
            {
                throw new OperationCanceledException(token);
            }
        }

        var stats = Analyze.Run(frame);
        return new SmoothWorkingDecodeResult(frame, stats, tiles, gen);
    }

    private static void WriteHalf(byte[] dest, int offset, float value)
    {
        var h = SmoothTileSession.Float32ToFloat16Bits(value);
        dest[offset] = (byte)(h & 0xff);
        dest[offset + 1] = (byte)(h >> 8);
    }
}

/// <summary>One successful V2 Working decode + L0 tile map.</summary>
public sealed class SmoothWorkingDecodeResult
{
    public SmoothWorkingDecodeResult(
        NormalizedFrame frame,
        ImageStats stats,
        IReadOnlyDictionary<SmoothTileId, byte[]> level0Tiles,
        int generation)
    {
        Frame = frame;
        Stats = stats;
        Level0Tiles = level0Tiles;
        Generation = generation;
    }

    public NormalizedFrame Frame { get; }
    public ImageStats Stats { get; }
    public IReadOnlyDictionary<SmoothTileId, byte[]> Level0Tiles { get; }
    public int Generation { get; }
    public int Width => Frame.Width;
    public int Height => Frame.Height;
}
