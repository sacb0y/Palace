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
/// GBR). HDR JPEG XR runs native WIC COM float/half CopyPixels first;
/// packaged apps then QI the WinRT <c>CreateAsync(stream)</c>
/// decoder/frame (<c>IWinRTObject.ThisPtr</c> + <c>QueryInterface</c>)
/// or <c>CreateStreamOverRandomAccessStream</c> for float/half.
/// WinRT unorm clamps HDR — do not present it. Do not QI with the
/// wrong <c>ISoftwareBitmapNative</c> GUID. HEIF / other stills use
/// WIC P010/NV12/YUY2 or packed RGB-as-YUV. Never online-only.
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
            return await TryLoadJxrAsync(
                path,
                probe,
                viewportPixelWidth,
                viewportPixelHeight,
                scaling,
                wantRgba: true,
                measure: false,
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
            var frame = await TryLoadJxrAsync(
                path,
                probe,
                viewportPixelWidth,
                viewportPixelHeight,
                ImageScaling.Fit,
                wantRgba: false,
                measure: true,
                cancellation).ConfigureAwait(false);
            return frame is null
                ? null
                : new HdrStats(
                    frame.MaxNits,
                    frame.AvgNits,
                    frame.MinNits,
                    frame.MaxScrgb,
                    frame.NativeWidth,
                    frame.NativeHeight);
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

    /// <summary>
    /// JXR: native WIC float / half / <c>1010102XR</c> first. Packaged
    /// catalog omits HD Photo — next open via
    /// <c>CreateAsync(stream)</c> and QI the WinRT decoder/frame for
    /// float CopyPixels (<c>IWinRTObject.ThisPtr</c> +
    /// <c>QueryInterface</c>), else
    /// <c>CreateStreamOverRandomAccessStream</c>. WinRT unorm
    /// (<c>Rgba16</c>/<c>Rgba8</c>/<c>Bgra8</c>) clamps to scRGB 1.0 —
    /// do not present (<c>JxrWinrtClampsHdr</c> / <c>unorm</c>).
    /// </summary>
    private static async Task<HdrFrame?> TryLoadJxrAsync(
        string path,
        HdrProbe probe,
        int viewportPixelWidth,
        int viewportPixelHeight,
        ImageScaling scaling,
        bool wantRgba,
        bool measure,
        CancellationToken cancellation)
    {
        LastWicError = null;
        var bytes = await TryReadLocalBytesAsync(path, cancellation).ConfigureAwait(false);
        var native = await Task.Run(
            () => FromWicFloat(
                path,
                bytes,
                probe,
                viewportPixelWidth,
                viewportPixelHeight,
                scaling,
                wantRgba,
                measure,
                cancellation),
            cancellation).ConfigureAwait(false);
        if (native is not null)
        {
            LastWicError = null;
            return native;
        }

        var nativeError = LastWicError;
        try
        {
            var winrt = await TryLoadJxrWinrtAsync(
                path,
                probe,
                viewportPixelWidth,
                viewportPixelHeight,
                scaling,
                wantRgba,
                measure,
                cancellation).ConfigureAwait(false);
            if (winrt is not null)
            {
                LastWicError = null;
                return winrt;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastWicError ??= WicNative.WicDecoderOpen.Failed(
                WicNative.WicDecoderOpen.CreateAsync,
                ex);
        }

        if (string.IsNullOrEmpty(LastWicError))
        {
            LastWicError = nativeError;
        }
        else if (!string.IsNullOrEmpty(nativeError)
            && LastWicError.Contains(WicNative.WicDecoderOpen.UnormClamp, StringComparison.Ordinal))
        {
            // Prefer the float-path failure over a later unorm reject.
            LastWicError = nativeError;
        }

        return null;
    }

    private static async Task<HdrFrame?> TryLoadJxrWinrtAsync(
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
            return await TryLoadJxrWinrtCoreAsync(
                path,
                probe,
                viewportPixelWidth,
                viewportPixelHeight,
                scaling,
                wantRgba,
                measure,
                cancellation).ConfigureAwait(false);
        }
        catch (Exception ex) when (WicNative.WicDecoderOpen.IsWrongThread(ex.HResult))
        {
            try
            {
                return await UiDispatch.RunTaskAsync(
                    () => TryLoadJxrWinrtCoreAsync(
                        path,
                        probe,
                        viewportPixelWidth,
                        viewportPixelHeight,
                        scaling,
                        wantRgba,
                        measure,
                        cancellation)).ConfigureAwait(false);
            }
            catch (Exception retry) when (WicNative.WicDecoderOpen.IsWrongThread(retry.HResult))
            {
                LastWicError ??= WicNative.WicDecoderOpen.Failed(
                    WicNative.WicDecoderOpen.Qi,
                    retry);
                return null;
            }
        }
    }

    private static async Task<HdrFrame?> TryLoadJxrWinrtCoreAsync(
        string path,
        HdrProbe probe,
        int viewportPixelWidth,
        int viewportPixelHeight,
        ImageScaling scaling,
        bool wantRgba,
        bool measure,
        CancellationToken cancellation)
    {
        LastWicError = null;
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellation);
        cancellation.ThrowIfCancellationRequested();
        using var stream = await file.OpenAsync(FileAccessMode.Read).AsTask(cancellation);
        cancellation.ThrowIfCancellationRequested();
        BitmapDecoder decoder;
        try
        {
            decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellation);
        }
        catch (Exception ex) when (WicNative.WicDecoderOpen.IsWrongThread(ex.HResult))
        {
            throw;
        }
        catch (Exception ex)
        {
            LastWicError = WicNative.WicDecoderOpen.Failed(
                WicNative.WicDecoderOpen.CreateAsync,
                ex);
            return null;
        }

        BitmapFrame? bitmapFrame = null;
        try
        {
            bitmapFrame = await decoder.GetFrameAsync(0).AsTask(cancellation);
        }
        catch (Exception ex) when (WicNative.WicDecoderOpen.IsWrongThread(ex.HResult))
        {
            throw;
        }
        catch
        {
            // Frame optional; decoder QI next.
        }

        cancellation.ThrowIfCancellationRequested();
        var floatFrame = await Task.Run(
            () => WicCom.CopyFloatFromWinrt(
                decoder,
                bitmapFrame,
                stream,
                probe,
                viewportPixelWidth,
                viewportPixelHeight,
                scaling,
                wantRgba,
                measure,
                cancellation),
            cancellation).ConfigureAwait(false);
        if (floatFrame is not null)
        {
            return floatFrame;
        }

        var floatError = LastWicError;

        // Diagnostic only — unorm is never presented as HDR.
        var nativeW = (int)decoder.OrientedPixelWidth;
        var nativeH = (int)decoder.OrientedPixelHeight;
        if (nativeW > 0 && nativeH > 0 && nativeW <= 16384 && nativeH <= 16384)
        {
            var (decodeW, decodeH) = measure
                ? GalleryPresent.MeasureDecodeSize(
                    nativeW, nativeH, viewportPixelWidth, viewportPixelHeight)
                : GalleryPresent.PresentDecodeSize(
                    nativeW, nativeH, viewportPixelWidth, viewportPixelHeight, scaling);
            if (decodeW <= 0 || decodeH <= 0)
            {
                decodeW = nativeW;
                decodeH = nativeH;
            }

            var packed = await TryJxrSoftwareBitmapAsync(decoder, decodeW, decodeH, cancellation);
            if (packed is null)
            {
                packed = await TryJxrPixelDataAsync(decoder, decodeW, decodeH, cancellation);
            }

            if (packed is not null && GalleryPresent.JxrWinrtClampsHdr(packed.Value.Format))
            {
                LastWicError = !string.IsNullOrEmpty(floatError)
                    ? floatError
                    : WicNative.WicDecoderOpen.Failed(WicNative.WicDecoderOpen.UnormClamp);
                return null;
            }

            if (packed is not null)
            {
                cancellation.ThrowIfCancellationRequested();
                var copy = packed.Value;
                return await Task.Run(
                    () => FinishJxrFrame(
                        copy.Data,
                        copy.Format,
                        copy.Width,
                        copy.Height,
                        nativeW,
                        nativeH,
                        probe,
                        viewportPixelWidth,
                        viewportPixelHeight,
                        scaling,
                        wantRgba,
                        measure,
                        cancellation),
                    cancellation);
            }
        }

        if (!string.IsNullOrEmpty(floatError))
        {
            LastWicError = floatError;
        }

        return null;
    }

    private static async Task<(byte[] Data, HdrPackedFormat Format, int Width, int Height)?> TryJxrSoftwareBitmapAsync(
        BitmapDecoder decoder,
        int decodeW,
        int decodeH,
        CancellationToken cancellation)
    {
        var transform = JxrTransform(decoder, decodeW, decodeH);
        foreach (var (format, alpha) in new[]
        {
            (BitmapPixelFormat.Rgba16, BitmapAlphaMode.Premultiplied),
            (BitmapPixelFormat.Unknown, BitmapAlphaMode.Premultiplied),
            (BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied),
            (BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
        })
        {
            cancellation.ThrowIfCancellationRequested();
            SoftwareBitmap? bitmap = null;
            try
            {
                bitmap = format == BitmapPixelFormat.Unknown
                    ? await decoder.GetSoftwareBitmapAsync().AsTask(cancellation)
                    : await decoder.GetSoftwareBitmapAsync(
                        format,
                        alpha,
                        transform,
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.DoNotColorManage).AsTask(cancellation);
            }
            catch (Exception ex) when (WicNative.WicDecoderOpen.IsWrongThread(ex.HResult))
            {
                throw;
            }
            catch (Exception ex)
            {
                LastWicError = WicNative.WicDecoderOpen.Failed(
                    WicNative.WicDecoderOpen.GetSoftwareBitmap,
                    ex);
                continue;
            }

            if (bitmap is null)
            {
                continue;
            }

            using (bitmap)
            {
                if (!TryMapSoftwareFormat(bitmap.BitmapPixelFormat, out var packed))
                {
                    LastWicError = WicNative.WicDecoderOpen.Failed(
                        WicNative.WicDecoderOpen.GetSoftwareBitmap);
                    continue;
                }

                var copied = CopySoftwareRgb(bitmap, packed);
                if (copied is not null)
                {
                    return (copied, packed, bitmap.PixelWidth, bitmap.PixelHeight);
                }
            }
        }

        return null;
    }

    private static async Task<(byte[] Data, HdrPackedFormat Format, int Width, int Height)?> TryJxrPixelDataAsync(
        BitmapDecoder decoder,
        int decodeW,
        int decodeH,
        CancellationToken cancellation)
    {
        var transform = JxrTransform(decoder, decodeW, decodeH);
        foreach (var (format, packed) in new[]
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
                    format,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage).AsTask(cancellation);
                var bytes = data.DetachPixelData();
                if (HdrPixels.HasPackedData(bytes, packed, decodeW, decodeH))
                {
                    LastWicError = null;
                    return (bytes, packed, decodeW, decodeH);
                }
            }
            catch (Exception ex) when (WicNative.WicDecoderOpen.IsWrongThread(ex.HResult))
            {
                throw;
            }
            catch (Exception ex)
            {
                LastWicError = WicNative.WicDecoderOpen.Failed(
                    WicNative.WicDecoderOpen.GetPixelData,
                    ex);
            }
        }

        return null;
    }

    private static BitmapTransform JxrTransform(BitmapDecoder decoder, int decodeW, int decodeH)
    {
        var transform = new BitmapTransform();
        var sourceW = (int)decoder.PixelWidth;
        var sourceH = (int)decoder.PixelHeight;
        var orientedW = (int)decoder.OrientedPixelWidth;
        var orientedH = (int)decoder.OrientedPixelHeight;
        var (scaleW, scaleH) = HdrPixels.SourceScaleSize(
            sourceW, sourceH, orientedW, orientedH, decodeW, decodeH);
        if (scaleW > 0 && scaleH > 0 && (scaleW < sourceW || scaleH < sourceH))
        {
            transform.ScaledWidth = (uint)scaleW;
            transform.ScaledHeight = (uint)scaleH;
            transform.InterpolationMode = BitmapInterpolationMode.Linear;
        }

        return transform;
    }

    private static bool TryMapSoftwareFormat(BitmapPixelFormat format, out HdrPackedFormat packed)
    {
        switch (format)
        {
            case BitmapPixelFormat.Rgba16:
                packed = HdrPackedFormat.Rgba16;
                return true;
            case BitmapPixelFormat.Rgba8:
                packed = HdrPackedFormat.Rgba8;
                return true;
            case BitmapPixelFormat.Bgra8:
                packed = HdrPackedFormat.Bgra8;
                return true;
            default:
                packed = default;
                return false;
        }
    }

    private static byte[]? CopySoftwareRgb(SoftwareBitmap bitmap, HdrPackedFormat format)
    {
        try
        {
            using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
            if (buffer.GetPlaneCount() < 1)
            {
                LastWicError = WicNative.WicDecoderOpen.Failed(
                    WicNative.WicDecoderOpen.LockBuffer);
                return null;
            }

            var desc = buffer.GetPlaneDescription(0);
            using var reference = buffer.CreateReference();
            var access = reference.As<IMemoryBufferByteAccess>();
            access.GetBuffer(out var ptr, out var capacity);
            if (ptr == IntPtr.Zero || capacity == 0)
            {
                LastWicError = WicNative.WicDecoderOpen.Failed(
                    WicNative.WicDecoderOpen.LockBuffer);
                return null;
            }

            var raw = new byte[(int)capacity];
            Marshal.Copy(ptr, raw, 0, raw.Length);
            var bpp = HdrPixels.BytesPerPixel(format);
            if (bpp <= 0)
            {
                LastWicError = WicNative.WicDecoderOpen.Failed(
                    WicNative.WicDecoderOpen.LockBuffer);
                return null;
            }

            var stride = bitmap.PixelWidth * bpp;
            var dest = new byte[stride * bitmap.PixelHeight];
            for (var row = 0; row < bitmap.PixelHeight; row++)
            {
                var src = desc.StartIndex + (row * desc.Stride);
                if (src < 0 || src + stride > raw.Length)
                {
                    LastWicError = WicNative.WicDecoderOpen.Failed(
                        WicNative.WicDecoderOpen.LockBuffer);
                    return null;
                }

                Buffer.BlockCopy(raw, src, dest, row * stride, stride);
            }

            LastWicError = null;
            return dest;
        }
        catch (Exception ex) when (WicNative.WicDecoderOpen.IsWrongThread(ex.HResult))
        {
            throw;
        }
        catch (Exception ex)
        {
            LastWicError = WicNative.WicDecoderOpen.Failed(
                WicNative.WicDecoderOpen.LockBuffer,
                ex);
            return null;
        }
    }

    private static HdrFrame? FinishJxrFrame(
        byte[] data,
        HdrPackedFormat format,
        int width,
        int height,
        int nativeWidth,
        int nativeHeight,
        HdrProbe probe,
        int viewportPixelWidth,
        int viewportPixelHeight,
        ImageScaling scaling,
        bool wantRgba,
        bool measure,
        CancellationToken cancellation)
    {
        var converted = Convert(data, format, width, height, probe, wantRgba, cancellation);
        if (converted is null)
        {
            LastWicError = "WIC convert failed";
            return null;
        }

        var rgba = converted.Value.Rgba;
        var (decodeW, decodeH) = measure
            ? GalleryPresent.MeasureDecodeSize(
                nativeWidth, nativeHeight, viewportPixelWidth, viewportPixelHeight)
            : GalleryPresent.PresentDecodeSize(
                nativeWidth, nativeHeight, viewportPixelWidth, viewportPixelHeight, scaling);
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
            NativeWidth = nativeWidth,
            NativeHeight = nativeHeight,
            MaxNits = converted.Value.MaxNits,
            AvgNits = converted.Value.AvgNits,
            MinNits = converted.Value.MinNits,
            MaxScrgb = converted.Value.MaxScrgb
        };
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

                return CopyOpenedFrame(factory, frame, cancellation);
            }
            finally
            {
                Release(frame);
                Release(decoder);
                Release(factory);
                streamKeep?.Dispose();
            }
        }

        /// <summary>
        /// Packaged: <c>CreateAsync(stream)</c> already opened JXR.
        /// QI <c>IWinRTObject.ThisPtr</c> to <c>IWICBitmapDecoder</c> /
        /// frame and CopyPixels float/half. Else wrap the RAS as
        /// <c>IStream</c> and <c>CreateDecoderFromStream</c>.
        /// </summary>
        public static HdrFrame? CopyFloatFromWinrt(
            BitmapDecoder sniffDecoder,
            BitmapFrame? sniffFrame,
            IRandomAccessStream ras,
            HdrProbe probe,
            int viewportPixelWidth,
            int viewportPixelHeight,
            ImageScaling scaling,
            bool wantRgba,
            bool measure,
            CancellationToken cancellation)
        {
            WicNative.IWICImagingFactory? factory = null;
            WicNative.IWICBitmapDecoder? decoder = null;
            WicNative.IWICBitmapFrameDecode? frame = null;
            IntPtr istream = IntPtr.Zero;
            try
            {
                factory = CreateFactory();
                var qiHr = unchecked((int)WicNative.WicDecoderOpen.NoInterface);
                decoder = QueryWinrtCom<WicNative.IWICBitmapDecoder>(
                    sniffDecoder, WicNative.IidBitmapDecoder, out qiHr);
                if (decoder is not null)
                {
                    try
                    {
                        decoder.GetFrame(0, out frame);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        frame = null;
                    }
                }

                if (frame is null && sniffFrame is not null)
                {
                    frame = QueryWinrtCom<WicNative.IWICBitmapFrameDecode>(
                        sniffFrame, WicNative.IidBitmapFrameDecode, out qiHr);
                }

                if (frame is not null)
                {
                    var packed = CopyOpenedFrame(factory, frame, cancellation);
                    if (packed is not null)
                    {
                        if (!GalleryPresent.JxrWinrtClampsHdr(packed.Value.Format))
                        {
                            LastWicError = null;
                            return FinishJxrFromPacked(packed.Value, probe, viewportPixelWidth, viewportPixelHeight, scaling, wantRgba, measure, cancellation);
                        }

                        LastWicError = WicNative.WicDecoderOpen.Failed(
                            WicNative.WicDecoderOpen.UnormClamp);
                    }
                }
                else
                {
                    LastWicError = WicNative.WicDecoderOpen.Failed(
                        WicNative.WicDecoderOpen.Qi,
                        qiHr);
                }

                Release(frame);
                frame = null;
                Release(decoder);
                decoder = null;

                if (!TryOpenFromRandomAccessStream(factory, ras, out decoder, out istream, out var rasHr)
                    || decoder is null)
                {
                    LastWicError = WicNative.WicDecoderOpen.Failed(
                        WicNative.WicDecoderOpen.Ras,
                        rasHr != 0 ? rasHr : qiHr);
                    return null;
                }

                decoder.GetFrame(0, out frame);
                if (frame is null)
                {
                    LastWicError = WicNative.WicDecoderOpen.Failed(
                        WicNative.WicDecoderOpen.Ras);
                    return null;
                }

                var fromRas = CopyOpenedFrame(factory, frame, cancellation);
                if (fromRas is null)
                {
                    return null;
                }

                if (GalleryPresent.JxrWinrtClampsHdr(fromRas.Value.Format))
                {
                    LastWicError = WicNative.WicDecoderOpen.Failed(
                        WicNative.WicDecoderOpen.UnormClamp);
                    return null;
                }

                LastWicError = null;
                return FinishJxrFromPacked(fromRas.Value, probe, viewportPixelWidth, viewportPixelHeight, scaling, wantRgba, measure, cancellation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastWicError ??= WicNative.WicDecoderOpen.Failed(
                    WicNative.WicDecoderOpen.Qi,
                    ex);
                return null;
            }
            finally
            {
                Release(frame);
                Release(decoder);
                Release(factory);
                if (istream != IntPtr.Zero)
                {
                    Marshal.Release(istream);
                }
            }
        }

        private static HdrFrame? FinishJxrFromPacked(
            (byte[] Data, HdrPackedFormat Format, int Width, int Height, int NativeWidth, int NativeHeight, uint Orientation) packed,
            HdrProbe probe,
            int viewportPixelWidth,
            int viewportPixelHeight,
            ImageScaling scaling,
            bool wantRgba,
            bool measure,
            CancellationToken cancellation)
        {
            var converted = Convert(
                packed.Data,
                packed.Format,
                packed.Width,
                packed.Height,
                probe,
                wantRgba,
                cancellation);
            if (converted is null)
            {
                LastWicError = "WIC convert failed";
                return null;
            }

            var rgba = converted.Value.Rgba;
            var width = packed.Width;
            var height = packed.Height;
            if (wantRgba && rgba is not null && packed.Orientation is >= 2 and <= 8)
            {
                (rgba, width, height) = HdrPixels.OrientScrgbRgba(
                    rgba, packed.Width, packed.Height, packed.Orientation);
            }
            else
            {
                (width, height) = HdrPixels.OrientedSize(
                    packed.Width, packed.Height, packed.Orientation);
            }

            var (decodeW, decodeH) = measure
                ? GalleryPresent.MeasureDecodeSize(
                    packed.NativeWidth,
                    packed.NativeHeight,
                    viewportPixelWidth,
                    viewportPixelHeight)
                : GalleryPresent.PresentDecodeSize(
                    packed.NativeWidth,
                    packed.NativeHeight,
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

            return new HdrFrame
            {
                ScrgbRgba = rgba ?? [],
                Width = width,
                Height = height,
                NativeWidth = packed.NativeWidth,
                NativeHeight = packed.NativeHeight,
                MaxNits = converted.Value.MaxNits,
                AvgNits = converted.Value.AvgNits,
                MinNits = converted.Value.MinNits,
                MaxScrgb = converted.Value.MaxScrgb
            };
        }

        private static T? QueryWinrtCom<T>(object? winrt, Guid iid, out int hr) where T : class
        {
            hr = unchecked((int)WicNative.WicDecoderOpen.NoInterface);
            if (winrt is null)
            {
                return null;
            }

            IntPtr unk;
            var releaseOuter = false;
            try
            {
                if (winrt is IWinRTObject obj)
                {
                    unk = obj.NativeObject.ThisPtr;
                }
                else
                {
                    unk = Marshal.GetIUnknownForObject(winrt);
                    releaseOuter = true;
                }
            }
            catch
            {
                return null;
            }

            if (unk == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var id = iid;
                hr = Marshal.QueryInterface(unk, ref id, out var ppv);
                if (hr < 0 || ppv == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    return WicNative.TypedUniqueFromIUnknown<T>(ppv);
                }
                finally
                {
                    Marshal.Release(ppv);
                }
            }
            finally
            {
                if (releaseOuter)
                {
                    Marshal.Release(unk);
                }
            }
        }

        private static bool TryOpenFromRandomAccessStream(
            WicNative.IWICImagingFactory factory,
            IRandomAccessStream ras,
            out WicNative.IWICBitmapDecoder? decoder,
            out IntPtr istreamHold,
            out int hr)
        {
            decoder = null;
            istreamHold = IntPtr.Zero;
            hr = unchecked((int)0x80004005);
            try
            {
                if (ras is not IWinRTObject winrt)
                {
                    return false;
                }

                var unk = winrt.NativeObject.ThisPtr;
                if (unk == IntPtr.Zero)
                {
                    return false;
                }

                var iid = WicNative.IidStream;
                hr = CreateStreamOverRandomAccessStream(unk, ref iid, out var istream);
                if (hr < 0 || istream == IntPtr.Zero)
                {
                    return false;
                }

                istreamHold = istream;
                object streamObj;
                try
                {
                    streamObj = Marshal.GetObjectForIUnknown(istream);
                }
                catch
                {
                    hr = unchecked((int)WicNative.WicDecoderOpen.NoInterface);
                    Marshal.Release(istream);
                    istreamHold = IntPtr.Zero;
                    return false;
                }

                hr = factory.CreateDecoderFromStream(streamObj, IntPtr.Zero, 0, out decoder);
                if (hr < 0 || decoder is null)
                {
                    decoder = null;
                    Marshal.Release(istream);
                    istreamHold = IntPtr.Zero;
                    return false;
                }

                return true;
            }
            catch
            {
                decoder = null;
                if (istreamHold != IntPtr.Zero)
                {
                    Marshal.Release(istreamHold);
                    istreamHold = IntPtr.Zero;
                }

                hr = unchecked((int)0x80004005);
                return false;
            }
        }

        private static (byte[] Data, HdrPackedFormat Format, int Width, int Height, int NativeWidth, int NativeHeight, uint Orientation)? CopyOpenedFrame(
            WicNative.IWICImagingFactory factory,
            WicNative.IWICBitmapFrameDecode frame,
            CancellationToken cancellation)
        {
            var orientation = ReadOrientation(frame);
            return CopyOpenedSource(factory, WicNative.AsSource(frame), orientation, cancellation);
        }

        private static (byte[] Data, HdrPackedFormat Format, int Width, int Height, int NativeWidth, int NativeHeight, uint Orientation)? CopyOpenedSource(
            WicNative.IWICImagingFactory factory,
            WicNative.IWICBitmapSource source,
            uint orientation,
            CancellationToken cancellation)
        {
            WicNative.IWICFormatConverter? converter = null;
            try
            {
                source.GetSize(out var storedW, out var storedH);
                if (storedW == 0 || storedH == 0 || storedW > 16384 || storedH > 16384)
                {
                    LastWicError = "WIC size";
                    return null;
                }

                var (nativeW, nativeH) = HdrPixels.OrientedSize((int)storedW, (int)storedH, orientation);
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

                Bytes = null;
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


        private const uint FileShareReadWrite = 3;
        private const uint OpenExisting = 3;
        private static readonly IntPtr InvalidHandle = new(-1);

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

        [DllImport("shcore.dll", PreserveSig = true)]
        private static extern int CreateStreamOverRandomAccessStream(
            IntPtr punk,
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
