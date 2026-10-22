using System.Runtime.InteropServices;
using Palace.Helpers;
using Starward.Codec.JpegXL;
using Starward.Codec.JpegXL.CMS;
using Starward.Codec.JpegXL.CodeStream;
using Starward.Codec.JpegXL.Decode;

namespace Palace.Services;

/// <summary>
/// Built-in JPEG XL decode via the libjxl (BSD-3) natives that ship with
/// Starward.Codec — no Windows JPEG XL extension required. Output is linear
/// scRGB float RGBA (libjxl's CMS converts PQ/HLG/wide gamut; HDR intensity
/// is rescaled so 1.0 = 80 nits). Used for mosaic thumbs and HDR present.
/// </summary>
internal static class JxlDecode
{
    public sealed record Frame(float[] Rgba, int Width, int Height);

    public static Frame? TryDecodeFile(string path, int maxPixels = 120_000_000)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return TryDecode(bytes, maxPixels);
    }

    public static Frame? TryDecode(byte[] bytes, int maxPixels = 120_000_000)
    {
        var dec = (nint)0;
        var input = default(GCHandle);
        var output = default(GCHandle);
        try
        {
            if ((uint)JxlDecoderLite.CheckSignature(bytes) <= 1u)
            {
                return null;
            }

            dec = JxlDecoderNativeMethod.JxlDecoderCreate();
            if (dec == 0)
            {
                return null;
            }

            JxlDecoderNativeMethod.JxlDecoderSetCms(dec, JxlCmsInterface.GetDefault());
            if (JxlDecoderNativeMethod.JxlDecoderSubscribeEvents(dec,
                    JxlDecoderStatus.BasicInfo | JxlDecoderStatus.ColorEncoding | JxlDecoderStatus.FullImage)
                != JxlDecoderStatus.Success)
            {
                return null;
            }

            input = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            JxlDecoderNativeMethod.JxlDecoderSetInput(dec, input.AddrOfPinnedObject(), (nuint)bytes.Length);
            JxlDecoderNativeMethod.JxlDecoderCloseInput(dec);

            var info = new JxlBasicInfo();
            var format = new JxlPixelFormat
            {
                NumChannels = 4,
                DataType = JxlDataType.Float,
                Endianness = JxlEndianness.Native,
                Align = 0
            };
            float[]? pixels = null;
            for (var guard = 0; guard < 64; guard++)
            {
                var status = JxlDecoderNativeMethod.JxlDecoderProcessInput(dec);
                switch (status)
                {
                    case JxlDecoderStatus.BasicInfo:
                        if (JxlDecoderNativeMethod.JxlDecoderGetBasicInfo(dec, ref info) != JxlDecoderStatus.Success
                            || info.XSize == 0 || info.YSize == 0
                            || (long)info.XSize * info.YSize > maxPixels)
                        {
                            return null;
                        }

                        break;
                    case JxlDecoderStatus.ColorEncoding:
                        // Linear sRGB primaries, values above 1.0 kept (scRGB).
                        var linear = JxlColorEncoding.LinearSRGB;
                        if (JxlDecoderNativeMethod.JxlDecoderSetOutputColorProfile(dec, in linear, 0, 0)
                            != JxlDecoderStatus.Success)
                        {
                            JxlDecoderNativeMethod.JxlDecoderSetPreferredColorProfile(dec, in linear);
                        }

                        break;
                    case JxlDecoderStatus.NeedImageOutBuffer:
                        nuint size = 0;
                        if (JxlDecoderNativeMethod.JxlDecoderImageOutBufferSize(dec, in format, ref size) != JxlDecoderStatus.Success)
                        {
                            return null;
                        }

                        pixels = new float[(int)(size / sizeof(float))];
                        output = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                        if (JxlDecoderNativeMethod.JxlDecoderSetImageOutBuffer(dec, in format, output.AddrOfPinnedObject(), size)
                            != JxlDecoderStatus.Success)
                        {
                            return null;
                        }

                        break;
                    case JxlDecoderStatus.FullImage:
                        // First frame is enough for a still / animation poster.
                        return pixels is null ? null : new Frame(Normalize(pixels, info), (int)info.XSize, (int)info.YSize);
                    case JxlDecoderStatus.Success:
                        return pixels is null ? null : new Frame(Normalize(pixels, info), (int)info.XSize, (int)info.YSize);
                    default:
                        return null;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                                       or JxlDecodeException or OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (output.IsAllocated) output.Free();
            if (input.IsAllocated) input.Free();
            if (dec != 0) JxlDecoderNativeMethod.JxlDecoderDestroy(dec);
        }
    }

    /// <summary>
    /// libjxl float output is relative to the image's intensity target
    /// (1.0 = IntensityTarget nits). Rescale so 1.0 = 80 nits SDR white (scRGB)
    /// for HDR images; SDR images (target ≤ 255 nits) stay 0..1.
    /// </summary>
    private static float[] Normalize(float[] rgba, JxlBasicInfo info)
    {
        HdrJxlPresent.ApplyIntensityScale(
            rgba,
            HdrJxlPresent.ScrgbScaleFromIntensityTarget(info.IntensityTarget));

        if (info.AlphaBits == 0)
        {
            for (var i = 3; i < rgba.Length; i += 4)
            {
                rgba[i] = 1f;
            }
        }

        return rgba;
    }
}
