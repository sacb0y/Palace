using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class GifFramesTests
{
    [Fact]
    public void CanScrub_RequiresHydratedLocalGif()
    {
        Assert.True(GifFrames.CanScrub(AssetKind.Gif, false, false, true, @"D:\loop.gif"));
        Assert.False(GifFrames.CanScrub(AssetKind.Gif, false, true, true, @"D:\loop.gif"));
        Assert.False(GifFrames.CanScrub(AssetKind.Gif, true, false, false, "cloud/loop.gif"));
        Assert.False(GifFrames.CanScrub(AssetKind.Image, false, false, true, @"D:\a.jpg"));
        Assert.False(GifFrames.CanScrub(AssetKind.Gif, false, false, false, @"D:\loop.gif"));
        Assert.False(GifFrames.CanScrub(AssetKind.Gif, false, false, true, @"D:\loop.png"));
        Assert.False(GifFrames.ShouldShowScrub(true, 1));
        Assert.True(GifFrames.ShouldShowScrub(true, 2));
        Assert.False(GifFrames.ShouldShowScrub(false, 12));
    }

    [Fact]
    public void IndexMath_ClampsAndWraps()
    {
        Assert.Equal(0, GifFrames.ClampIndex(-1, 4));
        Assert.Equal(3, GifFrames.ClampIndex(99, 4));
        Assert.Equal(0, GifFrames.ClampIndex(0, 0));
        Assert.Equal(2, GifFrames.Step(0, 3, -1));
        Assert.Equal(1, GifFrames.Step(0, 3, 1));
        Assert.Equal(0, GifFrames.Step(2, 3, 1));
        Assert.Equal(1, GifFrames.NextPlayIndex(0, 3));
        Assert.Equal(0, GifFrames.NextPlayIndex(2, 3));
        Assert.Equal(100, GifFrames.DisplayDelayMs(0));
        Assert.Equal(100, GifFrames.DisplayDelayMs(1));
        Assert.Equal(80, GifFrames.DisplayDelayMs(8));
        Assert.False(GifFrames.ShouldStopPlaybackOnIndexChange(true));
        Assert.True(GifFrames.ShouldStopPlaybackOnIndexChange(false));
        Assert.Equal("2 / 4", GifFrames.PositionLabel(1, 4));
        Assert.Equal("1 / 4", GifFrames.PositionLabel(-3, 4));
        Assert.Equal("", GifFrames.PositionLabel(0, 0));
    }

    [Fact]
    public void TryRead_CountsFramesAndDelays()
    {
        using var stream = new MemoryStream(TwoFrameGif());
        var info = GifFrames.TryRead(stream);
        Assert.NotNull(info);
        Assert.Equal(2, info!.Value.Width);
        Assert.Equal(1, info.Value.Height);
        Assert.Equal(2, info.Value.FrameCount);
        Assert.Equal(2, info.Value.DelaysCs.Count);
        Assert.Equal(8, info.Value.DelaysCs[0]);
        Assert.Equal(12, info.Value.DelaysCs[1]);
        Assert.True(GifFrames.ShouldShowScrub(true, info.Value.FrameCount));
    }

    [Fact]
    public void TryRenderFrame_CompositesEachIndex()
    {
        var bytes = TwoFrameGif();
        using var a = new MemoryStream(bytes);
        var first = GifFrames.TryRenderFrame(a, 0);
        using var b = new MemoryStream(bytes);
        var second = GifFrames.TryRenderFrame(b, 1);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(2, first!.Value.Width);
        Assert.Equal(8, first.Value.Bgra.Length);
        AssertRed(first.Value.Bgra, 0);
        AssertRed(first.Value.Bgra, 4);
        AssertBlue(second!.Value.Bgra, 0);
        AssertBlue(second.Value.Bgra, 4);
    }

    [Fact]
    public void TryRenderAll_OnePassMatchesPerFrame()
    {
        var bytes = TwoFrameGif();
        using var allStream = new MemoryStream(bytes);
        var all = GifFrames.TryRenderAll(allStream);
        Assert.NotNull(all);
        Assert.Equal(2, all!.Count);
        using var a = new MemoryStream(bytes);
        var first = GifFrames.TryRenderFrame(a, 0);
        using var b = new MemoryStream(bytes);
        var second = GifFrames.TryRenderFrame(b, 1);
        Assert.Equal(first!.Value.Bgra, all[0].Bgra);
        Assert.Equal(second!.Value.Bgra, all[1].Bgra);
        Assert.Equal(all[1].Bgra, GifFrames.PickCached(all, 99)!.Value.Bgra);
        Assert.Equal(all[0].Bgra, GifFrames.PickCached(all, -3)!.Value.Bgra);
        Assert.Null(GifFrames.PickCached(Array.Empty<GifFrames.Raster>(), 0));
        Assert.True(GifFrames.CacheMatchesPath(@"D:\loop.gif", @"d:\loop.gif"));
        Assert.False(GifFrames.CacheMatchesPath(@"D:\a.gif", @"D:\b.gif"));
        Assert.False(GifFrames.CacheMatchesPath(null, @"D:\loop.gif"));
        Assert.False(GifFrames.ShouldApplyGifTick(true, cacheReady: false));
        Assert.True(GifFrames.ShouldApplyGifTick(true, cacheReady: true));
        Assert.False(GifFrames.ShouldApplyGifTick(false, cacheReady: true));
        Assert.True(GifFrames.ShouldStartGifCompositeLoad(cacheReady: false, loadInFlightForSamePath: false));
        Assert.False(GifFrames.ShouldStartGifCompositeLoad(cacheReady: false, loadInFlightForSamePath: true));
        Assert.False(GifFrames.ShouldStartGifCompositeLoad(cacheReady: true, loadInFlightForSamePath: false));
    }

    [Fact]
    public void TryRenderFrame_KeepsTransparentPixelsClear()
    {
        var bytes = TransparentThenPartialGif();
        using var firstStream = new MemoryStream(bytes);
        var first = GifFrames.TryRenderFrame(firstStream, 0);
        using var secondStream = new MemoryStream(bytes);
        var second = GifFrames.TryRenderFrame(secondStream, 1);
        Assert.NotNull(first);
        Assert.NotNull(second);
        AssertRed(first!.Value.Bgra, 0);
        AssertClear(first.Value.Bgra, 4);
        AssertRed(second!.Value.Bgra, 0);
        AssertBlue(second.Value.Bgra, 4);
    }

    [Fact]
    public void TryRenderFrame_DisposalRestoreIsTransparent()
    {
        using var ms = new MemoryStream();
        ms.Write("GIF89a"u8);
        ms.WriteByte(2);
        ms.WriteByte(0);
        ms.WriteByte(1);
        ms.WriteByte(0);
        ms.WriteByte(0x81);
        ms.WriteByte(2);
        ms.WriteByte(0);
        ms.WriteByte(255);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(255);
        ms.WriteByte(255);
        ms.WriteByte(0);
        ms.WriteByte(255);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        WriteGce(ms, delayCs: 8, transparent: 2, disposal: 2);
        WriteImage(ms, [0, 0], width: 2, height: 1);
        WriteGce(ms, delayCs: 12, transparent: 2, disposal: 1);
        WriteImage(ms, [1], left: 0, width: 1, height: 1);
        ms.WriteByte(0x3B);
        using var stream = new MemoryStream(ms.ToArray());
        var second = GifFrames.TryRenderFrame(stream, 1);
        Assert.NotNull(second);
        AssertBlue(second!.Value.Bgra, 0);
        AssertClear(second.Value.Bgra, 4);
    }

    [Fact]
    public void TryRead_RejectsNonGif()
    {
        using var stream = new MemoryStream("not a gif"u8.ToArray());
        Assert.Null(GifFrames.TryRead(stream));
    }

    private static void AssertRed(byte[] bgra, int o)
    {
        Assert.Equal(0, bgra[o]);
        Assert.Equal(0, bgra[o + 1]);
        Assert.Equal(255, bgra[o + 2]);
        Assert.Equal(255, bgra[o + 3]);
    }

    private static void AssertBlue(byte[] bgra, int o)
    {
        Assert.Equal(255, bgra[o]);
        Assert.Equal(0, bgra[o + 1]);
        Assert.Equal(0, bgra[o + 2]);
        Assert.Equal(255, bgra[o + 3]);
    }

    private static void AssertClear(byte[] bgra, int o)
    {
        Assert.Equal(0, bgra[o]);
        Assert.Equal(0, bgra[o + 1]);
        Assert.Equal(0, bgra[o + 2]);
        Assert.Equal(0, bgra[o + 3]);
    }

    /// <summary>2×1 GIF89a, two full-canvas frames (red then blue).</summary>
    private static byte[] TwoFrameGif()
    {
        using var ms = new MemoryStream();
        ms.Write("GIF89a"u8);
        ms.WriteByte(2);
        ms.WriteByte(0);
        ms.WriteByte(1);
        ms.WriteByte(0);
        ms.WriteByte(0x80);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(255);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(255);
        WriteGce(ms, delayCs: 8);
        WriteImage(ms, [0, 0]);
        WriteGce(ms, delayCs: 12);
        WriteImage(ms, [1, 1]);
        ms.WriteByte(0x3B);
        return ms.ToArray();
    }

    /// <summary>
    /// 2×1 red + hole, then a 1-wide blue pixel over the hole.
    /// Magenta is the LSD background / transparent index — it must not
    /// appear as an opaque backdrop.
    /// </summary>
    private static byte[] TransparentThenPartialGif()
    {
        using var ms = new MemoryStream();
        ms.Write("GIF89a"u8);
        ms.WriteByte(2);
        ms.WriteByte(0);
        ms.WriteByte(1);
        ms.WriteByte(0);
        ms.WriteByte(0x81);
        ms.WriteByte(2);
        ms.WriteByte(0);
        ms.WriteByte(255);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(255);
        ms.WriteByte(255);
        ms.WriteByte(0);
        ms.WriteByte(255);
        ms.WriteByte(0);
        ms.WriteByte(0);
        ms.WriteByte(0);
        WriteGce(ms, delayCs: 8, transparent: 2, disposal: 1);
        WriteImage(ms, [0, 2], width: 2, height: 1);
        WriteGce(ms, delayCs: 12, transparent: 2, disposal: 1);
        WriteImage(ms, [1], left: 1, width: 1, height: 1);
        ms.WriteByte(0x3B);
        return ms.ToArray();
    }

    private static void WriteGce(Stream stream, int delayCs, int transparent = -1, int disposal = 0)
    {
        stream.WriteByte(0x21);
        stream.WriteByte(0xF9);
        stream.WriteByte(4);
        stream.WriteByte((byte)((disposal << 2) | (transparent >= 0 ? 1 : 0)));
        stream.WriteByte((byte)delayCs);
        stream.WriteByte((byte)(delayCs >> 8));
        stream.WriteByte((byte)(transparent >= 0 ? transparent : 0));
        stream.WriteByte(0);
    }

    private static void WriteImage(
        Stream stream,
        byte[] indices,
        int left = 0,
        int top = 0,
        int width = 2,
        int height = 1)
    {
        stream.WriteByte(0x2C);
        stream.WriteByte((byte)left);
        stream.WriteByte((byte)(left >> 8));
        stream.WriteByte((byte)top);
        stream.WriteByte((byte)(top >> 8));
        stream.WriteByte((byte)width);
        stream.WriteByte((byte)(width >> 8));
        stream.WriteByte((byte)height);
        stream.WriteByte((byte)(height >> 8));
        stream.WriteByte(0);
        stream.WriteByte(2);
        var lzw = EncodeLzw(indices, minCodeSize: 2);
        var offset = 0;
        while (offset < lzw.Length)
        {
            var n = Math.Min(255, lzw.Length - offset);
            stream.WriteByte((byte)n);
            stream.Write(lzw, offset, n);
            offset += n;
        }

        stream.WriteByte(0);
    }

    private static byte[] EncodeLzw(byte[] indices, int minCodeSize)
    {
        var clear = 1 << minCodeSize;
        var eoi = clear + 1;
        var codeSize = minCodeSize + 1;
        var bits = new List<int>();
        void Emit(int code, int size)
        {
            for (var i = 0; i < size; i++)
            {
                bits.Add((code >> i) & 1);
            }
        }

        Emit(clear, codeSize);
        foreach (var index in indices)
        {
            Emit(index, codeSize);
        }

        Emit(eoi, codeSize);
        var bytes = new byte[(bits.Count + 7) / 8];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i] != 0)
            {
                bytes[i >> 3] |= (byte)(1 << (i & 7));
            }
        }

        return bytes;
    }
}
