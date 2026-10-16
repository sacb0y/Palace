using Palace.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace Palace.Services;

public readonly record struct ThumbnailInfo(string Path, int Width, int Height);

public sealed class ThumbnailService
{
    public const int MaxSide = 512;
    public const string LargePreviewSuffix = "_lg";

    private string _root;

    public ThumbnailService(string thumbsRoot)
    {
        _root = thumbsRoot;
        Directory.CreateDirectory(_root);
    }

    /// <summary>Active JPEG folder under the media cache root (<c>…/thumbs</c>).</summary>
    public string Root => _root;

    public void SetRoot(string thumbsRoot)
    {
        _root = thumbsRoot;
        Directory.CreateDirectory(_root);
    }

    public string PathForHash(string hash, string? suffix = null) =>
        Path.Combine(_root, ThumbFileName.FileName(hash, suffix));

    public string? ExistingPathForHash(string? hash, string? suffix = null)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return null;
        }

        var path = PathForHash(hash, suffix);
        return File.Exists(path) ? path : null;
    }

    public void TryDelete(string? hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return;
        }

        foreach (var path in new[] { PathForHash(hash), PathForHash(hash, LargePreviewSuffix) })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cache cleanup.
            }
        }
    }

    /// <summary>
    /// Ensures a mosaic JPEG exists under the Palace thumb cache — never
    /// beside the source. Online-only: cached JPEG only; do not
    /// <see cref="StorageFile.GetThumbnailAsync"/> or WIC-open the original
    /// (AVIF still recalls Dropbox). Local video / AVIF: provider thumb only.
    /// Local video / AVIF / HEIC / PSD: provider thumb only. Other local
    /// images may WIC-decode the original. Missing / API-only paths return
    /// a cached JPEG if present.
    /// </summary>
    public async Task<ThumbnailInfo?> EnsureThumbnailAsync(string filePath, string? hash, Models.AssetKind kind)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return null;
        }

        if (!CloudFile.TryGetAttributes(filePath, out var attrs))
        {
            return ReadCached(hash);
        }

        var onlineOnly = CloudFile.IsOnlineOnly(attrs);
        if (onlineOnly || kind == Models.AssetKind.Other)
        {
            return ReadCached(hash);
        }

        var dest = PathForHash(hash);
        if (File.Exists(dest) && !ShouldRegenerate(dest, filePath))
        {
            TouchLastAccess(dest);
            return ReadCached(hash) ?? new ThumbnailInfo(dest, 0, 0);
        }

        if (kind == Models.AssetKind.Image)
        {
            var custom = await TryCustomStillThumbAsync(filePath, dest, hash).ConfigureAwait(false);
            if (custom is not null)
            {
                return custom;
            }
        }

        if (GalleryMedia.UsesShellThumbnail(kind, filePath))
        {
            return await EnsureShellThumbnailAsync(filePath, hash).ConfigureAwait(false);
        }

        if (!GalleryMedia.MayOpenOriginalForThumb(onlineOnly, kind, filePath))
        {
            return ReadCached(hash);
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath).AsTask().ConfigureAwait(false);
            using var stream = await file.OpenAsync(FileAccessMode.Read).AsTask().ConfigureAwait(false);
            return await EncodeScaledJpegAsync(stream, dest, hash).ConfigureAwait(false);
        }
        catch
        {
            return ReadCached(hash);
        }
    }

    public async Task<ThumbnailInfo?> CacheJpegAsync(string hash, Stream jpegOrImage, string? suffix = null)
    {
        var dest = PathForHash(hash, suffix);
        try
        {
            Directory.CreateDirectory(_root);
            if (jpegOrImage.CanSeek)
            {
                jpegOrImage.Position = 0;
            }

            await using var output = File.Create(dest);
            await jpegOrImage.CopyToAsync(output).ConfigureAwait(false);
            TouchLastAccess(dest);
            EnforceCacheBudget();
            var written = ImageDimensions.TryRead(dest);
            return written is { } size
                ? new ThumbnailInfo(dest, size.Width, size.Height)
                : new ThumbnailInfo(dest, 0, 0);
        }
        catch
        {
            return ReadCached(hash, suffix);
        }
    }

    /// <summary>
    /// Decode any image stream, scale to <see cref="MaxSide"/>, and replace the
    /// mosaic JPEG for <paramref name="hash"/> under the thumb cache (never
    /// beside the source). Used for video-frame posters.
    /// </summary>
    public Task<ThumbnailInfo?> CacheScaledJpegAsync(string hash, IRandomAccessStream imageStream, string? suffix = null)
    {
        var dest = PathForHash(hash, suffix);
        return EncodeScaledJpegAsync(imageStream, dest, hash);
    }

    /// <summary>
    /// Windows shell / provider poster. Local video / AVIF / HEIC / PSD
    /// only — never the online-only path (HEIF/AVIF handlers open the original).
    /// </summary>
    private async Task<ThumbnailInfo?> EnsureShellThumbnailAsync(string filePath, string hash)
    {
        var cached = ReadCached(hash);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath).AsTask().ConfigureAwait(false);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, MaxSide).AsTask().ConfigureAwait(false);
            if (thumb is null || !GalleryMedia.AcceptsProviderThumbnail(thumb.Type == ThumbnailType.Image))
            {
                return null;
            }

            var dest = PathForHash(hash);
            return await EncodeScaledJpegAsync(thumb, dest, hash).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<ThumbnailInfo?> EncodeScaledJpegAsync(IRandomAccessStream stream, string dest, string hash, int maxSide = MaxSide)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var width = decoder.PixelWidth;
        var height = decoder.PixelHeight;
        if (width == 0 || height == 0)
        {
            return null;
        }

        var scale = Math.Min(maxSide / (double)width, maxSide / (double)height);
        scale = Math.Min(scale, 1.0);
        var tw = Math.Max(1u, (uint)Math.Round(width * scale));
        var th = Math.Max(1u, (uint)Math.Round(height * scale));
        var transform = new BitmapTransform
        {
            ScaledWidth = tw,
            ScaledHeight = th,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        var folder = await StorageFolder.GetFolderFromPathAsync(_root);
        var outFile = await folder.CreateFileAsync(Path.GetFileName(dest), CreationCollisionOption.ReplaceExisting);
        using var outStream = await outFile.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, tw, th, 96, 96, pixels.DetachPixelData());
        await encoder.FlushAsync();
        TouchLastAccess(dest);
        EnforceCacheBudget();
        var written = ImageDimensions.TryRead(dest);
        return written is { } size
            ? new ThumbnailInfo(dest, size.Width, size.Height)
            : new ThumbnailInfo(dest, (int)width, (int)height);
    }

    /// <summary>Side of the cached full-view JPEG for stills Windows cannot show.</summary>
    public const int LargePreviewMaxSide = 2048;

    /// <summary>
    /// Cached full-view JPEG for EXR / Radiance / TGA / Clip / non-image
    /// stills that <c>BitmapImage</c> cannot decode. Keyed by path, size and
    /// mtime so the gallery can resolve it without the content hash.
    /// </summary>
    public string? ExistingLargePreviewForPath(string? filePath)
    {
        var path = LargePreviewPathFor(filePath);
        return path is not null && File.Exists(path) ? path : null;
    }

    private string? LargePreviewPathFor(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(filePath);
            var key = $"{filePath.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
            return PathForHash("p" + digest, LargePreviewSuffix);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Custom still paths: EXR / Radiance / TGA / Clip decoders, HDR stills
    /// tone-mapped from scRGB (WinRT unorm decode shows HDR PNG/JXR/AVIF as
    /// clipped red), and a placeholder tile for files that are not images.
    /// Returns null to fall through to the WinRT / shell thumbnail.
    /// </summary>
    private async Task<ThumbnailInfo?> TryCustomStillThumbAsync(string filePath, string dest, string hash)
    {
        try
        {
            byte[] head;
            await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                head = new byte[64];
                var n = await fs.ReadAsync(head).ConfigureAwait(false);
                Array.Resize(ref head, n);
            }

            var ext = PathSafe.Extension(filePath);
            if (StillDecode.IsNotAnImage(head, ext))
            {
                var ph = StillDecode.Placeholder();
                await EncodeBgraJpegAsync(ph.Bgra!, ph.Width, ph.Height, dest).ConfigureAwait(false);
                await EncodeBgraJpegAsync(ph.Bgra!, ph.Width, ph.Height, LargePreviewPathFor(filePath)).ConfigureAwait(false);
                return Finish(dest, ph.Width, ph.Height);
            }

            if (StillDecode.HandlesPath(filePath))
            {
                var large = await Task.Run(() => StillDecode.TryDecodeFile(filePath, LargePreviewMaxSide)).ConfigureAwait(false);
                if (large is null)
                {
                    var ph = StillDecode.Placeholder();
                    await EncodeBgraJpegAsync(ph.Bgra!, ph.Width, ph.Height, dest).ConfigureAwait(false);
                    return Finish(dest, ph.Width, ph.Height);
                }

                if (large.EncodedImage is { } encoded)
                {
                    // Clip Studio canvas preview (PNG) — let WinRT decode it.
                    using var ras = new InMemoryRandomAccessStream();
                    await ras.WriteAsync(encoded.AsBuffer());
                    ras.Seek(0);
                    var lgPath = LargePreviewPathFor(filePath);
                    if (lgPath is not null)
                    {
                        await EncodeScaledJpegAsync(ras, lgPath, hash, LargePreviewMaxSide).ConfigureAwait(false);
                        ras.Seek(0);
                    }

                    return await EncodeScaledJpegAsync(ras, dest, hash).ConfigureAwait(false);
                }

                await EncodeBgraJpegAsync(large.Bgra!, large.Width, large.Height, LargePreviewPathFor(filePath)).ConfigureAwait(false);
                var (tw, th) = StillDecode.Fit(large.Width, large.Height, MaxSide);
                var small = tw == large.Width && th == large.Height
                    ? large.Bgra!
                    : StillDecode.BoxScaleBgra(large.Bgra!, large.Width, large.Height, tw, th);
                await EncodeBgraJpegAsync(small, tw, th, dest).ConfigureAwait(false);
                return Finish(dest, tw, th);
            }

            var probe = HdrFile.ProbePath(filePath);
            if (probe.IsHdr && probe.Kind != HdrKind.UltraHdrJpeg)
            {
                var frame = await HdrWicDecode.TryLoadAsync(filePath, probe, MaxSide, MaxSide, ImageScaling.Fit, CancellationToken.None)
                    .ConfigureAwait(false);
                if (frame is not null && frame.Width > 0 && frame.Height > 0)
                {
                    var r = await Task.Run(() => StillDecode.FromScrgb(frame.ScrgbRgba, frame.Width, frame.Height, MaxSide)).ConfigureAwait(false);
                    await EncodeBgraJpegAsync(r.Bgra!, r.Width, r.Height, dest).ConfigureAwait(false);
                    return Finish(dest, r.Width, r.Height);
                }
            }
        }
        catch
        {
            // Fall back to WinRT / shell thumbnail.
        }

        return null;
    }

    private ThumbnailInfo Finish(string dest, int w, int h)
    {
        TouchLastAccess(dest);
        EnforceCacheBudget();
        return new ThumbnailInfo(dest, w, h);
    }

    private async Task EncodeBgraJpegAsync(byte[] bgra, int width, int height, string? dest)
    {
        if (dest is null)
        {
            return;
        }

        // JPEG has no alpha: composite straight alpha over the dark tile colour.
        var flat = new byte[bgra.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            for (var c = 0; c < 3; c++)
            {
                flat[i + c] = (byte)((bgra[i + c] * a + 32 * (255 - a) + 127) / 255);
            }

            flat[i + 3] = 255;
        }

        Directory.CreateDirectory(_root);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(dest)!);
        var outFile = await folder.CreateFileAsync(Path.GetFileName(dest), CreationCollisionOption.ReplaceExisting);
        using var outStream = await outFile.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, flat);
        await encoder.FlushAsync();
    }

    private ThumbnailInfo? ReadCached(string hash, string? suffix = null)
    {
        var dest = PathForHash(hash, suffix);
        if (!File.Exists(dest))
        {
            return null;
        }

        TouchLastAccess(dest);
        var existing = ImageDimensions.TryRead(dest);
        return existing is { } size
            ? new ThumbnailInfo(dest, size.Width, size.Height)
            : new ThumbnailInfo(dest, 0, 0);
    }

    private void EnforceCacheBudget()
    {
        if (!MediaCache.Enabled)
        {
            return;
        }

        try
        {
            var cacheRoot = Directory.GetParent(_root)?.FullName;
            if (string.IsNullOrEmpty(cacheRoot))
            {
                return;
            }

            // Off UI thread: callers already await Encode/Cache on a pool or
            // continue from async; keep eviction out of the dispatcher.
            MediaCache.EnforceMaxSize(cacheRoot);
        }
        catch
        {
            // Budget enforcement is best-effort.
        }
    }

    private static void TouchLastAccess(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
            // Some volumes ignore LastAccessTime.
        }
    }

    private static bool ShouldRegenerate(string dest, string originalPath)
    {
        if (CloudFile.IsOnlineOnly(originalPath))
        {
            return false;
        }

        return GalleryMedia.ShouldRegenerateCachedThumb(ImageDimensions.TryRead(dest), MaxSide);
    }

    internal static string SanitizeHash(string hash) => ThumbFileName.Sanitize(hash);
}
