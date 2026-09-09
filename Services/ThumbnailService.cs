using Palace.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Palace.Services;

public readonly record struct ThumbnailInfo(string Path, int Width, int Height);

public sealed class ThumbnailService
{
    public const int MaxSide = 512;
    public const string LargePreviewSuffix = "_lg";

    private readonly string _root;

    public ThumbnailService(string thumbsRoot)
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
    /// Ensures a mosaic JPEG exists. Online-only placeholders use
    /// <see cref="StorageFile.GetThumbnailAsync"/> and never decode the original.
    /// Missing local / API-only paths return a cached JPEG if present.
    /// </summary>
    public async Task<ThumbnailInfo?> EnsureThumbnailAsync(string filePath, string? hash, Models.AssetKind kind)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return null;
        }

        if (!File.Exists(filePath))
        {
            return ReadCached(hash);
        }

        if (CloudFile.IsOnlineOnly(filePath))
        {
            return await EnsureOnlineOnlyThumbnailAsync(filePath, hash, kind).ConfigureAwait(false);
        }

        if (kind == Models.AssetKind.Other)
        {
            return ReadCached(hash);
        }

        if (kind == Models.AssetKind.Video)
        {
            return await EnsureShellThumbnailAsync(filePath, hash).ConfigureAwait(false);
        }

        var dest = PathForHash(hash);
        if (File.Exists(dest) && !ShouldRegenerate(dest, filePath))
        {
            return ReadCached(hash) ?? new ThumbnailInfo(dest, 0, 0);
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
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

    private async Task<ThumbnailInfo?> EnsureOnlineOnlyThumbnailAsync(string filePath, string hash, Models.AssetKind kind)
    {
        var cached = ReadCached(hash);
        if (cached is not null)
        {
            return cached;
        }

        if (kind == Models.AssetKind.Other)
        {
            return null;
        }

        return await EnsureShellThumbnailAsync(filePath, hash).ConfigureAwait(false);
    }

    /// <summary>
    /// Windows shell / provider poster. Used for videos and online-only
    /// placeholders — never opens a decode stream on the original file.
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
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, MaxSide);
            if (thumb is null)
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

    private async Task<ThumbnailInfo?> EncodeScaledJpegAsync(IRandomAccessStream stream, string dest, string hash)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var width = decoder.PixelWidth;
        var height = decoder.PixelHeight;
        if (width == 0 || height == 0)
        {
            return null;
        }

        var scale = Math.Min(MaxSide / (double)width, MaxSide / (double)height);
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
        var written = ImageDimensions.TryRead(dest);
        return written is { } size
            ? new ThumbnailInfo(dest, size.Width, size.Height)
            : new ThumbnailInfo(dest, (int)width, (int)height);
    }

    private ThumbnailInfo? ReadCached(string hash, string? suffix = null)
    {
        var dest = PathForHash(hash, suffix);
        if (!File.Exists(dest))
        {
            return null;
        }

        var existing = ImageDimensions.TryRead(dest);
        return existing is { } size
            ? new ThumbnailInfo(dest, size.Width, size.Height)
            : new ThumbnailInfo(dest, 0, 0);
    }

    private static bool ShouldRegenerate(string dest, string originalPath)
    {
        if (CloudFile.IsOnlineOnly(originalPath))
        {
            return false;
        }

        var thumb = ImageDimensions.TryRead(dest);
        if (thumb is null)
        {
            return true;
        }

        var thumbMax = Math.Max(thumb.Value.Width, thumb.Value.Height);
        if (thumbMax >= MaxSide)
        {
            return false;
        }

        var original = ImageDimensions.TryRead(originalPath);
        if (original is null)
        {
            return false;
        }

        var originalMax = Math.Max(original.Value.Width, original.Value.Height);
        return originalMax > thumbMax;
    }

    internal static string SanitizeHash(string hash) => ThumbFileName.Sanitize(hash);
}
