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
/// Local HDR still → linear scRGB. HDR AVIF prefers libavif (identity
/// GBR). HDR JPEG XR uses native WIC COM float/half (WinRT has no float
/// pixel format). HEIF / other stills use WIC P010/NV12/YUY2 or packed
/// RGB-as-YUV. Never online-only.
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

        if (probe.Kind == HdrKind.HdrJxr)
        {
            return await Task.Run(
                () => FromWicFloat(
                    path,
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
            return await Task.Run(
                () =>
                {
                    var frame = FromWicFloat(
                        path,
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
    private static HdrFrame? FromWicFloat(
        string path,
        HdrProbe probe,
        int viewportPixelWidth,
        int viewportPixelHeight,
        ImageScaling scaling,
        bool wantRgba,
        bool measure,
        CancellationToken cancellation)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var packed = WicCom.CopyRgba(
                path, viewportPixelWidth, viewportPixelHeight, scaling, measure, cancellation);
            if (packed is null)
            {
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
        private static readonly Guid GuidRgbaFloat = new("6fddc324-4e03-4bfe-b185-3d77768dc91b");
        private static readonly Guid GuidRgbaHalf = new("6fddc324-4e03-4bfe-b185-3d77768dc910");
        private const uint GenericRead = 0x80000000;
        private const uint InterpolationLinear = 1;

        public static (byte[] Data, HdrPackedFormat Format, int Width, int Height, int NativeWidth, int NativeHeight, uint Orientation)? CopyRgba(
            string path,
            int viewportPixelWidth,
            int viewportPixelHeight,
            ImageScaling scaling,
            bool measure,
            CancellationToken cancellation)
        {
            IWICImagingFactory? factory = null;
            IWICBitmapDecoder? decoder = null;
            IWICBitmapFrameDecode? frame = null;
            IWICBitmapScaler? scaler = null;
            IWICFormatConverter? converter = null;
            try
            {
                factory = CreateFactory();
                factory.CreateDecoderFromFilename(path, IntPtr.Zero, GenericRead, 0, out decoder);
                decoder.GetFrame(0, out frame);
                frame.GetSize(out var storedW, out var storedH);
                if (storedW == 0 || storedH == 0 || storedW > 16384 || storedH > 16384)
                {
                    return null;
                }

                var orientation = ReadOrientation(frame);
                var (nativeW, nativeH) = HdrPixels.OrientedSize((int)storedW, (int)storedH, orientation);
                var (decodeW, decodeH) = measure
                    ? GalleryPresent.MeasureDecodeSize(nativeW, nativeH, viewportPixelWidth, viewportPixelHeight)
                    : GalleryPresent.PresentDecodeSize(
                        nativeW, nativeH, viewportPixelWidth, viewportPixelHeight, scaling);
                if (decodeW <= 0 || decodeH <= 0)
                {
                    decodeW = nativeW;
                    decodeH = nativeH;
                }

                var (scaleW, scaleH) = HdrPixels.SourceScaleSize(
                    (int)storedW, (int)storedH, nativeW, nativeH, decodeW, decodeH);
                IWICBitmapSource source = frame;
                if (scaleW > 0 && scaleH > 0 && (scaleW != (int)storedW || scaleH != (int)storedH))
                {
                    cancellation.ThrowIfCancellationRequested();
                    factory.CreateBitmapScaler(out scaler);
                    scaler.Initialize(frame, (uint)scaleW, (uint)scaleH, InterpolationLinear);
                    source = scaler;
                }

                source.GetPixelFormat(out var format);
                HdrPackedFormat packed;
                int bytesPer;
                if (format == GuidRgbaFloat)
                {
                    packed = HdrPackedFormat.RgbaFloat;
                    bytesPer = 16;
                }
                else if (format == GuidRgbaHalf)
                {
                    packed = HdrPackedFormat.RgbaHalf;
                    bytesPer = 8;
                }
                else
                {
                    packed = HdrPackedFormat.RgbaFloat;
                    bytesPer = 16;
                    factory.CreateFormatConverter(out converter);
                    var dst = GuidRgbaFloat;
                    converter.Initialize(source, ref dst, 0, IntPtr.Zero, 0, 0);
                    source = converter;
                }

                source.GetSize(out var copyW, out var copyH);
                if (copyW == 0 || copyH == 0 || copyW > 16384 || copyH > 16384)
                {
                    return null;
                }

                cancellation.ThrowIfCancellationRequested();
                var stride = copyW * (uint)bytesPer;
                var buffer = new byte[stride * copyH];
                var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    source.CopyPixels(IntPtr.Zero, stride, (uint)buffer.Length, pin.AddrOfPinnedObject());
                }
                finally
                {
                    pin.Free();
                }

                return (buffer, packed, (int)copyW, (int)copyH, nativeW, nativeH, orientation);
            }
            finally
            {
                Release(converter);
                Release(scaler);
                Release(frame);
                Release(decoder);
                Release(factory);
            }
        }

        private static IWICImagingFactory CreateFactory()
        {
            foreach (var clsid in new[] { ClsidFactory2, ClsidFactory })
            {
                var type = Type.GetTypeFromCLSID(clsid, throwOnError: false);
                if (type is not null && Activator.CreateInstance(type) is IWICImagingFactory factory)
                {
                    return factory;
                }
            }

            throw new InvalidOperationException("WIC factory");
        }

        private static uint ReadOrientation(IWICBitmapFrameDecode frame)
        {
            IWICMetadataQueryReader? reader = null;
            try
            {
                frame.GetMetadataQueryReader(out reader);
                foreach (var name in new[]
                         {
                             "System.Photo.Orientation",
                             "/ifd/{ushort=274}",
                             "/app1/ifd/{ushort=274}",
                             "/ifd/{ushort=48130}"
                         })
                {
                    var value = default(PropVariant);
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

        private static uint ReadUInt(PropVariant value) =>
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
        private static extern int PropVariantClear(ref PropVariant pvar);

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort vt;
            public ushort reserved1;
            public ushort reserved2;
            public ushort reserved3;
            public IntPtr data1;
            public IntPtr data2;
        }

        [ComImport]
        [Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICImagingFactory
        {
            void CreateDecoderFromFilename(
                [MarshalAs(UnmanagedType.LPWStr)] string wzFilename,
                IntPtr pguidVendor,
                uint dwDesiredAccess,
                uint metadataOptions,
                out IWICBitmapDecoder ppIDecoder);

            void CreateDecoderFromStream();
            void CreateDecoderFromFileHandle();
            void CreateComponentInfo();
            void CreateDecoder();
            void CreateEncoder();
            void CreatePalette();
            void CreateFormatConverter(out IWICFormatConverter ppIFormatConverter);
            void CreateBitmapScaler(out IWICBitmapScaler ppIBitmapScaler);
        }

        [ComImport]
        [Guid("9edde9c7-3d7c-410a-ba78-0ebaf22aa18d")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapDecoder
        {
            void QueryCapability();
            void Initialize();
            void GetContainerFormat();
            void GetDecoderInfo();
            void CopyPalette();
            void GetMetadataQueryReader();
            void GetPreview();
            void GetColorContexts();
            void GetThumbnail();
            void GetFrameCount();
            void GetFrame(uint index, out IWICBitmapFrameDecode ppIFrameDecode);
        }

        [ComImport]
        [Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapSource
        {
            void GetSize(out uint puiWidth, out uint puiHeight);
            void GetPixelFormat(out Guid pPixelFormat);
            void GetResolution(out double pDpiX, out double pDpiY);
            void CopyPalette();
            void CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);
        }

        [ComImport]
        [Guid("3b16811b-6a43-4ec9-a813-3d930c13b940")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapFrameDecode : IWICBitmapSource
        {
            void GetMetadataQueryReader(out IWICMetadataQueryReader ppIMetadataQueryReader);
        }

        [ComImport]
        [Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICFormatConverter : IWICBitmapSource
        {
            void Initialize(
                IWICBitmapSource pISource,
                ref Guid dstFormat,
                uint dither,
                IntPtr pIPalette,
                double alphaThresholdPercent,
                uint paletteTranslate);
        }

        [ComImport]
        [Guid("00000302-a8f2-4877-ba0a-fd2b6645fb94")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapScaler : IWICBitmapSource
        {
            void Initialize(
                IWICBitmapSource pISource,
                uint uiWidth,
                uint uiHeight,
                uint mode);
        }

        [ComImport]
        [Guid("30989668-e1c9-4597-b395-458eedb808df")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICMetadataQueryReader
        {
            void GetContainerFormat();
            void GetLocation();
            void GetMetadataByName(
                [MarshalAs(UnmanagedType.LPWStr)] string wzName,
                ref PropVariant pvarValue);
        }
    }

    [ComImport]
    [Guid("5b0d3235-4dba-4d44-865e-8f1d0e4fdd3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr buffer, out uint capacity);
    }
}
