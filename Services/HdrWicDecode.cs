using System.Runtime.InteropServices;
using Palace.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;
using WinRT;

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
/// Local HDR still → linear scRGB. AVIF/HEIF uses WIC P010/NV12 + CICP
/// YUV→RGB (SKIV’s libavif step). Never online-only.
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

        if (probe.Kind == HdrKind.HdrRadiance || PathSafe.IsRadiance(path))
        {
            return await Task.Run(() => FromRadiance(path), cancellation).ConfigureAwait(false);
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

            var pixels = await TryPixelsAsync(
                decoder, decodeW, decodeH, HdrColor.NeedsYuvConvert(probe.Kind), cancellation).ConfigureAwait(false);
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

        if (probe.Kind == HdrKind.HdrRadiance || PathSafe.IsRadiance(path))
        {
            return await Task.Run(
                () =>
                {
                    var frame = FromRadiance(path);
                    return frame is null
                        ? null
                        : new HdrStats(
                            frame.MaxNits,
                            frame.AvgNits,
                            frame.MinNits,
                            frame.MaxScrgb,
                            frame.Width,
                            frame.Height);
                },
                cancellation).ConfigureAwait(false);
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

            var pixels = await TryPixelsAsync(
                decoder, nativeW, nativeH, HdrColor.NeedsYuvConvert(probe.Kind), cancellation).ConfigureAwait(false);
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

    private static HdrFrame? FromRadiance(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var frame = RadianceFile.TryDecode(stream);
            if (frame is null)
            {
                return null;
            }

            return new HdrFrame
            {
                ScrgbRgba = frame.ScrgbRgba,
                Width = frame.Width,
                Height = frame.Height,
                NativeWidth = frame.Width,
                NativeHeight = frame.Height,
                MaxNits = frame.MaxNits,
                AvgNits = frame.AvgNits,
                MinNits = frame.MinNits,
                MaxScrgb = frame.MaxScrgb
            };
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
        bool preferYuv,
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

        if (preferYuv)
        {
            foreach (var (wic, packed) in new[]
            {
                (BitmapPixelFormat.P010, HdrPackedFormat.P010),
                (BitmapPixelFormat.Nv12, HdrPackedFormat.Nv12)
            })
            {
                cancellation.ThrowIfCancellationRequested();
                var yuv = await TryYuvAsync(decoder, wic, packed, transform, width, height).ConfigureAwait(false);
                if (yuv is not null)
                {
                    return yuv;
                }
            }
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

    private static async Task<(byte[] Data, HdrPackedFormat Format)?> TryYuvAsync(
        BitmapDecoder decoder,
        BitmapPixelFormat wic,
        HdrPackedFormat packed,
        BitmapTransform transform,
        int width,
        int height)
    {
        try
        {
            var data = await decoder.GetPixelDataAsync(
                wic,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask().ConfigureAwait(false);
            var bytes = data.DetachPixelData();
            if (HdrPixels.HasPackedData(bytes, packed, width, height))
            {
                return (bytes, packed);
            }
        }
        catch
        {
            // SoftwareBitmap planes next.
        }

        try
        {
            using var bitmap = await decoder.GetSoftwareBitmapAsync(
                wic,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask().ConfigureAwait(false);
            var tight = CopyYuvPlanes(bitmap, packed);
            if (tight is not null && HdrPixels.HasPackedData(tight, packed, width, height))
            {
                return (tight, packed);
            }
        }
        catch
        {
            // Caller tries the next YUV / RGB format.
        }

        return null;
    }

    private static byte[]? CopyYuvPlanes(SoftwareBitmap bitmap, HdrPackedFormat format)
    {
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        if (buffer.GetPlaneCount() < 2)
        {
            return null;
        }

        var yDesc = buffer.GetPlaneDescription(0);
        var uvDesc = buffer.GetPlaneDescription(1);
        using var reference = buffer.CreateReference();
        var access = reference.As<IMemoryBufferByteAccess>();
        access.GetBuffer(out var ptr, out var capacity);
        if (ptr == IntPtr.Zero || capacity == 0)
        {
            return null;
        }

        var raw = new byte[(int)capacity];
        Marshal.Copy(ptr, raw, 0, raw.Length);
        var yStride = format == HdrPackedFormat.P010 ? yDesc.Width * 2 : yDesc.Width;
        var uvStride = format == HdrPackedFormat.P010 ? uvDesc.Width * 2 : uvDesc.Width;
        var yBytes = yStride * yDesc.Height;
        var uvBytes = uvStride * uvDesc.Height;
        var dest = new byte[yBytes + uvBytes];
        for (var row = 0; row < yDesc.Height; row++)
        {
            var src = yDesc.StartIndex + (row * yDesc.Stride);
            Buffer.BlockCopy(raw, src, dest, row * yStride, yStride);
        }

        for (var row = 0; row < uvDesc.Height; row++)
        {
            var src = uvDesc.StartIndex + (row * uvDesc.Stride);
            Buffer.BlockCopy(raw, src, dest, yBytes + (row * uvStride), uvStride);
        }

        return dest;
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
        var converted = Convert(data, format, width, height, probe, wantRgba: true, default);
        if (converted is null)
        {
            return null;
        }

        return new HdrFrame
        {
            ScrgbRgba = converted.Value.Rgba!,
            Width = width,
            Height = height,
            NativeWidth = nativeWidth,
            NativeHeight = nativeHeight,
            MaxNits = converted.Value.MaxNits,
            AvgNits = converted.Value.AvgNits,
            MinNits = converted.Value.MinNits,
            MaxScrgb = converted.Value.MaxScrgb
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
        var converted = Convert(data, format, width, height, probe, wantRgba: false, cancellation);
        return converted is null
            ? null
            : new HdrStats(
                converted.Value.MaxNits,
                converted.Value.AvgNits,
                converted.Value.MinNits,
                converted.Value.MaxScrgb,
                width,
                height);
    }

    private static (float[]? Rgba, float MaxNits, float AvgNits, float MinNits, float MaxScrgb)? Convert(
        byte[] data,
        HdrPackedFormat format,
        int width,
        int height,
        HdrProbe probe,
        bool wantRgba,
        CancellationToken cancellation)
    {
        var count = width * height;
        if (count <= 0 || !HdrPixels.HasPackedData(data, format, width, height))
        {
            return null;
        }

        var yuv = HdrPixels.IsYuv(format);
        if (!yuv && HdrColor.NeedsYuvConvert(probe.Kind) && IsRgbLumaOnly(data, format, count))
        {
            // Y in R, G=B=0 — chroma is gone. Do not present Isiac’s red tint.
            return null;
        }

        var rgba = wantRgba ? new float[count * 4] : null;
        var maxY = 0f;
        var minY = float.MaxValue;
        var sumY = 0.0;
        var maxScrgb = 0f;
        for (var i = 0; i < count; i++)
        {
            if (!wantRgba && (i & 0x3FFF) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            float sr;
            float sg;
            float sb;
            float a;
            float nitsY;
            if (yuv)
            {
                var x = i % width;
                var y = i / width;
                HdrPixels.ReadYuv(data, width, height, x, y, format, out var luma, out var u, out var v);
                HdrColor.YuvEncodedToScrgb(luma, u, v, probe, out sr, out sg, out sb);
                a = 1f;
                var nitsR = sr * GalleryPresent.ScrgbNits;
                var nitsG = sg * GalleryPresent.ScrgbNits;
                var nitsB = sb * GalleryPresent.ScrgbNits;
                nitsY = GalleryPresent.LuminanceY(nitsR, nitsG, nitsB, probe.CicpPrimaries == 9);
            }
            else
            {
                HdrPixels.Read(data, i, format, out var r, out var g, out var b, out a);
                EncodedRgbToPresent(r, g, b, probe, out sr, out sg, out sb, out nitsY);
            }

            maxY = Math.Max(maxY, nitsY);
            minY = Math.Min(minY, nitsY);
            sumY += nitsY;
            maxScrgb = Math.Max(maxScrgb, Math.Max(sr, Math.Max(sg, sb)));
            if (rgba is not null)
            {
                var o = i * 4;
                rgba[o] = sr;
                rgba[o + 1] = sg;
                rgba[o + 2] = sb;
                rgba[o + 3] = a;
            }
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

        return (rgba, maxY, (float)(sumY / count), minY, maxScrgb);
    }

    /// <summary>
    /// PNG path stays EncodedToNits + BT.2020→709 (Isiac: HDR PNGs look
    /// right). AVIF/JXL/other use SKIV EncodedRgbToScrgb.
    /// </summary>
    private static void EncodedRgbToPresent(
        float r,
        float g,
        float b,
        HdrProbe probe,
        out float sr,
        out float sg,
        out float sb,
        out float nitsY)
    {
        var transfer = probe.Transfer;
        if (probe.Kind is HdrKind.HdrPng or HdrKind.WideGamutPng)
        {
            var nitsR = GalleryPresent.EncodedToNits(r, transfer);
            var nitsG = GalleryPresent.EncodedToNits(g, transfer);
            var nitsB = GalleryPresent.EncodedToNits(b, transfer);
            var bt2020 = probe.CicpPrimaries == 9;
            nitsY = GalleryPresent.LuminanceY(nitsR, nitsG, nitsB, bt2020);
            if (bt2020)
            {
                GalleryPresent.Bt2020ToBt709(nitsR, nitsG, nitsB, out nitsR, out nitsG, out nitsB);
            }

            sr = GalleryPresent.NitsToScrgb(nitsR);
            sg = GalleryPresent.NitsToScrgb(nitsG);
            sb = GalleryPresent.NitsToScrgb(nitsB);
            return;
        }

        HdrColor.EncodedRgbToScrgb(r, g, b, transfer, probe.CicpPrimaries, out sr, out sg, out sb);
        nitsY = GalleryPresent.LuminanceY(
            GalleryPresent.EncodedToNits(r, transfer),
            GalleryPresent.EncodedToNits(g, transfer),
            GalleryPresent.EncodedToNits(b, transfer),
            probe.CicpPrimaries == 9);
    }

    private static bool IsRgbLumaOnly(byte[] data, HdrPackedFormat format, int count)
    {
        var maxR = 0f;
        var maxG = 0f;
        var maxB = 0f;
        var step = Math.Max(1, count / 4096);
        for (var i = 0; i < count; i += step)
        {
            HdrPixels.Read(data, i, format, out var r, out var g, out var b, out _);
            maxR = Math.Max(maxR, r);
            maxG = Math.Max(maxG, g);
            maxB = Math.Max(maxB, b);
        }

        return HdrColor.IsLumaInRedOnly([maxR], [maxG], [maxB]);
    }

    [ComImport]
    [Guid("5b0d3235-4dba-4d44-865e-8f1d0e4fdd3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr buffer, out uint capacity);
    }
}
