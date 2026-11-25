namespace Palace.Helpers;

/// <summary>
/// Tile session: builds Working-space tiles on a background task, mips
/// L→L+1 via <see cref="SmoothMipDownsampleCpu.BoxDownsampleTileSlot"/> (and
/// optional GPU <c>TileMipBox</c>), pins the coarse set, and never retains every
/// L0 tile in managed memory. V2 feeds Palace.Imaging Working L0 tiles
/// (<see cref="BuildFromWorkingAsync"/>); synthetic procedural remains for
/// tests. EXIF is never baked; cloud online-only paths are refused via
/// <see cref="SmoothTileDecodeGate"/>.
/// </summary>
public sealed class SmoothTileSession
{
    private IReadOnlyDictionary<SmoothTileId, byte[]>? _level0Override;
    private float[]? _workingRgba;
    private int _workingW;
    private int _workingH;
    private readonly Queue<SmoothTileId> _streamCpuOrder = new();
    private const int StreamCpuCap = 64;

    public int FullWidth { get; }
    public int FullHeight { get; }
    public int CoarseLevel { get; }
    public int MaxLevel { get; }
    public int PoolCapacity { get; private set; }
    public SmoothCpuTileCache Cpu { get; } = new();
    public SmoothGpuTileLru Lru { get; private set; }
    public long FirstCoarseFrameMs { get; private set; }
    public int CoarseTileCount { get; private set; }
    public string WorkingFormat { get; } = SmoothTileWorkingSpace.GpuFormat;
    public long AvailableVramBytes { get; private set; }
    public bool UsedWorkingDecode { get; private set; }
    public bool HasRetainedWorking => _workingRgba is not null;

