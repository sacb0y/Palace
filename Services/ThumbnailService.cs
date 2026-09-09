using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Palace.Services;

public sealed class ThumbnailService
{
    private readonly string _root;

    public ThumbnailService(string thumbsRoot)
    {
        _root = thumbsRoot;
        Directory.CreateDirectory(_root);
    }

    public string PathForHash(string hash) => Path.Combine(_root, $"{hash}.jpg");

    public async Task<string?> EnsureThumbnailAsync(string filePath, string? hash, Models.AssetKind kind)
    {
        if (string.IsNullOrEmpty(hash) || kind == Models.AssetKind.Video || kind == Models.AssetKind.Other)
        {
            return null;
        }

        var dest = PathForHash(hash);
        if (File.Exists(dest))
        {
            return dest;
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

            const uint max = 256;
            var scale = Math.Min(max / (double)width, max / (double)height);
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
            return dest;
        }
        catch
        {
            return File.Exists(dest) ? dest : null;
        }
    }
}
