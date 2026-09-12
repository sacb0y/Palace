using Palace.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Palace.Services;

internal sealed class HdrFrame
{
    public required float[] ScrgbRgba { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int NativeWidth { get; init; }
    public required int NativeHeight { get; init; }
    public required float MaxNits { get; init; }
    public required float AvgNits { get; init; }
    public required float MinNits { get; init; }
    public required float MaxScrgb { get; init; }

    public bool HasStats => MaxNits > 0;
}

internal readonly record struct HdrStats(
    float MaxNits,
    float AvgNits,
    float MinNits,
    float MaxScrgb,
    int Width,
    int Height);

/// <summary>
/// WIC decode of a local HDR still (PNG / AVIF) into linear scRGB. Call only
/// after <see cref="GalleryPresent.ShouldAttemptHdrPresent"/> (never online-only).
/// First paint decodes at the viewport; native stats can run after present.
/// </summary>
internal static class HdrWicDecode
{
    public static Task<HdrFrame?> TryLoadAsync(
        string path,
        HdrProbe probe,
        CancellationToken cancellation) =>
        TryLoadAsync(path, probe, 0, 0, ImageScaling.Fit, cancellation);

    public static async Task<HdrFrame?> TryLoadAsync(
        string path,
        HdrProbe probe,
        int viewportPixelWidth,
        int viewportPixelHeight,
        ImageScaling scaling,
        CancellationToken cancellation)
    {
        if (!CloudFile.Exists(path) || CloudFile.IsOnlineOnly(path))
        {
            return null;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            using var stream = await file.OpenAsync(FileAccessMode.Read).AsTask().ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
            var nativeW = (int)decoder.OrientedPixelWidth;
            var nativeH = (int)decoder.OrientedPixelHeight;
            if (nativeW <= 0 || nativeH <= 0 || nativeW > 16384 || nativeH > 16384)
            {
                return null;
            }

            var (decodeW, decodeH) = GalleryPresent.PresentDecodeSize(
                nativeW, nativeH, viewportPixelWidth, viewportPixelHeight, scaling);
            if (decodeW <= 0 || decodeH <= 0)
            {
                decodeW = nativeW;
                decodeH = nativeH;
            }

            var pixels = await TryPixelsAsync(decoder, decodeW, decodeH, cancellation).ConfigureAwait(false);
            if (pixels is null)
            {
                return null;
            }

            cancellation.ThrowIfCancellationRequested();
            var packed = pixels.Value;
            return await Task.Run(
                () => ToScrgb(packed.Data, packed.Format, decodeW, decodeH, nativeW, nativeH, probe),
                cancellation).ConfigureAwait(false);
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

    public static async Task<HdrStats?> TryMeasureAsync(string path, HdrProbe probe, CancellationToken cancellation)
    {
        if (!CloudFile.Exists(path) || CloudFile.IsOnlineOnly(path))
        {
            return null;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            using var stream = await file.OpenAsync(FileAccessMode.Read).AsTask().ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
            var nativeW = (int)decoder.OrientedPixelWidth;
            var nativeH = (int)decoder.OrientedPixelHeight;
            if (nativeW <= 0 || nativeH <= 0 || nativeW > 16384 || nativeH > 16384)
            {
                return null;
            }

            var pixels = await TryPixelsAsync(decoder, nativeW, nativeH, cancellation).ConfigureAwait(false);
            if (pixels is null)
            {
                return null;
            }

            cancellation.ThrowIfCancellationRequested();
            var packed = pixels.Value;
            return await Task.Run(
                () => Measure(packed.Data, packed.Format, nativeW, nativeH, probe, cancellation),
                cancellation).ConfigureAwait(false);
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

    private static async Task<(byte[] Data, HdrPackedFormat Format)?> TryPixelsAsync(
        BitmapDecoder decoder,
        int width,
        int height,
        CancellationToken cancellation)
    {
        var transform = new BitmapTransform();
        var sourceW = (int)decoder.PixelWidth;
        var sourceH = (int)decoder.PixelHeight;
        var orientedW = (int)decoder.OrientedPixelWidth;
        var orientedH = (int)decoder.OrientedPixelHeight;
        var (scaleW, scaleH) = HdrPixels.SourceScaleSize(
            sourceW, sourceH, orientedW, orientedH, width, height);
        if (scaleW > 0 && scaleH > 0 && (scaleW < sourceW || scaleH < sourceH))
        {
            transform.ScaledWidth = (uint)scaleW;
            transform.ScaledHeight = (uint)scaleH;
            transform.InterpolationMode = BitmapInterpolationMode.Linear;
        }

        foreach (var (wic, packed) in new[]
        {
            (BitmapPixelFormat.Rgba16, HdrPackedFormat.Rgba16),
            (BitmapPixelFormat.Rgba8, HdrPackedFormat.Rgba8),
            (BitmapPixelFormat.Bgra8, HdrPackedFormat.Bgra8)
        })
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var data = await decoder.GetPixelDataAsync(
                    wic,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage).AsTask().ConfigureAwait(false);
                return (data.DetachPixelData(), packed);
            }
            catch
            {
                // Try the next pixel format.
            }
        }

        return null;
    }

    private static HdrFrame? ToScrgb(
        byte[] data,
        HdrPackedFormat format,
        int width,
        int height,
        int nativeWidth,
        int nativeHeight,
        HdrProbe probe)
    {
        var count = width * height;
        if (count <= 0 || data.Length < count * HdrPixels.BytesPerPixel(format))
        {
            return null;
        }

        var rgba = new float[count * 4];
        var maxY = 0f;
        var minY = float.MaxValue;
        var sumY = 0.0;
        var maxScrgb = 0f;
        var transfer = probe.Transfer;
        var bt2020 = probe.CicpPrimaries == 9;
        for (var i = 0; i < count; i++)
        {
            HdrPixels.Read(data, i, format, out var r, out var g, out var b, out var a);
            var nitsR = GalleryPresent.EncodedToNits(r, transfer);
            var nitsG = GalleryPresent.EncodedToNits(g, transfer);
            var nitsB = GalleryPresent.EncodedToNits(b, transfer);
            var y = GalleryPresent.LuminanceY(nitsR, nitsG, nitsB, bt2020);
            maxY = Math.Max(maxY, y);
            minY = Math.Min(minY, y);
            sumY += y;
            if (bt2020)
            {
                GalleryPresent.Bt2020ToBt709(nitsR, nitsG, nitsB, out nitsR, out nitsG, out nitsB);
            }

            var sr = GalleryPresent.NitsToScrgb(nitsR);
            var sg = GalleryPresent.NitsToScrgb(nitsG);
            var sb = GalleryPresent.NitsToScrgb(nitsB);
            maxScrgb = Math.Max(maxScrgb, Math.Max(sr, Math.Max(sg, sb)));
            var oOut = i * 4;
            rgba[oOut] = sr;
            rgba[oOut + 1] = sg;
            rgba[oOut + 2] = sb;
            rgba[oOut + 3] = a;
        }

        if (maxY <= 0)
        {
            maxY = 0;
            minY = 0;
        }
        else if (minY == float.MaxValue)
        {
            minY = 0;
        }

        return new HdrFrame
        {
            ScrgbRgba = rgba,
            Width = width,
            Height = height,
            NativeWidth = nativeWidth,
            NativeHeight = nativeHeight,
            MaxNits = maxY,
            AvgNits = (float)(sumY / count),
            MinNits = minY,
            MaxScrgb = maxScrgb
        };
    }

    private static HdrStats? Measure(
        byte[] data,
        HdrPackedFormat format,
        int width,
        int height,
        HdrProbe probe,
        CancellationToken cancellation)
    {
        var count = width * height;
        if (count <= 0 || data.Length < count * HdrPixels.BytesPerPixel(format))
        {
            return null;
        }

        var maxY = 0f;
        var minY = float.MaxValue;
        var sumY = 0.0;
        var maxScrgb = 0f;
        var transfer = probe.Transfer;
        var bt2020 = probe.CicpPrimaries == 9;
        for (var i = 0; i < count; i++)
        {
            if ((i & 0x3FFF) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            HdrPixels.Read(data, i, format, out var r, out var g, out var b, out _);
            var nitsR = GalleryPresent.EncodedToNits(r, transfer);
            var nitsG = GalleryPresent.EncodedToNits(g, transfer);
            var nitsB = GalleryPresent.EncodedToNits(b, transfer);
            var y = GalleryPresent.LuminanceY(nitsR, nitsG, nitsB, bt2020);
            maxY = Math.Max(maxY, y);
            minY = Math.Min(minY, y);
            sumY += y;
            if (bt2020)
            {
                GalleryPresent.Bt2020ToBt709(nitsR, nitsG, nitsB, out nitsR, out nitsG, out nitsB);
            }

            maxScrgb = Math.Max(
                maxScrgb,
                Math.Max(
                    GalleryPresent.NitsToScrgb(nitsR),
                    Math.Max(GalleryPresent.NitsToScrgb(nitsG), GalleryPresent.NitsToScrgb(nitsB))));
        }

        if (maxY <= 0)
        {
            maxY = 0;
            minY = 0;
        }
        else if (minY == float.MaxValue)
        {
            minY = 0;
        }

        return new HdrStats(maxY, (float)(sumY / count), minY, maxScrgb, width, height);
    }
}
