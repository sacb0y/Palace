using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// GIF frame count / index math and local composite raster for overlay scrub.
/// Off WinUI. Never opens an On-Demand original.
/// </summary>
public static class GifFrames
{
    public const int MinScrubCount = 2;

    public static bool CanScrub(
        AssetKind kind,
        bool apiOnly,
        bool isOnlineOnly,
        bool localExists,
        string? path) =>
        kind == AssetKind.Gif
        && !apiOnly
        && !isOnlineOnly
        && localExists
        && PathSafe.GifExt.Contains(PathSafe.Extension(path));

    public static int ClampIndex(int index, int frameCount)
    {
        if (frameCount <= 0)
        {
            return 0;
        }

        return Math.Clamp(index, 0, frameCount - 1);
    }

    public static int Step(int index, int frameCount, int delta)
    {
        if (frameCount <= 0)
        {
            return 0;
        }

        var n = ClampIndex(index, frameCount) + delta;
        n %= frameCount;
        if (n < 0)
        {
            n += frameCount;
        }

        return n;
    }

    public static string PositionLabel(int index, int frameCount)
    {
        if (frameCount <= 0)
        {
            return "";
        }

        return $"{ClampIndex(index, frameCount) + 1} / {frameCount}";
    }

    public static int NextPlayIndex(int index, int frameCount) =>
        Step(index, frameCount, 1);

    /// <summary>
    /// GIF delay units are centiseconds. 0 and 1cs are treated as 10cs
    /// (common players / BitmapImage). Playback owns this clock so Pause
    /// keeps the visible frame instead of jumping to slider residue.
    /// </summary>
    public static int DisplayDelayMs(int delayCs)
    {
        var cs = delayCs < 2 ? 10 : delayCs;
        return cs * 10;
    }

    public static bool ShouldStopPlaybackOnIndexChange(bool isPlaybackTick) =>
        !isPlaybackTick;

    /// <summary>
    /// Overlay / GalleryWindow close and <c>Bind(null)</c> must cancel the
    /// delay clock. Decode cancel alone leaves <see cref="NotifyGifCompositeReady"/>
    /// ticks running.
    /// </summary>
    public static bool ShouldStopPlaybackOnUnbind(bool hadGallery) =>
        hadGallery;

    /// <summary>
    /// <see cref="TryRenderAll"/> failed after scrub chrome was shown: drop
    /// the timeline and keep <c>BitmapImage</c> autoplay.
    /// </summary>
    public static bool ShouldAbandonScrubOnFailedComposite(bool canScrub, bool hasCompositeFrames) =>
        canScrub && !hasCompositeFrames;

    public static bool ShouldAnimateGifFallback(bool abandonScrub) =>
        abandonScrub;

    public static bool ShouldShowScrub(bool canScrub, int frameCount) =>
        canScrub && frameCount >= MinScrubCount;

    /// <summary>
    /// Play ticks / Pause apply a cached raster. They must not start
    /// another <see cref="TryRenderAll"/> while the first load is in flight.
    /// </summary>
    public static bool ShouldApplyGifTick(bool canScrub, bool cacheReady) =>
        canScrub && cacheReady;

    public static bool ShouldStartGifCompositeLoad(
        bool cacheReady,
        bool loadInFlightForSamePath) =>
        !cacheReady && !loadInFlightForSamePath;

    /// <summary>
    /// Opening a scrubbable GIF must bump the HDR present epoch so a late
    /// WIC / scRGB result cannot paint over the still. That bump must not
    /// cancel an in-flight <see cref="TryRenderAll"/>.
    /// </summary>
    public static bool ShouldBumpHdrEpochOnGifStillRefresh(bool isScrubbableGif) =>
        isScrubbableGif;

    /// <summary>
    /// Applying a cached composite for the current path must cancel any
    /// in-flight decode for a previous GIF so it cannot replace rasters.
    /// </summary>
    public static bool ShouldCancelStaleGifLoad(bool cacheReadyForCurrentPath) =>
        cacheReadyForCurrentPath;

