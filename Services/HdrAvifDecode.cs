using System.Runtime.InteropServices;
using Palace.Helpers;
using Starward.Codec.AVIF;

namespace Palace.Services;

/// <summary>
/// HDR AVIF → linear scRGB via libavif (SKIV path). Inbox WIC drops chroma
/// on identity-matrix 4:4:4; <c>avifImageYUVToRGB</c> keeps GBR intact.
/// </summary>
internal static unsafe class HdrAvifDecode
{
    public static HdrFrame? TryLoad(
        string path,
        HdrProbe probe,
        int viewportPixelWidth,
        int viewportPixelHeight,
        ImageScaling scaling,
        CancellationToken cancellation)
    {
        if (!CanDecode(path, probe))
        {
            return null;
        }

        return Decode(
            path,
            probe,
            viewportPixelWidth,
            viewportPixelHeight,
            scaling,
            wantRgba: true,
            cancellation) is { } packed
            ? new HdrFrame
            {
                ScrgbRgba = packed.Rgba!,
                Width = packed.Width,
                Height = packed.Height,
                NativeWidth = packed.NativeWidth,
                NativeHeight = packed.NativeHeight,
                MaxNits = packed.MaxNits,
                AvgNits = packed.AvgNits,
                MinNits = packed.MinNits,
                MaxScrgb = packed.MaxScrgb
            }
            : null;
    }

    public static HdrStats? TryMeasure(string path, HdrProbe probe, CancellationToken cancellation)
    {
        if (!CanDecode(path, probe))
        {
            return null;
        }

        return Decode(
            path,
            probe,
            viewportPixelWidth: 0,
            viewportPixelHeight: 0,
            ImageScaling.Actual,
            wantRgba: false,
            cancellation) is { } packed
            ? new HdrStats(
                packed.MaxNits,
                packed.AvgNits,
                packed.MinNits,
                packed.MaxScrgb,
                packed.NativeWidth,
                packed.NativeHeight)
            : null;
    }

    private static bool CanDecode(string path, HdrProbe probe) =>
        probe.Kind == HdrKind.HdrAvif
        && PathSafe.IsAvif(path)
        && CloudFile.Exists(path)
        && !CloudFile.IsOnlineOnly(path);

    private static (
        float[]? Rgba,
        int Width,
        int Height,
        int NativeWidth,
        int NativeHeight,
        float MaxNits,
        float AvgNits,
        float MinNits,
        float MaxScrgb)? Decode(
        string path,
        HdrProbe probe,
        int viewportPixelWidth,
        int viewportPixelHeight,
        ImageScaling scaling,
        bool wantRgba,
        CancellationToken cancellation)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }

        if (bytes.Length == 0)
        {
            return null;
        }

        cancellation.ThrowIfCancellationRequested();
        fixed (byte* pBytes = bytes)
        {
            var decoder = avifNativeMethod.avifDecoderCreate();
            if (decoder == null)
            {
                return null;
            }

            try
            {
                decoder->maxThreads = Math.Clamp(Environment.ProcessorCount, 1, 64);
                decoder->imageSizeLimit = 16384u * 16384u;
                decoder->imageDimensionLimit = 16384;
                if (avifNativeMethod.avifDecoderSetIOMemory(
                        decoder, (IntPtr)pBytes, (UIntPtr)(uint)bytes.Length) != avifResult.OK)
                {
                    return null;
                }

                if (avifNativeMethod.avifDecoderParse(decoder) != avifResult.OK)
                {
                    return null;
                }

                if (avifNativeMethod.avifDecoderNextImage(decoder) != avifResult.OK)
                {
                    return null;
                }

                var image = decoder->image;
                if (image == null)
                {
                    return null;
                }

                var nativeW = (int)image->width;
                var nativeH = (int)image->height;
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

                if (decodeW < nativeW || decodeH < nativeH)
                {
                    avifDiagnostics diag = default;
                    if (avifNativeMethod.avifImageScale(
                            image, (uint)decodeW, (uint)decodeH, &diag) == avifResult.OK)
                    {
                        decodeW = (int)image->width;
                        decodeH = (int)image->height;
                    }
                    else
                    {
                        decodeW = nativeW;
                        decodeH = nativeH;
                    }
                }

                cancellation.ThrowIfCancellationRequested();
                avifRGBImage rgb = default;
                avifNativeMethod.avifRGBImageSetDefaults(&rgb, image);
                rgb.depth = 16;
                rgb.format = avifRGBFormat.RGBA;
                rgb.isFloat = false;
                rgb.maxThreads = decoder->maxThreads;
                if (avifNativeMethod.avifRGBImageAllocatePixels(&rgb) != avifResult.OK)
                {
                    return null;
                }

                try
                {
                    if (avifNativeMethod.avifImageYUVToRGB(image, &rgb) != avifResult.OK
                        || rgb.pixels == IntPtr.Zero
                        || rgb.rowBytes == 0)
                    {
                        return null;
                    }

                    return ToScrgb(
                        rgb.pixels,
                        (int)rgb.rowBytes,
                        decodeW,
                        decodeH,
                        nativeW,
                        nativeH,
                        probe,
                        wantRgba,
                        cancellation);
                }
                finally
                {
                    avifNativeMethod.avifRGBImageFreePixels(&rgb);
                }
            }
            finally
            {
                avifNativeMethod.avifDecoderDestroy(decoder);
            }
        }
    }

    private static (
        float[]? Rgba,
        int Width,
        int Height,
        int NativeWidth,
        int NativeHeight,
        float MaxNits,
        float AvgNits,
        float MinNits,
        float MaxScrgb)? ToScrgb(
        IntPtr pixels,
        int rowBytes,
        int width,
        int height,
        int nativeWidth,
        int nativeHeight,
        HdrProbe probe,
        bool wantRgba,
        CancellationToken cancellation)
    {
        var count = width * height;
        if (count <= 0 || rowBytes < width * 8)
        {
            return null;
        }

        var rgba = wantRgba ? new float[count * 4] : null;
        var maxY = 0f;
        var minY = float.MaxValue;
        var sumY = 0.0;
        var maxScrgb = 0f;
        var row = new byte[rowBytes];
        for (var y = 0; y < height; y++)
        {
            if ((y & 0x3F) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            Marshal.Copy(pixels + (y * rowBytes), row, 0, rowBytes);
            for (var x = 0; x < width; x++)
            {
                var o = x * 8;
                var er = BitConverter.ToUInt16(row, o) / 65535f;
                var eg = BitConverter.ToUInt16(row, o + 2) / 65535f;
                var eb = BitConverter.ToUInt16(row, o + 4) / 65535f;
                var a = BitConverter.ToUInt16(row, o + 6) / 65535f;
                HdrColor.EncodedRgbToScrgb(
                    er, eg, eb, probe.Transfer, probe.CicpPrimaries,
                    out var sr, out var sg, out var sb);
                var nitsY = HdrColor.SourcePrimaryNitsY(
                    er, eg, eb, probe.Transfer, probe.CicpPrimaries);
                maxY = Math.Max(maxY, nitsY);
                minY = Math.Min(minY, nitsY);
                sumY += nitsY;
                maxScrgb = Math.Max(maxScrgb, Math.Max(sr, Math.Max(sg, sb)));
                if (rgba is not null)
                {
                    var i = ((y * width) + x) * 4;
                    rgba[i] = sr;
                    rgba[i + 1] = sg;
                    rgba[i + 2] = sb;
                    rgba[i + 3] = a;
                }
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

        return (rgba, width, height, nativeWidth, nativeHeight, maxY, (float)(sumY / count), minY, maxScrgb);
    }
}
