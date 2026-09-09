using Palace.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Palace.Services;

public readonly record struct ThumbnailInfo(string Path, int Width, int Height);

public sealed class ThumbnailService
{
    public const int MaxSide = 512;

    private readonly string _root;

    public ThumbnailService(string thumbsRoot)
    {
        _root = thumbsRoot;
        Directory.CreateDirectory(_root);
    }

    public string PathForHash(string hash) => Path.Combine(_root, $"{hash}.jpg");

    public void TryDelete(string? hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return;
        }

        var path = PathForHash(hash);
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

    public async Task<ThumbnailInfo?> EnsureThumbnailAsync(string filePath, string? hash, Models.AssetKind kind)
    {
        if (string.IsNullOrEmpty(hash) || kind == Models.AssetKind.Video || kind == Models.AssetKind.Other)
        {
            return null;
        }

        var dest = PathForHash(hash);
        if (File.Exists(dest) && !ShouldRegenerate(dest, filePath))
        {
            var existing = ImageDimensions.TryRead(dest);
            return existing is { } size
                ? new ThumbnailInfo(dest, size.Width, size.Height)
                : new ThumbnailInfo(dest, 0, 0);
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
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
            var outFile = await folder.CreateFileAsync($"{hash}.jpg", CreationCollisionOption.ReplaceExisting);
            using var outStream = await outFile.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, tw, th, 96, 96, pixels.DetachPixelData());
            await encoder.FlushAsync();
            var written = ImageDimensions.TryRead(dest);
            return written is { } size
                ? new ThumbnailInfo(dest, size.Width, size.Height)
                : new ThumbnailInfo(dest, (int)width, (int)height);
        }
        catch
        {
            if (!File.Exists(dest))
            {
                return null;
            }

            var existing = ImageDimensions.TryRead(dest);
            return existing is { } size
                ? new ThumbnailInfo(dest, size.Width, size.Height)
                : new ThumbnailInfo(dest, 0, 0);
        }
    }

    private static bool ShouldRegenerate(string dest, string originalPath)
    {
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
}