    /// <summary>
    /// The delay clock must not run until <see cref="TryRenderAll"/> has
    /// filled the overlay cache. Otherwise Pause/slider cannot freeze the
    /// still, and applying the cache jumps to an advanced index.
    /// </summary>
    public static bool ShouldRunGifPlayLoop(bool canScrub, bool playing, bool compositeReady) =>
        canScrub && playing && compositeReady;

    public const int KeyLeft = 37;
    public const int KeyRight = 39;

    /// <summary>
    /// Overlay / GalleryWindow PreviewKeyDown must not mark Left/Right
    /// handled while the GIF frame slider is focused, so the slider can
    /// nudge the frame instead of changing the asset.
    /// </summary>
    public static bool PassesGifSliderArrows(bool sliderFocused, int keyCode) =>
        sliderFocused && (keyCode == KeyLeft || keyCode == KeyRight);

    public static bool IsGifFrameSlider(string? automationId) =>
        automationId is "SldGifFrame" or "SldGalleryWindowGifFrame";

    /// <summary>
    /// Upper bound on the decoded composite cache (one full-canvas BGRA
    /// raster per frame). Larger GIFs skip scrub and keep BitmapImage autoplay.
    /// </summary>
    public const long MaxCacheBytes = 256L * 1024 * 1024;

    public const int MaxCacheFrames = 2048;

    public static bool FitsCacheBudget(int width, int height, int frameCount)
    {
        if (width <= 0 || height <= 0 || frameCount <= 0 || frameCount > MaxCacheFrames)
        {
            return false;
        }

        var stride = (long)width * 4;
        if (height > MaxCacheBytes / stride)
        {
            return false;
        }

        var frameBytes = stride * height;
        return frameBytes <= MaxCacheBytes / frameCount;
    }

    /// <summary>
    /// Image-descriptor <c>fw*fh</c> is independent of the logical screen.
    /// Cap the 1-byte LZW index buffer and Blit loops with the same 256 MB
    /// bound, overflow-safe (divide, not 32-bit <c>fw*fh</c>).
    /// </summary>
    public static bool FitsFramePixels(int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            return false;
        }

