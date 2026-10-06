using System.Runtime.InteropServices;
using Palace.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
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
/// Local HDR still → linear scRGB. HDR AVIF prefers libavif (identity
/// GBR). HDR JPEG XR uses native WIC COM float/half (WinRT has no float
/// pixel format). HEIF / other stills use WIC P010/NV12/YUY2 or packed
/// RGB-as-YUV. Never online-only.
/// </summary>
internal static class HdrWicDecode
{
    public static string? LastWicError { get; private set; }

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

        if (probe.Kind == HdrKind.HdrJxr)
        {
            var bytes = await TryReadLocalBytesAsync(path, cancellation).ConfigureAwait(false);
            return await Task.Run(
                () => FromWicFloat(
                    path,
                    bytes,
                    probe,
                    viewportPixelWidth,
                    viewportPixelHeight,
                    scaling,
                    wantRgba: true,
                    measure: false,
                    cancellation),
                cancellation).ConfigureAwait(false);
        }

        if (probe.Kind == HdrKind.HdrAvif && PathSafe.IsAvif(path))
        {
            try
            {
                var avif = await Task.Run(
                    () => HdrAvifDecode.TryLoad(
                        path, probe, viewportPixelWidth, viewportPixelHeight, scaling, cancellation),
                    cancellation).ConfigureAwait(false);
                if (avif is not null)
                {
                    return avif;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Native libavif load/throw — fall through to WIC.
            }
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

    public static Task<HdrStats?> TryMeasureAsync(
        string path,
        HdrProbe probe,
        CancellationToken cancellation) =>
        TryMeasureAsync(path, probe, 0, 0, cancellation);

    /// <summary>
    /// CIE Y / MaxCLL from a downscaled or viewport-sized frame — never a
    /// native 16384² decode. File pixel size is still reported as native.
    /// </summary>
    public static async Task<HdrStats?> TryMeasureAsync(
        string path,
        HdrProbe probe,
        int viewportPixelWidth,
        int viewportPixelHeight,
        CancellationToken cancellation)
    {
        if (!CloudFile.Exists(path) || CloudFile.IsOnlineOnly(path))
        {
            return null;
        }

        if (probe.Kind == HdrKind.HdrAvif && PathSafe.IsAvif(path))
        {
            try
            {
                var avif = await Task.Run(
                    () => HdrAvifDecode.TryMeasure(
                        path, probe, viewportPixelWidth, viewportPixelHeight, cancellation),
                    cancellation).ConfigureAwait(false);
                if (avif is not null)
                {
                    return avif;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Native libavif load/throw — fall through to WIC.
            }
        }

        if (probe.Kind == HdrKind.HdrRadiance || PathSafe.IsRadiance(path))
        {
            return await Task.Run(
                () =>
                {
                    var frame = FromRadiance(path);
                    return frame is null
                        ? (HdrStats?)null
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

        if (probe.Kind == HdrKind.HdrJxr)
        {
            var bytes = await TryReadLocalBytesAsync(path, cancellation).ConfigureAwait(false);
            return await Task.Run(
                () =>
                {
                    var frame = FromWicFloat(
                        path,
                        bytes,
                        probe,
                        viewportPixelWidth,
                        viewportPixelHeight,
                        ImageScaling.Fit,
                        wantRgba: false,
                        measure: true,
                        cancellation);
                    return frame is null
                        ? (HdrStats?)null
                        : new HdrStats(
                            frame.MaxNits,
                            frame.AvgNits,
                            frame.MinNits,
                            frame.MaxScrgb,
                            frame.NativeWidth,
                            frame.NativeHeight);
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

            var (decodeW, decodeH) = GalleryPresent.MeasureDecodeSize(
                nativeW, nativeH, viewportPixelWidth, viewportPixelHeight);
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
                () => Measure(packed.Data, packed.Format, decodeW, decodeH, nativeW, nativeH, probe, cancellation),
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

    /// <summary>
    /// Native WIC COM (not WinRT <c>BitmapDecoder</c>) so float / half
    /// JPEG XR can reach the scRGB swapchain. WinRT has no float pixel
    /// format and would clamp to <c>Rgba16</c>.
    /// </summary>
    /// <summary>
    /// Packaged WinUI: <c>File</c> can be ACCESS_DENIED; <c>StorageFile</c>
    /// is the FutureAccessList broker. AVIF already reads Captures via
    /// <c>File.ReadAllBytes</c> — try that first, then the broker.
    /// </summary>
    private static async Task<byte[]?> TryReadLocalBytesAsync(
        string path,
        CancellationToken cancellation)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellation).ConfigureAwait(false);
            if (bytes.Length > 0)
            {
                return bytes;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Packaged path: File can miss the broker.
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellation).ConfigureAwait(false);
            using var ras = await file.OpenReadAsync().AsTask(cancellation).ConfigureAwait(false);
            var size = ras.Size;
            if (size == 0 || size > 256L * 1024 * 1024)
            {
                return null;
            }

            var reader = new DataReader(ras.GetInputStreamAt(0));
            try
            {
                await reader.LoadAsync((uint)size).AsTask(cancellation).ConfigureAwait(false);
                var bytes = new byte[size];
                reader.ReadBytes(bytes);
                return bytes;
            }
            finally
            {
                reader.Dispose();
            }
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

    private static HdrFrame? FromWicFloat(
        string path,
        byte[]? bytes,
        HdrProbe probe,
        int viewportPixelWidth,
        int viewportPixelHeight,
        ImageScaling scaling,
        bool wantRgba,
        bool measure,
        CancellationToken cancellation)
    {
        LastWicError = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var packed = WicCom.CopyRgba(path, bytes, cancellation);
            if (packed is null)
            {
                LastWicError ??= "WIC copy failed";
                return null;
            }

            cancellation.ThrowIfCancellationRequested();
            var converted = Convert(
                packed.Value.Data,
                packed.Value.Format,
                packed.Value.Width,
                packed.Value.Height,
                probe,
                wantRgba,
                cancellation);
            if (converted is null)
            {
                LastWicError = "WIC convert failed";
                return null;
            }

            var rgba = converted.Value.Rgba;
            var width = packed.Value.Width;
            var height = packed.Value.Height;
            if (wantRgba && rgba is not null && packed.Value.Orientation is >= 2 and <= 8)
            {
                (rgba, width, height) = HdrPixels.OrientScrgbRgba(
                    rgba, packed.Value.Width, packed.Value.Height, packed.Value.Orientation);
            }
            else
            {
                (width, height) = HdrPixels.OrientedSize(
                    packed.Value.Width, packed.Value.Height, packed.Value.Orientation);
            }

            var (decodeW, decodeH) = measure
                ? GalleryPresent.MeasureDecodeSize(
                    packed.Value.NativeWidth,
                    packed.Value.NativeHeight,
                    viewportPixelWidth,
                    viewportPixelHeight)
                : GalleryPresent.PresentDecodeSize(
                    packed.Value.NativeWidth,
                    packed.Value.NativeHeight,
                    viewportPixelWidth,
                    viewportPixelHeight,
                    scaling);
            if (wantRgba && rgba is not null && decodeW > 0 && decodeH > 0
                && (decodeW != width || decodeH != height))
            {
                rgba = HdrPixels.BoxScaleScrgb(rgba, width, height, decodeW, decodeH);
                width = decodeW;
                height = decodeH;
            }

            LastWicError = null;
            return new HdrFrame
            {
                ScrgbRgba = rgba ?? [],
                Width = width,
                Height = height,
                NativeWidth = packed.Value.NativeWidth,
                NativeHeight = packed.Value.NativeHeight,
                MaxNits = converted.Value.MaxNits,
                AvgNits = converted.Value.AvgNits,
                MinNits = converted.Value.MinNits,
                MaxScrgb = converted.Value.MaxScrgb
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastWicError = "WIC throw " + ex.GetType().Name;
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
                (BitmapPixelFormat.Nv12, HdrPackedFormat.Nv12),
                (BitmapPixelFormat.Yuy2, HdrPackedFormat.Yuy2)
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
        int nativeWidth,
        int nativeHeight,
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
                nativeWidth,
                nativeHeight);
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

        var identity = HdrColor.IsIdentityMatrix(probe.CicpMatrix);
        var yuv = HdrPixels.TreatAsYuv(format, HdrColor.NeedsYuvConvert(probe.Kind), identity);
        if (yuv && !HdrPixels.IsYuv(format) && IsRgbLumaOnly(data, format, count))
        {
            // Y in R, G=B=0 — WIC dropped chroma (common for matrix=0
            // 4:4:4: P010/Yuy2 unavailable, Rgba16 is luma-only). Do not
            // present Isiac’s red tint, or identity GBR’s green (Y→G).
            // Fall back to SDR preview until a real 3-channel buffer exists.
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
                HdrPixels.ReadYuvPackedRgb(data, width, height, x, y, format, out var luma, out var u, out var v);
                HdrColor.YuvToRgb(
                    luma,
                    u,
                    v,
                    probe.CicpMatrix ?? 9,
                    probe.FullRange ?? true,
                    out var er,
                    out var eg,
                    out var eb);
                HdrColor.EncodedRgbToScrgb(er, eg, eb, probe.Transfer, probe.CicpPrimaries, out sr, out sg, out sb);
                a = 1f;
                nitsY = HdrColor.SourcePrimaryNitsY(er, eg, eb, probe.Transfer, probe.CicpPrimaries);
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
    /// right). AVIF/JXL/JXR/other use SKIV EncodedRgbToScrgb (JXR is
    /// already linear scRGB).
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
        nitsY = HdrColor.SourcePrimaryNitsY(r, g, b, transfer, probe.CicpPrimaries);
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

    /// <summary>
    /// Native WIC factory / decoder. WinRT <c>BitmapDecoder.GetPixelDataAsync</c>
    /// cannot request float or half formats.
    /// </summary>
    private static class WicCom
    {
        private static readonly Guid ClsidFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
        private static readonly Guid ClsidFactory2 = new("317d06e8-5f24-433d-bdf7-79ce68d8abc2");
        private static readonly Guid IidFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
        private const uint GenericRead = 0x80000000;
        private const uint ClsctxInproc = 1;

        public static (byte[] Data, HdrPackedFormat Format, int Width, int Height, int NativeWidth, int NativeHeight, uint Orientation)? CopyRgba(
            string path,
            byte[]? bytes,
            CancellationToken cancellation)
        {
            WicNative.IWICImagingFactory? factory = null;
            WicNative.IWICBitmapDecoder? decoder = null;
            WicNative.IWICBitmapFrameDecode? frame = null;
            WicNative.IWICFormatConverter? converter = null;
            DecoderOpenHold? streamKeep = null;
            try
            {
                factory = CreateFactory();
                if (!TryOpenDecoder(factory, path, bytes, out decoder, out frame, out streamKeep)
                    || (decoder is null && frame is null))
                {
                    LastWicError ??= WicNative.WicDecoderOpen.Failed("open");
                    return null;
                }

                if (frame is null)
                {
                    decoder!.GetFrame(0, out frame);
                }
                frame.GetSize(out var storedW, out var storedH);
                if (storedW == 0 || storedH == 0 || storedW > 16384 || storedH > 16384)
                {
                    LastWicError = "WIC size";
                    return null;
                }

                var orientation = ReadOrientation(frame);
                var (nativeW, nativeH) = HdrPixels.OrientedSize((int)storedW, (int)storedH, orientation);
                var source = WicNative.AsSource(frame);
                source.GetPixelFormat(out var format);
                if (!HdrPixels.TryMapWicPixelFormat(format, out var packed))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!TryConvertNew(factory, source, HdrPixels.GuidRgbaFloat, out converter, out source)
                        && !TryConvertNew(factory, source, HdrPixels.GuidRgbaHalf, out converter, out source))
                    {
                        LastWicError = "WIC format " + format.ToString("N")[..8];
                        return null;
                    }

                    source.GetPixelFormat(out format);
                    if (!HdrPixels.TryMapWicPixelFormat(format, out packed))
                    {
                        LastWicError = "WIC convert format";
                        return null;
                    }
                }

                var bytesPer = HdrPixels.BytesPerPixel(packed);
                if (bytesPer <= 0)
                {
                    LastWicError = "WIC bpp";
                    return null;
                }

                cancellation.ThrowIfCancellationRequested();
                var stride = storedW * (uint)bytesPer;
                var buffer = new byte[stride * storedH];
                var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    source.CopyPixels(IntPtr.Zero, stride, (uint)buffer.Length, pin.AddrOfPinnedObject());
                }
                finally
                {
                    pin.Free();
                }

                return (buffer, packed, (int)storedW, (int)storedH, nativeW, nativeH, orientation);
            }
            finally
            {
                Release(converter);
                Release(frame);
                Release(decoder);
                Release(factory);
                streamKeep?.Dispose();
            }
        }

        private static WicNative.IWICImagingFactory CreateFactory()
        {
            foreach (var clsid in new[] { ClsidFactory2, ClsidFactory })
            {
                var cls = clsid;
                var iid = IidFactory;
                if (CoCreateInstance(ref cls, IntPtr.Zero, ClsctxInproc, ref iid, out var unk) >= 0
                    && unk != IntPtr.Zero)
                {
                    try
                    {
                        var factory = WicNative.TypedFromIUnknown<WicNative.IWICImagingFactory>(unk);
                        if (factory is not null)
                        {
                            return factory;
                        }
                    }
                    finally
                    {
                        Marshal.Release(unk);
                    }
                }
            }

            foreach (var clsid in new[] { ClsidFactory2, ClsidFactory })
            {
                var type = Type.GetTypeFromCLSID(clsid, throwOnError: false);
                if (type is not null && Activator.CreateInstance(type) is WicNative.IWICImagingFactory factory)
                {
                    return factory;
                }
            }

            throw new InvalidOperationException("WIC factory");
        }

        private static bool TryConvertNew(
            WicNative.IWICImagingFactory factory,
            WicNative.IWICBitmapSource source,
            Guid dst,
            out WicNative.IWICFormatConverter? converter,
            out WicNative.IWICBitmapSource converted)
        {
            converter = null;
            converted = source;
            WicNative.IWICFormatConverter? created = null;
            try
            {
                factory.CreateFormatConverter(out created);
                if (!TryConvert(created, source, dst))
                {
                    Release(created);
                    return false;
                }

                converter = created;
                converted = WicNative.AsSource(created);
                return true;
            }
            catch
            {
                Release(created);
                return false;
            }
        }

        private sealed class DecoderOpenHold : IDisposable
        {
            public object? Stream;
            public GCHandle Pin;
            public IntPtr FileHandle;
            public byte[]? Bytes;
            public object? Ras;
            public object? WinrtDecoder;
            public object? WinrtFrame;

            public void Dispose()
            {
                if (Stream is not null)
                {
                    Release(Stream);
                    Stream = null;
                }

                if (Pin.IsAllocated)
                {
                    Pin.Free();
                }

                if (FileHandle != IntPtr.Zero && FileHandle != InvalidHandle)
                {
                    CloseHandle(FileHandle);
                    FileHandle = IntPtr.Zero;
                }

                DisposeKeep(WinrtFrame);
                WinrtFrame = null;
                DisposeKeep(WinrtDecoder);
                WinrtDecoder = null;
                DisposeKeep(Ras);
                Ras = null;
                Bytes = null;
            }

            private static void DisposeKeep(object? value)
            {
                if (value is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch
                    {
                        // WinRT / stream already closed.
                    }
                }
            }
        }

        private static bool TryOpenDecoder(
            WicNative.IWICImagingFactory factory,
            string path,
            byte[]? bytes,
            out WicNative.IWICBitmapDecoder? decoder,
            out WicNative.IWICBitmapFrameDecode? frame,
            out DecoderOpenHold? streamKeep)
        {
            decoder = null;
            frame = null;
            streamKeep = null;
            var lastHr = 0;
            var lastStage = WicNative.WicDecoderOpen.Filename;

            try
            {
                lastHr = factory.CreateDecoderFromFilename(
                    path, IntPtr.Zero, GenericRead, 0, out decoder);
                if (lastHr >= 0 && decoder is not null)
                {
                    return true;
                }
            }
            catch
            {
                decoder = null;
                lastHr = unchecked((int)0x80004005);
            }

            LastWicError = WicNative.WicDecoderOpen.Failed(lastStage, lastHr);

            if (TryOpenFromHandle(factory, path, out decoder, out streamKeep, out lastHr))
            {
                LastWicError = null;
                return true;
            }

            lastStage = WicNative.WicDecoderOpen.Handle;
            LastWicError = WicNative.WicDecoderOpen.Failed(lastStage, lastHr);

            bytes ??= TryReadLocalBytes(path);
            if (bytes is null || bytes.Length == 0)
            {
                LastWicError = WicNative.WicDecoderOpen.Failed("bytes");
                decoder = null;
                return false;
            }

            if (TryOpenFromPinnedMemory(factory, bytes, out decoder, out streamKeep, out lastHr))
            {
                LastWicError = null;
                return true;
            }

            lastStage = WicNative.WicDecoderOpen.Memory;
            LastWicError = WicNative.WicDecoderOpen.Failed(lastStage, lastHr);

            if (TryOpenFromWinrt(path, bytes, out decoder, out frame, out streamKeep, out lastHr))
            {
                LastWicError = null;
                return true;
            }

            lastStage = WicNative.WicDecoderOpen.Winrt;
            LastWicError = WicNative.WicDecoderOpen.Failed(lastStage, lastHr);
            decoder = null;
            frame = null;
            return false;
        }

        private static byte[]? TryReadLocalBytes(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                return bytes.Length > 0 ? bytes : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryOpenFromHandle(
            WicNative.IWICImagingFactory factory,
            string path,
            out WicNative.IWICBitmapDecoder? decoder,
            out DecoderOpenHold? hold,
            out int hr)
        {
            decoder = null;
            hold = null;
            hr = 0;
            var handle = CreateFileW(
                path,
                GenericRead,
                FileShareReadWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == InvalidHandle)
            {
                hr = Marshal.GetHRForLastWin32Error();
                if (hr == 0)
                {
                    hr = unchecked((int)0x80070005);
                }

                return false;
            }

            try
            {
                hr = factory.CreateDecoderFromFileHandle(
                    unchecked((UIntPtr)(ulong)handle.ToInt64()),
                    IntPtr.Zero,
                    0,
                    out decoder);
                if (hr >= 0 && decoder is not null)
                {
                    hold = new DecoderOpenHold { FileHandle = handle };
                    return true;
                }

                decoder = null;
                CloseHandle(handle);
                return false;
            }
            catch
            {
                decoder = null;
                CloseHandle(handle);
                hr = unchecked((int)0x80004005);
                return false;
            }
        }

        private static bool TryOpenFromPinnedMemory(
            WicNative.IWICImagingFactory factory,
            byte[] bytes,
            out WicNative.IWICBitmapDecoder? decoder,
            out DecoderOpenHold? hold,
            out int hr)
        {
            decoder = null;
            hold = null;
            hr = 0;
            WicNative.IWICStream? stream = null;
            var pin = default(GCHandle);
            try
            {
                factory.CreateStream(out stream);
                if (stream is null)
                {
                    hr = unchecked((int)0x80004005);
                    return false;
                }

                pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                hr = stream.InitializeFromMemory(pin.AddrOfPinnedObject(), (uint)bytes.Length);
                if (hr < 0)
                {
                    pin.Free();
                    Release(stream);
                    return false;
                }

                hold = new DecoderOpenHold
                {
                    Stream = stream,
                    Pin = pin,
                    Bytes = bytes
                };

                hr = factory.CreateDecoderFromStream(stream, IntPtr.Zero, 0, out decoder);
                if (hr < 0 || decoder is null)
                {
                    decoder = null;
                    hold.Dispose();
                    hold = null;
                    return false;
                }

                return true;
            }
            catch
            {
                decoder = null;
                if (hold is not null)
                {
                    hold.Dispose();
                    hold = null;
                }
                else
                {
                    if (pin.IsAllocated)
                    {
                        pin.Free();
                    }

                    Release(stream);
                }

                hr = unchecked((int)0x80004005);
                return false;
            }
        }

        /// <summary>
        /// Packaged BitmapImage already decodes these JXR files. Open
        /// with WinRT <c>BitmapDecoder.JpegXrDecoderId</c> (same inbox
        /// path), then native float/half <c>CopyPixels</c>. Do not
        /// <c>CoCreate(CLSID_WICWmpDecoder)</c> — that QIs
        /// <c>E_NOINTERFACE</c>.
        /// </summary>
        private static bool TryOpenFromWinrt(
            string path,
            byte[] bytes,
            out WicNative.IWICBitmapDecoder? decoder,
            out WicNative.IWICBitmapFrameDecode? frame,
            out DecoderOpenHold? hold,
            out int hr)
        {
            decoder = null;
            frame = null;
            hold = null;
            hr = 0;
            IRandomAccessStream? ras = null;
            try
            {
                ras = OpenRandomAccess(path, bytes);
                if (ras is null)
                {
                    hr = unchecked((int)0x80070002);
                    return false;
                }

                var winrt = CreateWinrtJpegXr(ras, out hr);
                if (winrt is null)
                {
                    ras.Dispose();
                    return false;
                }

                hold = new DecoderOpenHold
                {
                    Bytes = bytes,
                    Ras = ras,
                    WinrtDecoder = winrt
                };

                decoder = WicNative.TypedFromUnknown<WicNative.IWICBitmapDecoder>(winrt);
                if (decoder is not null)
                {
                    return true;
                }

                var winrtFrame = winrt.GetFrameAsync(0).AsTask().GetAwaiter().GetResult();
                hold.WinrtFrame = winrtFrame;
                frame = WicNative.TypedFromUnknown<WicNative.IWICBitmapFrameDecode>(winrtFrame);
                if (frame is not null)
                {
                    return true;
                }

                if (TryFromAppOverRas(ras, out decoder, out var stream, out hr) && decoder is not null)
                {
                    hold.Stream = stream;
                    return true;
                }

                hold.Dispose();
                hold = null;
                decoder = null;
                frame = null;
                if (hr == 0)
                {
                    hr = unchecked((int)WicNative.WicDecoderOpen.NoInterface);
                }

                return false;
            }
            catch (Exception ex)
            {
                decoder = null;
                frame = null;
                hold?.Dispose();
                hold = null;
                hr = ex.HResult != 0 ? ex.HResult : unchecked((int)0x80004005);
                return false;
            }
        }

        private static IRandomAccessStream? OpenRandomAccess(string path, byte[] bytes)
        {
            try
            {
                var file = StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
                return file.OpenReadAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // In-memory bytes next (same bytes AVIF already reads).
            }

            try
            {
                var ras = new InMemoryRandomAccessStream();
                var writer = new DataWriter(ras);
                try
                {
                    writer.WriteBytes(bytes);
                    writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                    writer.DetachStream();
                }
                finally
                {
                    writer.Dispose();
                }

                ras.Seek(0);
                return ras;
            }
            catch
            {
                return null;
            }
        }

        private static BitmapDecoder? CreateWinrtJpegXr(IRandomAccessStream ras, out int hr)
        {
            hr = 0;
            try
            {
                ras.Seek(0);
                return BitmapDecoder.CreateAsync(BitmapDecoder.JpegXrDecoderId, ras)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                hr = ex.HResult != 0 ? ex.HResult : unchecked((int)0x80004005);
            }

            try
            {
                ras.Seek(0);
                var decoder = BitmapDecoder.CreateAsync(ras).AsTask().GetAwaiter().GetResult();
                hr = 0;
                return decoder;
            }
            catch (Exception ex)
            {
                hr = ex.HResult != 0 ? ex.HResult : hr;
                return null;
            }
        }

        /// <summary>
        /// Same activation WinRT uses in a packaged app
        /// (<c>CoCreateInstanceFromApp</c> +
        /// <c>CreateStreamOverRandomAccessStream</c>), then native
        /// <c>Initialize</c> so we can CopyPixels float/half.
        /// </summary>
        private static bool TryFromAppOverRas(
            IRandomAccessStream ras,
            out WicNative.IWICBitmapDecoder? decoder,
            out object? stream,
            out int hr)
        {
            decoder = null;
            stream = null;
            try
            {
                ras.Seek(0);
            }
            catch
            {
                // Stream may already be at 0.
            }

            hr = TryCreateStreamOverRas(ras, out stream);
            if (hr < 0 || stream is null)
            {
                return false;
            }

            hr = TryCoCreateFromApp(WicNative.JpegXrDecoderId, WicNative.IidBitmapDecoder, out var unk);
            if (hr < 0 || unk == IntPtr.Zero)
            {
                var dllHr = TryDecoderFromCodecsDll(out decoder);
                if (decoder is not null)
                {
                    var initDll = decoder.Initialize(stream, 0);
                    if (initDll >= 0)
                    {
                        hr = 0;
                        return true;
                    }

                    Release(decoder);
                    decoder = null;
                    hr = initDll;
                }
                else if (hr == 0)
                {
                    hr = dllHr;
                }

                Release(stream);
                stream = null;
                return false;
            }

            try
            {
                decoder = WicNative.TypedFromIUnknown<WicNative.IWICBitmapDecoder>(unk);
                if (decoder is null)
                {
                    hr = unchecked((int)WicNative.WicDecoderOpen.NoInterface);
                    Release(stream);
                    stream = null;
                    return false;
                }

                var init = decoder.Initialize(stream, 0);
                if (init < 0)
                {
                    Release(decoder);
                    decoder = null;
                    Release(stream);
                    stream = null;
                    hr = init;
                    return false;
                }

                hr = 0;
                return true;
            }
            finally
            {
                Marshal.Release(unk);
            }
        }

        private static int TryCreateStreamOverRas(IRandomAccessStream ras, out object? stream)
        {
            stream = null;
            var unk = Marshal.GetIUnknownForObject(ras);
            try
            {
                var iid = WicNative.IidStream;
                var hr = CreateStreamOverRandomAccessStream(unk, ref iid, out var streamPtr);
                if (hr < 0 || streamPtr == IntPtr.Zero)
                {
                    return hr != 0 ? hr : unchecked((int)0x80004005);
                }

                try
                {
                    stream = Marshal.GetObjectForIUnknown(streamPtr);
                    return stream is null ? unchecked((int)0x80004002) : 0;
                }
                finally
                {
                    Marshal.Release(streamPtr);
                }
            }
            finally
            {
                Marshal.Release(unk);
            }
        }

        private static int TryCoCreateFromApp(Guid clsid, Guid iid, out IntPtr unk)
        {
            unk = IntPtr.Zero;
            var iidLocal = iid;
            var iidPin = GCHandle.Alloc(iidLocal, GCHandleType.Pinned);
            try
            {
                var results = new MultiQi[1];
                results[0].pIID = iidPin.AddrOfPinnedObject();
                var hr = CoCreateInstanceFromApp(
                    ref clsid, IntPtr.Zero, ClsctxInproc, IntPtr.Zero, 1, results);
                if (hr < 0)
                {
                    return hr;
                }

                unk = results[0].pItf;
                if (results[0].hr < 0)
                {
                    return results[0].hr;
                }

                return unk == IntPtr.Zero ? unchecked((int)WicNative.WicDecoderOpen.NoInterface) : 0;
            }
            catch
            {
                return unchecked((int)0x80004001);
            }
            finally
            {
                iidPin.Free();
            }
        }

        private static int TryDecoderFromCodecsDll(out WicNative.IWICBitmapDecoder? decoder)
        {
            decoder = null;
            var lastHr = 0;
            foreach (var name in new[] { "WindowsCodecs.dll", "WindowsCodecsExt.dll" })
            {
                var hr = TryDecoderFromModule(name, out decoder);
                if (decoder is not null)
                {
                    return 0;
                }

                if (hr != 0)
                {
                    lastHr = hr;
                }
            }

            return lastHr != 0 ? lastHr : unchecked((int)WicNative.WicDecoderOpen.ComponentNotFound);
        }

        private static int TryDecoderFromModule(string fileName, out WicNative.IWICBitmapDecoder? decoder)
        {
            decoder = null;
            var module = GetModuleHandleW(fileName);
            if (module == IntPtr.Zero)
            {
                module = LoadLibraryExW(fileName, IntPtr.Zero, LoadLibrarySearchSystem32);
            }

            if (module == IntPtr.Zero)
            {
                var win32 = Marshal.GetHRForLastWin32Error();
                return win32 != 0 ? win32 : unchecked((int)0x8007007E);
            }

            var proc = GetProcAddress(module, "DllGetClassObject");
            if (proc == IntPtr.Zero)
            {
                return unchecked((int)0x80004005);
            }

            var fn = Marshal.GetDelegateForFunctionPointer<DllGetClassObjectFn>(proc);
            var clsid = WicNative.ClsidWmpDecoder;
            var factoryIid = WicNative.IidClassFactory;
            var hr = fn(ref clsid, ref factoryIid, out var factoryUnk);
            if (hr < 0 || factoryUnk == IntPtr.Zero)
            {
                return hr != 0 ? hr : unchecked((int)WicNative.WicDecoderOpen.ComponentNotFound);
            }

            try
            {
                var factory = WicNative.TypedFromIUnknown<WicNative.IClassFactory>(factoryUnk);
                if (factory is null)
                {
                    return unchecked((int)WicNative.WicDecoderOpen.NoInterface);
                }

                try
                {
                    var decoderIid = WicNative.IidBitmapDecoder;
                    hr = factory.CreateInstance(IntPtr.Zero, ref decoderIid, out var decoderUnk);
                    if (hr < 0 || decoderUnk == IntPtr.Zero)
                    {
                        return hr != 0 ? hr : unchecked((int)WicNative.WicDecoderOpen.ComponentNotFound);
                    }

                    try
                    {
                        decoder = WicNative.TypedFromIUnknown<WicNative.IWICBitmapDecoder>(decoderUnk);
                        return decoder is null ? unchecked((int)WicNative.WicDecoderOpen.NoInterface) : 0;
                    }
                    finally
                    {
                        Marshal.Release(decoderUnk);
                    }
                }
                finally
                {
                    Release(factory);
                }
            }
            finally
            {
                Marshal.Release(factoryUnk);
            }
        }

        private const uint FileShareReadWrite = 3;
        private const uint OpenExisting = 3;
        private const uint LoadLibrarySearchSystem32 = 0x00000800;
        private static readonly IntPtr InvalidHandle = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct MultiQi
        {
            public IntPtr pIID;
            public IntPtr pItf;
            public int hr;
        }

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstanceFromApp(
            ref Guid rclsid,
            IntPtr pUnkOuter,
            uint dwClsContext,
            IntPtr reserved,
            uint dwCount,
            [In] [Out] MultiQi[] pResults);

        [DllImport("shcore.dll", ExactSpelling = true)]
        private static extern int CreateStreamOverRandomAccessStream(
            IntPtr punk,
            ref Guid riid,
            out IntPtr ppv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DllGetClassObjectFn(ref Guid rclsid, ref Guid riid, out IntPtr ppv);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static bool TryConvert(
            WicNative.IWICFormatConverter converter,
            WicNative.IWICBitmapSource source,
            Guid dst)
        {
            try
            {
                converter.Initialize(source, ref dst, 0, IntPtr.Zero, 0, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static uint ReadOrientation(WicNative.IWICBitmapFrameDecode frame)
        {
            WicNative.IWICMetadataQueryReader? reader = null;
            try
            {
                frame.GetMetadataQueryReader(out reader);
                foreach (var name in new[]
                         {
                             "System.Photo.Orientation",
                             "/ifd/{ushort=274}",
                             "/app1/ifd/{ushort=274}"
                         })
                {
                    var value = default(WicNative.PropVariant);
                    try
                    {
                        reader.GetMetadataByName(name, ref value);
                        var n = ReadUInt(value);
                        if (n is >= 1 and <= 8)
                        {
                            return n;
                        }
                    }
                    catch
                    {
                        // Next query name.
                    }
                    finally
                    {
                        PropVariantClear(ref value);
                    }
                }
            }
            catch
            {
                // JPEG XR without orientation metadata is upright.
            }
            finally
            {
                Release(reader);
            }

            return 1;
        }

        private static uint ReadUInt(WicNative.PropVariant value) =>
            value.vt switch
            {
                2 => (ushort)(long)value.data1, // VT_I2
                18 => (ushort)(ulong)value.data1, // VT_UI2
                3 => (uint)(int)(long)value.data1, // VT_I4
                19 => (uint)(ulong)value.data1, // VT_UI4
                _ => 0
            };

        private static void Release(object? com)
        {
            if (com is not null)
            {
                Marshal.ReleaseComObject(com);
            }
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref WicNative.PropVariant pvar);

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(
            ref Guid rclsid,
            IntPtr pUnkOuter,
            uint dwClsContext,
            ref Guid riid,
            out IntPtr ppv);
    }

    [ComImport]
    [Guid("5b0d3235-4dba-4d44-865e-8f1d0e4fdd3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr buffer, out uint capacity);
    }
}
