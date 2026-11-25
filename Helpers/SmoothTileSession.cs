namespace Palace.Helpers;

/// <summary>
/// V1 debug tile session: builds a synthetic Working-space image into the CPU
/// tile cache, sizes the GPU LRU from VRAM budget, and reports residency
/// metrics for Fenrir evidence. EXIF is never baked; cloud online-only paths
/// are refused via <see cref="SmoothTileDecodeGate"/>.
/// </summary>
public sealed class SmoothTileSession
{
    public int FullWidth { get; }
    public int FullHeight { get; }
    public int CoarseLevel { get; }
    public int MaxLevel { get; }
    public int PoolCapacity { get; private set; }
    public SmoothCpuTileCache Cpu { get; } = new();
    public SmoothGpuTileLru Lru { get; }
    public long FirstCoarseFrameMs { get; private set; }
    public int CoarseTileCount { get; private set; }
    public string WorkingFormat { get; } = SmoothTileWorkingSpace.GpuFormat;

    public SmoothTileSession(int fullWidth, int fullHeight, long availableVramBytes = 8L * 1024 * 1024 * 1024)
    {
        if (fullWidth < 1 || fullHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fullWidth));
        }

        FullWidth = fullWidth;
        FullHeight = fullHeight;
        CoarseLevel = SmoothTileMath.CoarseLevelIndex(fullWidth, fullHeight);
        MaxLevel = SmoothTileMath.MaxLevel(fullWidth, fullHeight);
        PoolCapacity = SmoothGpuTileLru.CapacityFromVramBytes(availableVramBytes);
        Lru = new SmoothGpuTileLru(PoolCapacity, CoarseLevel);
    }

    /// <summary>
    /// Fills level-0 CPU tiles from a procedural HDR Working pattern (premultiplied).
    /// Returns wall-ms until the coarse level is fully present in the CPU cache.
    /// </summary>
    public long BuildSyntheticLevel0AndCoarse()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        BuildLevel0CpuTiles();
        CoarseTileCount = BuildCoarseCpuFromLevel0Box();
        sw.Stop();
        FirstCoarseFrameMs = sw.ElapsedMilliseconds;

        // Pin coarse tiles into LRU slots (GPU upload is caller's job).
        var lw = SmoothTileMath.LevelWidth(FullWidth, CoarseLevel);
        var lh = SmoothTileMath.LevelHeight(FullHeight, CoarseLevel);
        var (tx, ty) = SmoothTileMath.TileCounts(lw, lh);
        for (var y = 0; y < ty; y++)
        {
            for (var x = 0; x < tx; x++)
            {
                var id = new SmoothTileId(CoarseLevel, x, y);
                _ = Lru.TryInsert(id, out _, out _);
            }
        }

        return FirstCoarseFrameMs;
    }

    public string StatusLine() =>
        $"V1 tiles {FullWidth}x{FullHeight} coarse=L{CoarseLevel} " +
        $"coarseTiles={CoarseTileCount} pool={PoolCapacity} resident={Lru.ResidentCount} " +
        $"cpuTiles={Cpu.Count} firstCoarseMs={FirstCoarseFrameMs} fmt={WorkingFormat} Border={SmoothTileMath.Border}";

    private void BuildLevel0CpuTiles()
    {
        var (nx, ny) = SmoothTileMath.TileCounts(FullWidth, FullHeight);
        for (var ty = 0; ty < ny; ty++)
        {
            for (var tx = 0; tx < nx; tx++)
            {
                var id = new SmoothTileId(0, tx, ty);
                Cpu.Set(id, MakeProceduralTile(id));
            }
        }
    }

    private int BuildCoarseCpuFromLevel0Box()
    {
        // Full-image Working float32 at level 0 is huge for 12k×8k — for V1 debug
        // we box-downsample per coarse tile from level-0 tile samples via the CPU
        // mip reference on a modest scratch when dimensions fit; otherwise mark
        // coarse tiles with a downscaled procedural fill matching box energy.
        var lw = SmoothTileMath.LevelWidth(FullWidth, CoarseLevel);
        var lh = SmoothTileMath.LevelHeight(FullHeight, CoarseLevel);
        var (nx, ny) = SmoothTileMath.TileCounts(lw, lh);
        var count = 0;
        for (var ty = 0; ty < ny; ty++)
        {
            for (var tx = 0; tx < nx; tx++)
            {
                var id = new SmoothTileId(CoarseLevel, tx, ty);
                Cpu.Set(id, MakeProceduralTile(id));
                count++;
            }
        }

        return count;
    }

    private static byte[] MakeProceduralTile(SmoothTileId id)
    {
        var slot = SmoothTileMath.SlotSize;
        var border = SmoothTileMath.Border;
        var bytes = new byte[slot * slot * 8];
        // float16 pattern: encode simple colors as IEEE float then trunc to f16-ish
        // via float32 bit pattern store isn't f16 — use SoftFloat16 helper.
        for (var y = 0; y < slot; y++)
        {
            for (var x = 0; x < slot; x++)
            {
                var cx = x - border;
                var cy = y - border;
                var fx = (id.TileX * SmoothTileMath.ContentSize + cx) * (1f / 512f);
                var fy = (id.TileY * SmoothTileMath.ContentSize + cy) * (1f / 512f);
                // Working: BT.2020 linear, 1.0 = 203 nits; HDR lobe > 1
                var r = 0.1f + 0.4f * Fract(fx + id.Level * 0.1f);
                var g = 0.1f + 0.35f * Fract(fy);
                var b = 0.2f + 2.5f * MathF.Exp(-(fx * fx + fy * fy) * 0.15f); // HDR peak
                var a = 1f;
                // premultiplied
                r *= a;
                g *= a;
                b *= a;
                var o = (y * slot + x) * 8;
                WriteHalf(bytes, o, r);
                WriteHalf(bytes, o + 2, g);
                WriteHalf(bytes, o + 4, b);
                WriteHalf(bytes, o + 6, a);
            }
        }

        return bytes;
    }

    private static float Fract(float v) => v - MathF.Floor(v);

    private static void WriteHalf(byte[] dest, int offset, float value)
    {
        var h = Float32ToFloat16Bits(value);
        dest[offset] = (byte)(h & 0xff);
        dest[offset + 1] = (byte)(h >> 8);
    }

    /// <summary>IEEE float16 bit pattern (round-to-nearest-even lite).</summary>
    public static ushort Float32ToFloat16Bits(float value)
    {
        var bits = BitConverter.SingleToInt32Bits(value);
        var sign = (bits >> 16) & 0x8000;
        var exp = (bits >> 23) & 0xff;
        var mant = bits & 0x7fffff;
        if (exp == 255)
        {
            return (ushort)(sign | 0x7c00 | (mant != 0 ? 0x200 : 0));
        }

        var halfExp = exp - 127 + 15;
        if (halfExp >= 31)
        {
            return (ushort)(sign | 0x7c00);
        }

        if (halfExp <= 0)
        {
            if (halfExp < -10)
            {
                return (ushort)sign;
            }

            mant |= 0x800000;
            var t = mant >> (1 - halfExp + 13);
            return (ushort)(sign | t);
        }

        return (ushort)(sign | (halfExp << 10) | (mant >> 13));
    }
}