        return frameHeight <= MaxCacheBytes / frameWidth;
    }

    public readonly record struct Info(int Width, int Height, int FrameCount, IReadOnlyList<int> DelaysCs);

    public readonly record struct Raster(int Width, int Height, byte[] Bgra);

    public static Info? TryRead(string? path)
    {
        if (!MayOpen(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return TryRead(stream);
        }
        catch
        {
            return null;
        }
    }

    public static Info? TryRead(Stream stream)
    {
        try
        {
            var file = Parse(stream, renderIndex: null);
            return file is null
                ? null
                : new Info(file.Width, file.Height, file.FrameCount, file.DelaysCs);
        }
        catch
        {
            return null;
        }
    }

    public static Raster? TryRenderFrame(string? path, int index)
    {
        if (!MayOpen(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return TryRenderFrame(stream, index);
        }
        catch
        {
            return null;
        }
    }

    public static Raster? TryRenderFrame(Stream stream, int index)
    {
        try
        {
            var file = Parse(stream, index);
            if (file?.Composite is null)
            {
                return null;
            }

            return new Raster(file.Width, file.Height, file.Composite);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// One pass over the file — composites every frame. Overlay autoplay
    /// must use this cache instead of <see cref="TryRenderFrame"/> per tick.
    /// </summary>
    public static IReadOnlyList<Raster>? TryRenderAll(string? path, CancellationToken cancellation = default)
    {
        if (!MayOpen(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return TryRenderAll(stream, cancellation);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<Raster>? TryRenderAll(Stream stream, CancellationToken cancellation = default)
    {
        try
        {
            var file = Parse(stream, renderIndex: null, renderAll: true, cancellation);
            if (file?.Composites is null || file.Composites.Count == 0)
            {
                return null;
            }

            var frames = new Raster[file.Composites.Count];
            for (var i = 0; i < file.Composites.Count; i++)
            {
                frames[i] = new Raster(file.Width, file.Height, file.Composites[i]);
            }

            return frames;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public static Raster? PickCached(IReadOnlyList<Raster> frames, int index)
    {
        if (frames.Count == 0)
        {
            return null;
        }

        return frames[ClampIndex(index, frames.Count)];
    }

    public static bool CacheMatchesPath(string? cachePath, string? path) =>
        !string.IsNullOrEmpty(cachePath)
        && !string.IsNullOrEmpty(path)
        && string.Equals(cachePath, path, StringComparison.OrdinalIgnoreCase);

    private static bool MayOpen(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && CloudFile.Exists(path)
        && ScanContent.MayReadOriginal(path);

    private sealed class Parsed
    {
        public int Width;
        public int Height;
        public int FrameCount;
        public List<int> DelaysCs = [];
        public byte[]? Composite;
        public List<byte[]>? Composites;
    }

    private static Parsed? Parse(
        Stream stream,
        int? renderIndex,
        bool renderAll = false,
        CancellationToken cancellation = default)
    {
        if (!stream.CanSeek || stream.Length < 14)
        {
            return null;
        }

        stream.Position = 0;
        Span<byte> header = stackalloc byte[13];
        if (stream.Read(header) < 13)
        {
            return null;
        }

        if (header[0] != (byte)'G' || header[1] != (byte)'I' || header[2] != (byte)'F')
        {
            return null;
        }

        var width = header[6] | (header[7] << 8);
        var height = header[8] | (header[9] << 8);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var packed = header[10];
        var gctSize = (packed & 0x80) != 0 ? 1 << ((packed & 7) + 1) : 0;
        var gct = ReadPalette(stream, gctSize);

        // Transparent canvas — matches BitmapImage. Do not fill the LSD
        // background color (opaque backdrop on paused/scrubbed frames).
        var decode = renderAll || renderIndex is not null;
        if (decode && !FitsCacheBudget(width, height, 1))
        {
            return null;
        }

        var canvas = decode ? new byte[width * height * 4] : [];
        byte[]? previous = null;

        var disposal = 0;
        var delay = 10;
        var transparent = -1;
        var frameCount = 0;
        var delays = new List<int>();
        var want = renderIndex is null ? -1 : ClampIndex(renderIndex.Value, int.MaxValue);
        byte[]? composite = null;
        List<byte[]>? composites = renderAll ? [] : null;
        Span<byte> desc = stackalloc byte[9];

        while (stream.Position < stream.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            var intro = stream.ReadByte();
            if (intro < 0)
            {
                break;
            }

            if (intro == 0x3B)
            {
                break;
            }

            if (intro == 0x21)
            {
                var label = stream.ReadByte();
                if (label == 0xF9)
                {
                    var block = ReadSubBlockPayload(stream);
                    if (block.Length >= 4)
                    {
                        disposal = (block[0] >> 2) & 7;
                        delay = block[1] | (block[2] << 8);
                        transparent = (block[0] & 1) != 0 ? block[3] : -1;
                    }

                    continue;
                }

                SkipSubBlocks(stream);
                continue;
            }

            if (intro != 0x2C)
            {
                return frameCount > 0
                    ? Finish(width, height, frameCount, delays, composite, composites)
                    : null;
            }

            if (stream.Read(desc) < 9)
            {
                break;
            }

            var left = desc[0] | (desc[1] << 8);
            var top = desc[2] | (desc[3] << 8);
            var fw = desc[4] | (desc[5] << 8);
            var fh = desc[6] | (desc[7] << 8);
            var ip = desc[8];
            var lctSize = (ip & 0x80) != 0 ? 1 << ((ip & 7) + 1) : 0;
            var lct = ReadPalette(stream, lctSize);
            var palette = lct.Length >= 3 ? lct : gct;
            var interlace = (ip & 0x40) != 0;
            var minCode = stream.ReadByte();
            if (minCode < 0)
            {
                break;
            }

            if (!FitsFramePixels(fw, fh))
            {
                return null;
            }

            if (!decode)
            {
                SkipSubBlocks(stream);
            }
            else
            {
                cancellation.ThrowIfCancellationRequested();
                var indices = DecodeLzw(stream, minCode, fw * fh);
                if (disposal == 3)
                {
                    previous = (byte[])canvas.Clone();
                }

                if (indices is not null && fw > 0 && fh > 0)
                {
                    Blit(
                        canvas, width, height,
                        indices, fw, fh, left, top,
                        palette, transparent, interlace);
                }

                if (renderAll || frameCount == want)
                {
                    var copy = (byte[])canvas.Clone();
                    if (renderAll)
                    {
                        if (!FitsCacheBudget(width, height, composites!.Count + 1))
                        {
                            return null;
                        }

                        composites.Add(copy);
                    }
                    else
                    {
                        composite = copy;
                    }
                }

                ApplyDisposal(
                    canvas, width, height, left, top, fw, fh, disposal, previous);
            }

            delays.Add(delay <= 0 ? 10 : delay);
            frameCount++;
            disposal = 0;
            delay = 10;
            transparent = -1;

            if (!renderAll && renderIndex is not null && frameCount > want && composite is not null)
            {
                break;
            }
        }

        return frameCount > 0
            ? Finish(width, height, frameCount, delays, composite, composites)
            : null;
    }

    private static Parsed Finish(
        int width,
        int height,
        int frameCount,
        List<int> delays,
        byte[]? composite,
        List<byte[]>? composites) =>
        new()
        {
            Width = width,
            Height = height,
            FrameCount = frameCount,
            DelaysCs = delays,
            Composite = composite,
            Composites = composites
        };

    private static byte[] ReadPalette(Stream stream, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var bytes = new byte[count * 3];
        var n = stream.Read(bytes);
        if (n < bytes.Length)
        {
            Array.Resize(ref bytes, Math.Max(0, n));
        }

        return bytes;
    }

    private static uint PaletteColor(byte[] palette, int index, byte alpha)
    {
        var o = index * 3;
        if ((uint)o + 2 >= (uint)palette.Length)
        {
            return (uint)(alpha << 24);
        }

        return (uint)(alpha << 24 | palette[o] << 16 | palette[o + 1] << 8 | palette[o + 2]);
    }

    private static byte[] ReadSubBlockPayload(Stream stream)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            var len = stream.ReadByte();
            if (len <= 0)
            {
                break;
            }

            var buf = new byte[len];
            var n = stream.Read(buf);
            if (n > 0)
            {
                ms.Write(buf, 0, n);
            }

            if (n < len)
            {
                break;
            }
        }

        return ms.ToArray();
    }

    private static void SkipSubBlocks(Stream stream)
    {
        while (true)
        {
            var len = stream.ReadByte();
            if (len <= 0)
            {
                return;
            }

            stream.Seek(len, SeekOrigin.Current);
        }
    }

    private static byte[]? DecodeLzw(Stream stream, int minCodeSize, int pixelCount)
    {
        if (minCodeSize < 2 || minCodeSize > 8)
        {
            SkipSubBlocks(stream);
            return null;
        }

        var data = ReadSubBlockPayload(stream);
        if (data.Length == 0 || pixelCount <= 0)
        {
            return pixelCount <= 0 ? [] : null;
        }

        var clear = 1 << minCodeSize;
        var eoi = clear + 1;
        var codeSize = minCodeSize + 1;
        var next = eoi + 1;
        var prefixes = new int[4096];
        var suffixes = new byte[4096];
        var stack = new byte[4096];
        for (var i = 0; i < clear; i++)
        {
            prefixes[i] = -1;
            suffixes[i] = (byte)i;
        }

        var output = new byte[pixelCount];
        var written = 0;
        var bitPos = 0;
        var bitLen = data.Length * 8;
        var prev = -1;

        int ReadCode()
        {
            if (bitPos + codeSize > bitLen)
            {
                return -1;
            }

            var code = 0;
            for (var i = 0; i < codeSize; i++)
            {
                var byteIndex = bitPos >> 3;
                var bit = (data[byteIndex] >> (bitPos & 7)) & 1;
                code |= bit << i;
                bitPos++;
            }

            return code;
        }

        while (written < pixelCount)
        {
            var code = ReadCode();
            if (code < 0 || code == eoi)
            {
                break;
            }

            if (code == clear)
            {
                codeSize = minCodeSize + 1;
                next = eoi + 1;
                prev = -1;
                continue;
            }

            var extract = code;
            if (code == next && prev >= 0)
            {
                extract = prev;
            }
            else if (code > next)
            {
                break;
            }

            var sp = 0;
            var cur = extract;
            while (cur >= clear)
            {
                if (sp >= stack.Length)
                {
                    break;
                }

                stack[sp++] = suffixes[cur];
                cur = prefixes[cur];
                if (cur < 0)
                {
                    break;
                }
            }

            if (cur < 0)
            {
                break;
            }

            var first = suffixes[cur];
            if (written < pixelCount)
            {
                output[written++] = first;
            }

            while (sp > 0 && written < pixelCount)
            {
                output[written++] = stack[--sp];
            }

            if (code == next && written < pixelCount)
            {
                output[written++] = first;
            }

            if (prev >= 0 && next < 4096)
            {
                prefixes[next] = prev;
                suffixes[next] = first;
                next++;
                if (next == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                }
            }

            prev = code;
        }

        return output;
    }

    private static void Blit(
        byte[] canvas,
        int canvasW,
        int canvasH,
        byte[] indices,
        int fw,
        int fh,
        int left,
        int top,
        byte[] palette,
        int transparent,
        bool interlace)
    {
        var i = 0;
        void Put(int x, int y, byte index)
        {
            if ((uint)index == (uint)transparent)
            {
                return;
            }

            var dx = left + x;
            var dy = top + y;
            if ((uint)dx >= (uint)canvasW || (uint)dy >= (uint)canvasH)
            {
                return;
            }

            var color = PaletteColor(palette, index, 255);
            var o = (dy * canvasW + dx) * 4;
            canvas[o] = (byte)color;
            canvas[o + 1] = (byte)(color >> 8);
            canvas[o + 2] = (byte)(color >> 16);
            canvas[o + 3] = (byte)(color >> 24);
        }

        if (!interlace)
        {
            for (var y = 0; y < fh; y++)
            {
                for (var x = 0; x < fw; x++)
                {
                    if (i >= indices.Length)
                    {
                        return;
                    }

                    Put(x, y, indices[i++]);
                }
            }

            return;
        }

        var passes = new (int Start, int Step)[] { (0, 8), (4, 8), (2, 4), (1, 2) };
        foreach (var (start, step) in passes)
        {
            for (var y = start; y < fh; y += step)
            {
                for (var x = 0; x < fw; x++)
                {
                    if (i >= indices.Length)
                    {
                        return;
                    }

                    Put(x, y, indices[i++]);
                }
            }
        }
    }

    private static void ApplyDisposal(
        byte[] canvas,
        int canvasW,
        int canvasH,
        int left,
        int top,
        int fw,
        int fh,
        int disposal,
        byte[]? previous)
    {
        if (disposal == 3 && previous is not null)
        {
            Buffer.BlockCopy(previous, 0, canvas, 0, canvas.Length);
            return;
        }

        if (disposal == 2)
        {
            FillRect(canvas, canvasW, canvasH, left, top, fw, fh, 0);
        }
    }

    private static void FillRect(
        byte[] canvas, int canvasW, int canvasH, int left, int top, int fw, int fh, uint bg)
    {
        var b = (byte)bg;
        var g = (byte)(bg >> 8);
        var r = (byte)(bg >> 16);
        var a = (byte)(bg >> 24);
        var x1 = Math.Max(0, left);
        var y1 = Math.Max(0, top);
        var x2 = Math.Min(canvasW, left + fw);
        var y2 = Math.Min(canvasH, top + fh);
        for (var y = y1; y < y2; y++)
        {
            var o = (y * canvasW + x1) * 4;
            for (var x = x1; x < x2; x++)
            {
                canvas[o++] = b;
                canvas[o++] = g;
                canvas[o++] = r;
                canvas[o++] = a;
            }
        }
    }
}
