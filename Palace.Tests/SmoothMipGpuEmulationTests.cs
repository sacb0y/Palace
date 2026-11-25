using System.Diagnostics;
using Palace.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Palace.Tests;

/// <summary>
/// Emulates Geo's SmoothMipDownsampleCS (box, Border=2, RGBA16F) on the exact
/// SmoothGpuMipRequest the presenter dispatches, and holds the CPU parent to the
/// same ≤1 half-ULP gate as the Fenrir readback. Parents with tile X/Y ≥ 1 are
/// the ones the Fenrir pack failed.
/// </summary>
public sealed class SmoothMipGpuEmulationTests
{
    private readonly ITestOutputHelper _out;

    public SmoothMipGpuEmulationTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task CpuParent_MatchesEmulatedCs_WithinOneHalfUlp_AllParents()
    {
        // L0 9×3 tiles → L1 2050×550 (5×2 parents) → L2 coarse (3×1 parents).
        var session = new SmoothTileSession(4100, 1100, availableVramBytes: 8L << 30);
        Assert.Equal(2, session.CoarseLevel);
        var mips = 0;
        var worstOver = 0;
        var uploads = 0;
        // Persistent emulated Texture2DArray: uploads land in slots, the CS writes the
        // parent slot. Catches upload-dedup bugs (stale or GPU-written child slots).
        var gpu = new Dictionary<uint, byte[]>();
        await session.BuildPyramidAsync(CancellationToken.None, req =>
        {
            mips++;
            foreach (var (_, upSlot, bytes) in req.ChildUploads)
            {
                gpu[upSlot] = bytes;
                uploads++;
            }

            var outBytes = Emulate(req, gpu);
            gpu[req.DstSlice] = outBytes;
            var over = CountOverOneUlp(req, outBytes);
            worstOver = Math.Max(worstOver, over);
            _out.WriteLine($"mip#{mips} origin=({req.ParentContentOriginX},{req.ParentContentOriginY}) uploads={req.ChildUploads.Length} over1ulp={over}");
        });

        _out.WriteLine($"mips={mips} childUploads={uploads} (no dedup would be {mips * 16} minus SliceOutside)");
        Assert.Equal(13, mips);
        Assert.Equal(0, worstOver);
        // 27 L0 + 10 L1 children, each uploaded once (pool is large enough).
        Assert.Equal(37, uploads);
    }

    /// <summary>
    /// Full 12k×8k wall timing — Fenrir / local only. CI excludes
    /// <c>Category=FenrirPerf</c> so this stays off the PR gate; per-parent
    /// emulation above still runs in CI.
    /// </summary>
    [Fact]
    [Trait("Category", "FenrirPerf")]
    public void Timing_12000x8000_Procedural_Pyramid()
    {
        var session = new SmoothTileSession(12000, 8000);
        var sw = Stopwatch.StartNew();
        var ms = session.BuildSyntheticLevel0AndCoarse();
        _out.WriteLine($"firstCoarseMs={ms} wall={sw.ElapsedMilliseconds} coarse=L{session.CoarseLevel} tiles={session.CoarseTileCount}");
        Assert.Equal(6, session.CoarseTileCount);
    }

    private static byte[] Emulate(SmoothGpuMipRequest req, Dictionary<uint, byte[]> bySlot)
    {
        const int slot = SmoothTileMath.SlotSize;
        const int border = SmoothTileMath.Border;
        const int content = SmoothTileMath.ContentSize;
        var ox = req.ParentContentOriginX * 2;
        var oy = req.ParentContentOriginY * 2;
        var dst = new byte[slot * slot * 8];

        (float, float, float, float) FetchL(int px, int py)
        {
            px = Math.Clamp(px, 0, req.ChildLevelWidth - 1);
            py = Math.Clamp(py, 0, req.ChildLevelHeight - 1);
            var rx = px - ox;
            var ry = py - oy;
            var tx = Math.Clamp((int)Math.Floor(rx / (double)content), -1, 2);
            var ty = Math.Clamp((int)Math.Floor(ry / (double)content), -1, 2);
            var lx = rx - (tx * content) + border;
            var ly = ry - (ty * content) + border;
            var s = req.Slices4x4[((ty + 1) * 4) + tx + 1];
            var b = bySlot[s];
            var i = ((ly * slot) + lx) * 8;
            return (H(b, i), H(b, i + 2), H(b, i + 4), H(b, i + 6));
        }

        for (var y = 0; y < slot; y++)
        {
            for (var x = 0; x < slot; x++)
            {
                var s0x = ox + (2 * (x - border));
                var s0y = oy + (2 * (y - border));
                var a = FetchL(s0x, s0y);
                var bb = FetchL(s0x + 1, s0y);
                var c = FetchL(s0x, s0y + 1);
                var e = FetchL(s0x + 1, s0y + 1);
                var o = ((y * slot) + x) * 8;
                W(dst, o, (a.Item1 + bb.Item1 + c.Item1 + e.Item1) * 0.25f);
                W(dst, o + 2, (a.Item2 + bb.Item2 + c.Item2 + e.Item2) * 0.25f);
                W(dst, o + 4, (a.Item3 + bb.Item3 + c.Item3 + e.Item3) * 0.25f);
                W(dst, o + 6, (a.Item4 + bb.Item4 + c.Item4 + e.Item4) * 0.25f);
            }
        }

        return dst;
    }

    private static int CountOverOneUlp(SmoothGpuMipRequest req, byte[] gpu)
    {
        var over = 0;
        for (var i = 0; i < gpu.Length; i += 2)
        {
            var c = req.ParentBytes[i] | (req.ParentBytes[i + 1] << 8);
            var g = gpu[i] | (gpu[i + 1] << 8);
            if (Math.Abs(c - g) > 1)
            {
                over++;
            }
        }

        return over;
    }

    private static float H(byte[] b, int i) =>
        (float)BitConverter.UInt16BitsToHalf((ushort)(b[i] | (b[i + 1] << 8)));

    private static void W(byte[] d, int o, float v)
    {
        var h = SmoothTileSession.Float32ToFloat16Bits(v);
        d[o] = (byte)(h & 0xff);
        d[o + 1] = (byte)(h >> 8);
    }
}
