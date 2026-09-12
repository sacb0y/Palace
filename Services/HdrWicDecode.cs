using Palace.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Palace.Services;

internal sealed class HdrFrame
{
    public required float[] ScrgbRgba { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required float MaxNits { get; init; }
}

/// <summary>
/// WIC decode of a local HDR PNG into linear scRGB. Call only after
/// <see cref="GalleryPresent.ShouldAttemptHdrPresent"/> (never online-only).
/// </summary>
internal static class HdrWicDecode
{
    public static async Task<HdrFrame?> TryLoadAsync(string path, HdrProbe probe, CancellationToken cancellation)
    {
        if (!CloudFile.Exists(path) || CloudFile.IsOnlineOnly(path))
        {
            return null;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            cancellation.ThrowIfCancellationRequested();
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            cancellation.ThrowIfCancellationRequested();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var width = (int)decoder.PixelWidth;
            var height = (int)decoder.PixelHeight;
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            {
                return null;
            }

            var pixels = await TryPixelsAsync(decoder);
            if (pixels is null)
            {
                return null;
            }

            return ToScrgb(pixels.Value.Data, pixels.Value.Format, width, height, probe);
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

    private static async Task<(byte[] Data, HdrPackedFormat Format)?> TryPixelsAsync(BitmapDecoder decoder)
    {
        foreach (var (wic, packed) in new[]
        {
            (BitmapPixelFormat.Rgba16, HdrPackedFormat.Rgba16),
            (BitmapPixelFormat.Rgba8, HdrPackedFormat.Rgba8),
            (BitmapPixelFormat.Bgra8, HdrPackedFormat.Bgra8)
        })
        {
            try
            {
                var data = await decoder.GetPixelDataAsync(
                    wic,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage);
                return (data.DetachPixelData(), packed);
            }
            catch
            {
                // Try the next pixel format.
            }
        }

        return null;
    }

    private static HdrFrame? ToScrgb(byte[] data, HdrPackedFormat format, int width, int height, HdrProbe probe)
    {
        var count = width * height;
        if (data.Length < count * HdrPixels.BytesPerPixel(format))
        {
            return null;
        }

        var rgba = new float[count * 4];
        var maxNits = 0f;
        var pq = probe.IsPq || (probe.Kind == HdrKind.HdrPng && probe.CicpTransfer is null);
        var hlg = probe.IsHlg;
        var bt2020 = probe.CicpPrimaries == 9;

        for (var i = 0; i < count; i++)
        {
            HdrPixels.Read(data, i, format, out var r, out var g, out var b, out var a);
            float nitsR;
            float nitsG;
            float nitsB;
            if (pq)
            {
                nitsR = GalleryPresent.PqEotf(r) * 10000f;
                nitsG = GalleryPresent.PqEotf(g) * 10000f;
                nitsB = GalleryPresent.PqEotf(b) * 10000f;
            }
            else if (hlg)
            {
                nitsR = GalleryPresent.HlgEotf(r) * 1000f;
                nitsG = GalleryPresent.HlgEotf(g) * 1000f;
                nitsB = GalleryPresent.HlgEotf(b) * 1000f;
            }
            else
            {
                nitsR = r * GalleryPresent.SdrReferenceNits;
                nitsG = g * GalleryPresent.SdrReferenceNits;
                nitsB = b * GalleryPresent.SdrReferenceNits;
            }

            if (bt2020)
            {
                GalleryPresent.Bt2020ToBt709(nitsR, nitsG, nitsB, out nitsR, out nitsG, out nitsB);
            }

            maxNits = Math.Max(maxNits, Math.Max(nitsR, Math.Max(nitsG, nitsB)));
            var oOut = i * 4;
            rgba[oOut] = GalleryPresent.NitsToScrgb(nitsR);
            rgba[oOut + 1] = GalleryPresent.NitsToScrgb(nitsG);
            rgba[oOut + 2] = GalleryPresent.NitsToScrgb(nitsB);
            rgba[oOut + 3] = a;
        }

        if (probe.MaxCllNits is > 0)
        {
            maxNits = Math.Max(maxNits, probe.MaxCllNits.Value);
        }

        if (maxNits <= 0)
        {
            maxNits = GalleryPresent.SdrReferenceNits;
        }

        return new HdrFrame
        {
            ScrgbRgba = rgba,
            Width = width,
            Height = height,
            MaxNits = maxNits
        };
    }
}