    public SmoothTileSession(int fullWidth, int fullHeight, long availableVramBytes = 0)
    {
        if (fullWidth < 1 || fullHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fullWidth));
        }

        FullWidth = fullWidth;
        FullHeight = fullHeight;
        CoarseLevel = SmoothTileMath.CoarseLevelIndex(fullWidth, fullHeight);
        MaxLevel = SmoothTileMath.MaxLevel(fullWidth, fullHeight);
        CoarseTileCount = SmoothTileMath.CoarseTileCount(fullWidth, fullHeight);
        AvailableVramBytes = availableVramBytes;
        var seedVram = availableVramBytes > 0
            ? availableVramBytes
            : CoarseTileCount * SmoothTileMath.BytesPerTileRgba16F();
        PoolCapacity = SmoothGpuTileLru.CapacityFromVramBytes(seedVram, CoarseTileCount);
        Lru = new SmoothGpuTileLru(PoolCapacity, CoarseLevel);
    }

    /// <summary>GPU slot → tile whose CPU bytes were last uploaded there (upload dedup).</summary>
    private readonly Dictionary<int, SmoothTileId> _gpuSlotHoldsCpu = new();

    public void ApplyVramBudget(long availableVramBytes)
    {
        AvailableVramBytes = Math.Max(0, availableVramBytes);
        PoolCapacity = SmoothGpuTileLru.CapacityFromVramBytes(AvailableVramBytes, CoarseTileCount);
        Lru = new SmoothGpuTileLru(PoolCapacity, CoarseLevel);
        _gpuSlotHoldsCpu.Clear();
    }

    /// <summary>
    /// Background build. Callbacks are passed in (not mutated on the instance)
    /// so device-lost recreate cannot race a second build against stale delegates.
    /// </summary>
    public Task<long> BuildPyramidAsync(
        CancellationToken cancellationToken,
        Action<SmoothGpuMipRequest>? gpuMip = null,
        Action? afterLevel = null,
        string? sourcePath = null)
    {
        if (sourcePath is not null && !SmoothTileDecodeGate.MayDecodePath(sourcePath))
        {
            throw new InvalidOperationException("Decode refused by SmoothTileDecodeGate.");
        }

        return Task.Run(
            () => BuildPyramidCore(cancellationToken, gpuMip, afterLevel),
            cancellationToken);
    }

    /// <summary>Sync helper for unit tests (prefer modest sizes).</summary>
    public long BuildSyntheticLevel0AndCoarse()
    {
        using var cts = new CancellationTokenSource();
        return BuildPyramidCore(cts.Token, gpuMip: null, afterLevel: null);
    }

    public string StatusLine() =>
        $"{(UsedWorkingDecode ? "V2" : "V1")} tiles {FullWidth}x{FullHeight} coarse=L{CoarseLevel} " +
        $"coarseTiles={CoarseTileCount} pool={PoolCapacity} resident={Lru.ResidentCount} " +
        $"cpuTiles={Cpu.Count} firstCoarseMs={FirstCoarseFrameMs} fmt={WorkingFormat} Border={SmoothTileMath.Border}";

    /// <summary>
    /// V2: build the pyramid from Palace.Imaging Working L0 (cancel-aware).
    /// Retains Working floats for on-demand finer-level CPU tiles after the
    /// pyramid drops intermediate levels.
    /// </summary>
    public Task<long> BuildFromWorkingAsync(
        SmoothWorkingDecodeResult working,
        CancellationToken cancellationToken,
        Action<SmoothGpuMipRequest>? gpuMip = null,
        Action? afterLevel = null)
    {
        if (working.Width != FullWidth || working.Height != FullHeight)
        {
            throw new ArgumentException(
                $"Working size {working.Width}x{working.Height} != session {FullWidth}x{FullHeight}.",
                nameof(working));
        }

        _level0Override = working.Level0Tiles.Count > 0 ? working.Level0Tiles : null;
        _workingRgba = working.WorkingRgba;
        _workingW = working.Width;
        _workingH = working.Height;
        UsedWorkingDecode = true;
        working.ClearNormalizedFrame();
        return Task.Run(
            () =>
            {
                try
                {
                    return BuildPyramidCore(cancellationToken, gpuMip, afterLevel);
                }
                finally
                {
                    _level0Override = null;
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Ensure a CPU tile exists for streaming uploads. Uses retained Working
    /// floats for finer levels after the pyramid has dropped them from Cpu.
    /// Bounded LRU (~<see cref="StreamCpuCap"/>) for non-coarse tiles.
    /// </summary>
    public bool TryEnsureCpuTile(SmoothTileId id)
    {
        if (Cpu.TryGet(id, out _))
        {
            return true;
        }

        if (_workingRgba is null || _workingW < 1 || _workingH < 1)
        {
            return false;
        }

        if (id.Level < 0 || id.Level > MaxLevel)
        {
            return false;
        }

        var bytes = SmoothWorkingDecode.TileFromWorkingAtLevel(
            _workingRgba, _workingW, _workingH, id.Level, id.TileX, id.TileY);
        Cpu.Set(id, bytes);
        if (id.Level < CoarseLevel)
        {
            _streamCpuOrder.Enqueue(id);
            while (_streamCpuOrder.Count > StreamCpuCap)
            {
                var drop = _streamCpuOrder.Dequeue();
                if (drop.Level < CoarseLevel)
                {
                    _ = Cpu.Remove(drop);
                }
            }
        }

        return true;
    }

    private long BuildPyramidCore(
        CancellationToken cancellationToken,
        Action<SmoothGpuMipRequest>? gpuMip,
        Action? afterLevel)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Cpu.Clear();
        Lru.Reset();
        _gpuSlotHoldsCpu.Clear();

        if (CoarseLevel == 0)
        {
            var (nx0, ny0) = SmoothTileMath.TileCounts(FullWidth, FullHeight);
            for (var y = 0; y < ny0; y++)
            {
                for (var x = 0; x < nx0; x++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var id = new SmoothTileId(0, x, y);
                    Cpu.Set(id, MakeProceduralTile(id));
                    _ = Lru.TryInsert(id, out _, out _);
                }
            }

            CoarseTileCount = nx0 * ny0;
            sw.Stop();
            FirstCoarseFrameMs = sw.ElapsedMilliseconds;
            return FirstCoarseFrameMs;
        }

        for (var level = 0; level < CoarseLevel; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BuildNextLevelFromChildren(level, cancellationToken, gpuMip);
            afterLevel?.Invoke();
            Cpu.RemoveLevel(level);
            Lru.ClearTempPins();
        }

        var lw = SmoothTileMath.LevelWidth(FullWidth, CoarseLevel);
        var lh = SmoothTileMath.LevelHeight(FullHeight, CoarseLevel);
        var (nx, ny) = SmoothTileMath.TileCounts(lw, lh);
        // V1 authoritative path is CPU parents; rematerialize coarse residency for upload.
        Lru.Reset();
        _gpuSlotHoldsCpu.Clear();
        var count = 0;
        for (var y = 0; y < ny; y++)
        {
            for (var x = 0; x < nx; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = new SmoothTileId(CoarseLevel, x, y);
                if (!Cpu.TryGet(id, out _))
                {
                    continue;
                }

                _ = Lru.TryInsert(id, out _, out _);
                count++;
            }
        }

        CoarseTileCount = count;
        sw.Stop();
        FirstCoarseFrameMs = sw.ElapsedMilliseconds;
        return FirstCoarseFrameMs;
    }

    private void BuildNextLevelFromChildren(
        int childLevel,
        CancellationToken cancellationToken,
        Action<SmoothGpuMipRequest>? gpuMip)
    {
        var parentLevel = childLevel + 1;
        var childW = SmoothTileMath.LevelWidth(FullWidth, childLevel);
        var childH = SmoothTileMath.LevelHeight(FullHeight, childLevel);
        var parentW = SmoothTileMath.LevelWidth(FullWidth, parentLevel);
        var parentH = SmoothTileMath.LevelHeight(FullHeight, parentLevel);
        var (childNx, childNy) = SmoothTileMath.TileCounts(childW, childH);
        var (parentNx, parentNy) = SmoothTileMath.TileCounts(parentW, parentH);

        for (var py = 0; py < parentNy; py++)
        {
            for (var px = 0; px < parentNx; px++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (ox, oy) = SmoothTileMath.ContentOrigin(px, py);

                var src = AssembleParentFootprint(
                    childLevel,
                    childW,
                    childH,
                    childNx,
                    childNy,
                    ox,
                    oy,
                    out var patchOriginX,
                    out var patchOriginY,
                    out var patchW,
                    out var patchH);
                // BoxDownsampleTileSlot doubles its origin argument into child (level-L)
                // px, and the patch starts at child px patchOrigin. So pass the parent
                // origin relative to the patch in PARENT px: (2*ox - patchOrigin) / 2.
                // (The old `ox - patchOriginX` mixed parent and child px and was only
                // right for px == 0 / py == 0 — every other CPU parent was misread.)
                var f32 = SmoothMipDownsampleCpu.BoxDownsampleTileSlot(
                    src,
                    patchW,
                    patchH,
                    ox - (patchOriginX / 2),
                    oy - (patchOriginY / 2));
                var parentId = new SmoothTileId(parentLevel, px, py);
                var bytes = Float32SlotToRgba16F(f32);
                Cpu.Set(parentId, bytes);

                var ring = new SmoothTileId?[16];
                SmoothTileMath.FillChildRing4x4(px, py, childNx, childNy, ring);
                var slices = new uint[16];
                var tempPinned = new List<SmoothTileId>(16);
                var childUploads = new List<(SmoothTileId Id, uint Slot, byte[] Bytes)>(16);
                var childMissing = false;

                for (var i = 0; i < 16; i++)
                {
                    if (ring[i] is null)
                    {
                        slices[i] = SmoothTileMath.SliceOutside;
                        continue;
                    }

                    var childId = new SmoothTileId(childLevel, ring[i]!.Value.TileX, ring[i]!.Value.TileY);
                    EnsureChildTile(childId);
                    if (!Cpu.TryGet(childId, out var childBytes))
                    {
                        // In-image child must never become SliceOutside — skip GPU for this parent.
                        childMissing = true;
                        break;
                    }

                    if (!Lru.TryGetSlot(childId, out var childSlot))
                    {
                        if (!Lru.TryInsert(childId, out childSlot, out _))
                        {
                            childMissing = true;
                            break;
                        }
                    }

                    Lru.SetTempPin(childId, true);
                    tempPinned.Add(childId);
                    slices[i] = (uint)childSlot;
                    // Neighbouring parents share 12 of 16 ring children; only upload a
                    // child when its slot does not already hold this child's CPU bytes.
                    if (!_gpuSlotHoldsCpu.TryGetValue(childSlot, out var resident) || resident != childId)
                    {
                        childUploads.Add((childId, (uint)childSlot, childBytes));
                    }
                }

                if (!childMissing && Lru.TryInsert(parentId, out var parentSlot, out _))
                {
                    // The parent slot now holds GPU output (not CPU bytes): force a CPU
                    // upload when it is used as a child next level, so each level's GPU
                    // input stays byte-identical to the CPU reference input.
                    _gpuSlotHoldsCpu.Remove(parentSlot);
                    foreach (var (upId, upSlot, _) in childUploads)
                    {
                        _gpuSlotHoldsCpu[(int)upSlot] = upId;
                    }

                    gpuMip?.Invoke(new SmoothGpuMipRequest(
                        ChildLevelWidth: childW,
                        ChildLevelHeight: childH,
                        ParentContentOriginX: ox,
                        ParentContentOriginY: oy,
                        DstSlice: (uint)parentSlot,
                        Slices4x4: slices,
                        ParentBytes: bytes,
                        ChildUploads: childUploads.ToArray()));
                }

                foreach (var t in tempPinned)
                {
                    Lru.SetTempPin(t, false);
                }
            }

            // After the full parent row — not per-tile — drop finished child rows.
            DropFinishedChildRows(childLevel, childNx, childNy, py);
        }
    }

    private void DropFinishedChildRows(int childLevel, int childNx, int childNy, int parentYDone)
    {
        var dropBefore = (parentYDone * 2) - 1;
        if (dropBefore <= 0)
        {
            return;
        }

        for (var ty = 0; ty < dropBefore && ty < childNy; ty++)
        {
            for (var tx = 0; tx < childNx; tx++)
            {
                _ = Cpu.Remove(new SmoothTileId(childLevel, tx, ty));
            }
        }
    }

    private void EnsureChildTile(SmoothTileId id)
    {
        if (Cpu.TryGet(id, out _))
        {
            return;
        }

        if (_level0Override is not null
            && id.Level == 0
            && _level0Override.TryGetValue(id, out var workingBytes))
        {
            Cpu.Set(id, workingBytes);
            return;
        }

        if (_workingRgba is not null && TryEnsureCpuTile(id))
        {
            return;
        }

        if (id.Level == 0 && !UsedWorkingDecode)
        {
            // Procedural fill is tests / legacy only — never the primary V2 path.
            Cpu.Set(id, MakeProceduralTile(id));
            return;
        }

        throw new InvalidOperationException(
            $"Missing tile {id}; V2 requires retained Working, V1 level>0 must come from prior mip.");
    }

    private float[] AssembleParentFootprint(
        int childLevel,
        int childW,
        int childH,
        int childNx,
        int childNy,
        int parentContentOriginX,
        int parentContentOriginY,
        out int patchOriginX,
        out int patchOriginY,
        out int patchW,
        out int patchH)
    {
        var originL = (parentContentOriginX * 2, parentContentOriginY * 2);
        var extent = SmoothTileMath.SlotSize * 2 + 16;
        patchOriginX = Math.Max(0, originL.Item1 - 8);
        patchOriginY = Math.Max(0, originL.Item2 - 8);
        var maxX = Math.Min(childW, originL.Item1 + extent);
        var maxY = Math.Min(childH, originL.Item2 + extent);
        patchW = Math.Max(1, maxX - patchOriginX);
        patchH = Math.Max(1, maxY - patchOriginY);

        var src = new float[patchW * patchH * 4];
        var t0x = Math.Max(0, (patchOriginX / SmoothTileMath.ContentSize) - 1);
        var t0y = Math.Max(0, (patchOriginY / SmoothTileMath.ContentSize) - 1);
        var t1x = Math.Min(childNx - 1, ((patchOriginX + patchW - 1) / SmoothTileMath.ContentSize) + 1);
        var t1y = Math.Min(childNy - 1, ((patchOriginY + patchH - 1) / SmoothTileMath.ContentSize) + 1);

        for (var ty = t0y; ty <= t1y; ty++)
        {
            for (var tx = t0x; tx <= t1x; tx++)
            {
                var id = new SmoothTileId(childLevel, tx, ty);
                EnsureChildTile(id);
                if (!Cpu.TryGet(id, out var tileBytes))
                {
                    continue;
                }

                var (ox, oy, cw, ch) = SmoothTileMath.ContentRect(tx, ty, childW, childH);
                for (var cy = 0; cy < ch; cy++)
                {
                    var iy = oy + cy;
                    if (iy < patchOriginY || iy >= patchOriginY + patchH)
                    {
                        continue;
                    }

                    for (var cx = 0; cx < cw; cx++)
                    {
                        var ix = ox + cx;
                        if (ix < patchOriginX || ix >= patchOriginX + patchW)
                        {
                            continue;
                        }

                        var (sx, sy) = SmoothTileMath.ContentToSlot(cx, cy);
                        var bi = (sy * SmoothTileMath.SlotSize + sx) * 8;
                        var o = ((iy - patchOriginY) * patchW + (ix - patchOriginX)) * 4;
                        src[o] = HalfBitsToFloat32(tileBytes[bi], tileBytes[bi + 1]);
                        src[o + 1] = HalfBitsToFloat32(tileBytes[bi + 2], tileBytes[bi + 3]);
                        src[o + 2] = HalfBitsToFloat32(tileBytes[bi + 4], tileBytes[bi + 5]);
                        src[o + 3] = HalfBitsToFloat32(tileBytes[bi + 6], tileBytes[bi + 7]);
                    }
                }
            }
        }

        return src;
    }

    private static byte[] Float32SlotToRgba16F(float[] f32)
    {
        var bytes = new byte[SmoothTileMath.BytesPerTileRgba16F()];
        for (var i = 0; i < f32.Length; i++)
        {
            WriteHalf(bytes, i * 2, f32[i]);
        }

        return bytes;
    }

    private static byte[] MakeProceduralTile(SmoothTileId id)
    {
        var slot = SmoothTileMath.SlotSize;
        var border = SmoothTileMath.Border;
        var bytes = new byte[slot * slot * 8];
        var invContent = 1f / SmoothTileMath.ContentSize;
        for (var y = 0; y < slot; y++)
        {
            for (var x = 0; x < slot; x++)
            {
                var cx = x - border;
                var cy = y - border;
                var fx = (id.TileX * SmoothTileMath.ContentSize + cx) * invContent;
                var fy = (id.TileY * SmoothTileMath.ContentSize + cy) * invContent;
                var r = 0.1f + 0.4f * Fract(fx + id.Level * 0.1f);
                var g = 0.1f + 0.35f * Fract(fy);
                var b = 0.2f + 2.5f * MathF.Exp(-(fx * fx + fy * fy) * 0.15f);
                var a = 1f;
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

    // Exact half→float (hardware/BCL path). The old MathF.Pow-per-channel version
    // was the hottest loop in the pyramid build (~4M calls per parent tile).
    private static float HalfBitsToFloat32(byte lo, byte hi) =>
        (float)BitConverter.UInt16BitsToHalf((ushort)(lo | (hi << 8)));

    /// <summary>IEEE float16 bit pattern with round-to-nearest-even.</summary>
    public static ushort Float32ToFloat16Bits(float value)
    {
        var bits = (uint)BitConverter.SingleToInt32Bits(value);
        var sign = (bits >> 16) & 0x8000u;
        var exp = (bits >> 23) & 0xffu;
        var mant = bits & 0x7fffffu;

        if (exp == 255)
        {
            return (ushort)(sign | 0x7c00u | (mant != 0 ? 0x200u : 0u));
        }

        var halfExp = (int)exp - 127 + 15;
        if (halfExp >= 31)
        {
            return (ushort)(sign | 0x7c00u);
        }

        if (halfExp <= 0)
        {
            if (halfExp < -10)
            {
                return (ushort)sign;
            }

            mant |= 0x800000u;
            var shift = 1 - halfExp + 13;
            var rounded = (mant + (1u << (shift - 1)) - 1u + ((mant >> shift) & 1u)) >> shift;
            return (ushort)(sign | rounded);
        }

        var keep = mant >> 13;
        var remainder = mant & 0x1fffu;
        var half = 1u << 12;
        if (remainder > half || (remainder == half && (keep & 1u) != 0))
        {
            keep++;
        }

        if (keep == 0x400u)
        {
            keep = 0;
            halfExp++;
            if (halfExp >= 31)
            {
                return (ushort)(sign | 0x7c00u);
            }
        }

        return (ushort)(sign | ((uint)halfExp << 10) | keep);
    }
}

/// <summary>One GPU mip dispatch request produced while building the pyramid.</summary>
public readonly record struct SmoothGpuMipRequest(
    int ChildLevelWidth,
    int ChildLevelHeight,
    int ParentContentOriginX,
    int ParentContentOriginY,
    uint DstSlice,
    uint[] Slices4x4,
    byte[] ParentBytes,
    (SmoothTileId Id, uint Slot, byte[] Bytes)[] ChildUploads);
