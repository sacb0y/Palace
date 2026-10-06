using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class HdrPresentOffThreadTests
{
    private static float[] Gradient(int w, int h)
    {
        var src = new float[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            src[i * 4] = i * 0.37f;
            src[(i * 4) + 1] = (i % 7) * 1.5f;
            src[(i * 4) + 2] = -0.25f + (i % 3);
            src[(i * 4) + 3] = 1f;
        }

        return src;
    }

    // The pre-offthread presenter loop, kept as the oracle.
    private static ushort[] Reference(float[] src, int sw, int sh, ImageScaling scaling, int vw, int vh, float clip)
    {
        var dest = new ushort[vw * vh * 4];
        var (dx, dy, dw, dh) = GalleryPresent.DestRect(scaling, sw, sh, vw, vh);
        for (var y = 0; y < vh; y++)
        {
            for (var x = 0; x < vw; x++)
            {
                var u = (x + 0.5f - dx) / dw;
                var v = (y + 0.5f - dy) / dh;
                if (u < 0 || v < 0 || u >= 1 || v >= 1)
                {
                    continue;
                }

                var sx = Math.Clamp((int)(u * sw), 0, sw - 1);
                var sy = Math.Clamp((int)(v * sh), 0, sh - 1);
                var si = (sy * sw + sx) * 4;
                var di = (y * vw + x) * 4;
                dest[di] = GalleryPresent.FloatToHalf(Math.Clamp(src[si], 0, clip));
                dest[di + 1] = GalleryPresent.FloatToHalf(Math.Clamp(src[si + 1], 0, clip));
                dest[di + 2] = GalleryPresent.FloatToHalf(Math.Clamp(src[si + 2], 0, clip));
                dest[di + 3] = GalleryPresent.FloatToHalf(src[si + 3]);
            }
        }

        return dest;
    }

    [Theory]
    [InlineData(ImageScaling.Fit, 16, 9, 40, 40)]
    [InlineData(ImageScaling.Fit, 9, 16, 40, 24)]
    [InlineData(ImageScaling.Fill, 16, 9, 33, 21)]
    [InlineData(ImageScaling.Actual, 12, 8, 12, 8)]
    [InlineData(ImageScaling.Fit, 8, 8, 8, 8)]
    public void Fill_MatchesReference_EvenInDirtyReusedBuffer(
        ImageScaling scaling, int sw, int sh, int vw, int vh)
    {
        var src = Gradient(sw, sh);
        var clip = GalleryPresent.RasterizeClipScrgb(1000f);
        var expected = Reference(src, sw, sh, scaling, vw, vh, clip);
        var dest = new ushort[expected.Length + 64];
        Array.Fill(dest, (ushort)0xBEEF);

        HdrRasterize.Fill(src, sw, sh, scaling, vw, vh, clip, dest, CancellationToken.None);

        Assert.Equal(expected, dest.Take(expected.Length).ToArray());
        Assert.All(dest.Skip(expected.Length), v => Assert.Equal(0xBEEF, v));
    }

    [Fact]
    public void Fill_SdrMapsCllThenClipsToScrgbWhite()
    {
        var src = new float[] { 12.5f, 12.5f, 12.5f, 1f };
        var dest = new ushort[4];
        var map = GalleryPresent.PresentMap(false, 1000f, 0);
        HdrRasterize.Fill(src, 1, 1, ImageScaling.Fit, 1, 1, map.ClipScrgb, dest, CancellationToken.None, map.Scale);
        Assert.Equal(GalleryPresent.FloatToHalf(1f), dest[0]);
        Assert.Equal(1f, HdrColor.MapCllToDisplayScrgb(12.5f, 1000f, 80f), 3);
        var hdr = GalleryPresent.PresentMap(true, 1000f, 0);
        Assert.Equal(1f, hdr.Scale);
        Assert.Equal(125f, hdr.ClipScrgb);
    }

    [Fact]
    public void Fill_UnknownPeak_ClipsAtPqRangeNotPaperWhite()
    {
        var src = new float[] { 100f, 100f, 100f, 1f };
        var dest = new ushort[4];
        HdrRasterize.Fill(src, 1, 1, ImageScaling.Fit, 1, 1, GalleryPresent.RasterizeClipScrgb(0), dest, CancellationToken.None);
        Assert.Equal(GalleryPresent.FloatToHalf(100f), dest[0]);
        Assert.Equal(125f, GalleryPresent.RasterizeClipScrgb(0));
    }

    [Fact]
    public void Fill_HonorsCancellation()
    {
        var src = Gradient(64, 64);
        var dest = new ushort[HdrRasterize.HalfLength(64, 64)];
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            HdrRasterize.Fill(src, 64, 64, ImageScaling.Fit, 64, 64, 10f, dest, cts.Token));
    }

    [Fact]
    public void Fill_RejectsShortBuffers()
    {
        Assert.Throws<ArgumentException>(() =>
            HdrRasterize.Fill(new float[4], 1, 1, ImageScaling.Fit, 2, 2, 1f, new ushort[4], CancellationToken.None));
    }

    [Fact]
    public void HalfBuffer_ReusesWithinRange_AndTrimsWhenMuchSmaller()
    {
        var buffers = new HdrHalfBuffer();
        var first = buffers.Acquire(1000);
        Assert.Same(first, buffers.Acquire(1000));
        Assert.Same(first, buffers.Acquire(900));
        Assert.Same(first, buffers.Acquire(first.Length));

        var grown = buffers.Acquire(first.Length + 1);
        Assert.NotSame(first, grown);
        Assert.True(grown.Length >= first.Length + 1);

        var trimmed = buffers.Acquire(10);
        Assert.NotSame(grown, trimmed);
        Assert.True(trimmed.Length < grown.Length);

        buffers.Release();
        Assert.Equal(0, buffers.Capacity);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffers.Acquire(0));
    }

    [Fact]
    public void Coalescer_FirstRequestStartsLoop_LaterOnesJoinIt()
    {
        var q = new HdrPresentCoalescer();
        Assert.True(q.Request(HdrPresentCoalescer.SettleMs));
        Assert.False(q.Request(HdrPresentCoalescer.SettleMs));
        Assert.False(q.Request(HdrPresentCoalescer.SettleMs));

        Assert.True(q.TryTake(out var delay));
        Assert.Equal(HdrPresentCoalescer.SettleMs, delay);
        Assert.False(q.Superseded);
        Assert.False(q.TryTake(out _));
        Assert.False(q.IsRunning);
        Assert.True(q.Request(0));
    }

    [Fact]
    public void Coalescer_ImmediateWinsOverSettle_AndResetsAfterTake()
    {
        var q = new HdrPresentCoalescer();
        q.Request(HdrPresentCoalescer.SettleMs);
        q.Request(HdrPresentCoalescer.ImmediateMs);
        q.Request(HdrPresentCoalescer.SettleMs);
        Assert.True(q.TryTake(out var delay));
        Assert.Equal(0, delay);

        q.Request(HdrPresentCoalescer.SettleMs);
        Assert.True(q.TryTake(out delay));
        Assert.Equal(HdrPresentCoalescer.SettleMs, delay);
    }

    [Fact]
    public void Coalescer_RequestDuringWork_RunsAgainOnce()
    {
        var q = new HdrPresentCoalescer();
        Assert.True(q.Request(0));
        Assert.True(q.TryTake(out _));
        Assert.False(q.Request(HdrPresentCoalescer.SettleMs));
        Assert.False(q.Request(HdrPresentCoalescer.SettleMs));
        Assert.True(q.TryTake(out _));
        Assert.False(q.TryTake(out _));
    }

    [Fact]
    public void Coalescer_DebounceRestartsUntilMax()
    {
        var q = new HdrPresentCoalescer();
        q.Request(HdrPresentCoalescer.SettleMs);
        q.TryTake(out _);
        Assert.False(q.ShouldRestartDebounce(HdrPresentCoalescer.SettleMs));
        q.Request(HdrPresentCoalescer.SettleMs);
        Assert.True(q.ShouldRestartDebounce(HdrPresentCoalescer.SettleMs));
        Assert.False(q.ShouldRestartDebounce(HdrPresentCoalescer.MaxDebounceMs));
    }

    [Fact]
    public void Coalescer_Abort_AllowsRestart()
    {
        var q = new HdrPresentCoalescer();
        q.Request(0);
        q.Abort();
        Assert.False(q.IsRunning);
        Assert.True(q.Request(0));
    }

    [Fact]
    public void MeasureDecodeSize_UsesViewportOrFastCap_NeverNative16384()
    {
        var view = GalleryPresent.MeasureDecodeSize(3840, 2160, 1280, 720);
        Assert.Equal(GalleryPresent.PresentDecodeSize(3840, 2160, 1280, 720, ImageScaling.Fit), view);
        Assert.True(view.Width <= 1280);
        Assert.True(view.Height <= 720);

        var huge = GalleryPresent.MeasureDecodeSize(16384, 16384, 0, 0);
        Assert.True(Math.Max(huge.Width, huge.Height) <= GalleryPresent.FastPresentLongEdge);
        Assert.NotEqual((16384, 16384), huge);

        // Actual present is native; measure always Fit-caps.
        Assert.Equal((3840, 2160), GalleryPresent.PresentDecodeSize(3840, 2160, 1280, 720, ImageScaling.Actual));
        var measureActualViewport = GalleryPresent.MeasureDecodeSize(3840, 2160, 1280, 720);
        Assert.True(measureActualViewport.Width < 3840);
    }
}
