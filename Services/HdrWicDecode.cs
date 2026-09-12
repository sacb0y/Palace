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

            return ToScrgb(pixels.Value.Data, pixels.Value.Float16, width, height, probe);
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

    private static async Task<(byte[] Data, bool Float16)?> TryPixelsAsync(BitmapDecoder decoder)
    {
        foreach (var format in new[] { BitmapPixelFormat.Rgba16, BitmapPixelFormat.Rgba8, BitmapPixelFormat.Bgra8 })
        {
            try
            {
                var data = await decoder.GetPixelDataAsync(
                    format,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage);
                return (data.DetachPixelData(), false);
            }
            catch
            {
                // Try the next pixel format.
            }
        }

        return null;
    }

    private static HdrFrame ToScrgb(byte[] data, bool float16, int width, int height, HdrProbe probe)
    {
        var count = width * height;
        var rgba = new float[count * 4];
        var bpp = data.Length >= count * 8 ? 8 : 4;
        var bgra = bpp == 4 && data.Length >= count * 4;
        var maxNits = 0f;
        var pq = probe.IsPq || (probe.Kind == HdrKind.HdrPng && probe.CicpTransfer is null);
        var hlg = probe.IsHlg;
        var bt2020 = probe.CicpPrimaries == 9;

        for (var i = 0; i < count; i++)
        {
            float r;
            float g;
            float b;
            float a;
            if (bpp == 8)
            {
                var o = i * 8;
                r = ReadU16(data, o) / 65535f;
                g = ReadU16(data, o + 2) / 65535f;
                b = ReadU16(data, o + 4) / 65535f;
                a = ReadU16(data, o + 6) / 65535f;
            }
            else
            {
                var o = i * 4;
                if (bgra)
                {
                    b = data[o] / 255f;
                    g = data[o + 1] / 255f;
                    r = data[o + 2] / 255f;
                    a = data[o + 3] / 255f;
                }
                else
                {
                    r = data[o] / 255f;
                    g = data[o + 1] / 255f;
                    b = data[o + 2] / 255f;
                    a = data[o + 3] / 255f;
                }
            }

            _ = float16;
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

    private static int ReadU16(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8);
}
