using System.Runtime.InteropServices;
using Palace.Helpers;
using Palace.Imaging;
using Starward.Codec.JpegXL;
using Starward.Codec.JpegXL.CMS;
using Starward.Codec.JpegXL.CodeStream;
using Starward.Codec.JpegXL.Decode;

#pragma warning disable CA1416 // Linked into Palace.Tests on Linux; natives are Windows-only.

namespace Palace.Services;

/// <summary>
/// Built-in JPEG XL decode via the libjxl (BSD-3) natives that ship with
/// Starward.Codec — no Windows JPEG XL extension required. Output is linear
/// scRGB float RGBA. Verifies actual transfer after CMS
/// (<c>GetColorAsEncodedProfile(TARGET_DATA)</c>): still-PQ/HLG is EOTF→nits→scRGB
/// (never intensity_target scale on PQ codes); linear Relative uses
/// intensity_target/80. Used for mosaic thumbs and HDR present.
/// Present callers pass <see cref="HdrJxlPresent.PresentMaxPixels"/> (~64 MP);
/// thumbs use <see cref="HdrJxlPresent.ThumbMaxPixels"/> (120 MP).
/// P2 also fills <see cref="Frame.Working"/> via <see cref="ImagingStillNormalize"/>
/// before the legacy scRGB convert.
/// </summary>
internal static class JxlDecode
{
    public sealed record Frame(
        float[] Rgba,
        int Width,
        int Height,
        float IntensityTarget = 0f,
        float ScaleApplied = 1f,
        JxlBufferTransfer BufferTransfer = JxlBufferTransfer.Unknown,
        NormalizedFrame? Working = null,
        NormalizeOptions? WorkingOptions = null);

    public static Frame? TryDecodeFile(
        string path,
        int maxPixels = HdrJxlPresent.PresentMaxPixels,
        HdrTransfer transfer = HdrTransfer.Srgb,
        int? cicpPrimaries = null,
        bool buildWorking = false,
        CancellationToken cancellation = default)
    {
        byte[] bytes;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return TryDecode(bytes, maxPixels, transfer, cicpPrimaries, buildWorking, cancellation);
    }

    public static Frame? TryDecode(
        byte[] bytes,
        int maxPixels = HdrJxlPresent.PresentMaxPixels,
        HdrTransfer transfer = HdrTransfer.Srgb,
        int? cicpPrimaries = null,
        bool buildWorking = false,
        CancellationToken cancellation = default)
    {
        var dec = (nint)0;
        var input = default(GCHandle);
        var output = default(GCHandle);
        try
        {
            cancellation.ThrowIfCancellationRequested();
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
            var actualBuffer = JxlBufferTransfer.Unknown;
            int? targetDataPrimaries = null;
            for (var guard = 0; guard < 64; guard++)
            {
                cancellation.ThrowIfCancellationRequested();
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
                        cancellation.ThrowIfCancellationRequested();
                        // Prefer Relative linear float. PQ/HLG and wide-gamut SDR
                        // use BT.2100 primaries so OOG stays negative after
                        // Bt2020→709. Then verify TARGET_DATA — if CMS left PQ,
                        // NormalizeToScrgb EOTFs instead of IT-scaling codes.
                        var linear = JxlColorEncoding.LinearSRGB;
                        linear.RenderingIntent = (JxlRenderingIntent)HdrJxlPresent.ScrgbOutputRenderingIntentRelative;
                        var wideOut = HdrJxlPresent.UsesBt2020LinearOutput(transfer, cicpPrimaries);
                        if (wideOut)
                        {
                            linear.Primaries = JxlPrimaries.BT2100;
                        }

                        var setOk = JxlDecoderNativeMethod.JxlDecoderSetOutputColorProfile(dec, in linear, 0, 0)
                            == JxlDecoderStatus.Success;
                        if (!setOk)
                        {
                            JxlDecoderNativeMethod.JxlDecoderSetPreferredColorProfile(dec, in linear);
                        }

                        var dataEnc = new JxlColorEncoding();
                        if (JxlDecoderNativeMethod.JxlDecoderGetColorAsEncodedProfile(
                                dec, JxlColorProfileTarget.Data, ref dataEnc)
                            == JxlDecoderStatus.Success)
                        {
                            actualBuffer = HdrJxlPresent.ResolveActualBuffer(
                                (int)dataEnc.TransferFunction, setOk);
                            targetDataPrimaries = HdrJxlPresent.CicpPrimariesFromJxlPrimaries(
                                (int)dataEnc.Primaries);
                        }

                        break;
                    case JxlDecoderStatus.NeedImageOutBuffer:
                        cancellation.ThrowIfCancellationRequested();
                        nuint size = 0;
                        if (JxlDecoderNativeMethod.JxlDecoderImageOutBufferSize(dec, in format, ref size) != JxlDecoderStatus.Success)
                        {
                            return null;
                        }

                        var floatCount = (long)(size / sizeof(float));
                        var expected = (long)info.XSize * info.YSize * 4;
                        if (info.XSize == 0 || floatCount <= 0 || floatCount > expected * 2
                            || floatCount > (long)maxPixels * 4)
                        {
                            return null;
                        }

                        pixels = new float[(int)floatCount];
                        output = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                        if (JxlDecoderNativeMethod.JxlDecoderSetImageOutBuffer(dec, in format, output.AddrOfPinnedObject(), size)
                            != JxlDecoderStatus.Success)
                        {
                            return null;
                        }

                        break;
                    case JxlDecoderStatus.FullImage:
                        return pixels is null
                            ? null
                            : Finish(
                                pixels, info, transfer, cicpPrimaries, actualBuffer, targetDataPrimaries,
                                buildWorking);
                    case JxlDecoderStatus.Success:
                        return pixels is null
                            ? null
                            : Finish(
                                pixels, info, transfer, cicpPrimaries, actualBuffer, targetDataPrimaries,
                                buildWorking);
                    default:
                        return null;
                }
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
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

    private static Frame Finish(
        float[] rgba,
        JxlBasicInfo info,
        HdrTransfer probeTransfer,
        int? probeCicpPrimaries,
        JxlBufferTransfer actualBuffer,
        int? targetDataPrimaries,
        bool buildWorking)
    {
        // Still-encoded EOTF uses TARGET_DATA primaries (not probe ?? 9).
        // Linear BT.2020→709 keeps the probe (wide-out request matched that).
        var stillEncoded = actualBuffer is JxlBufferTransfer.Pq or JxlBufferTransfer.Hlg
            || (actualBuffer == JxlBufferTransfer.Unknown
                && probeTransfer is HdrTransfer.Pq or HdrTransfer.Hlg);
        var primaries = stillEncoded ? targetDataPrimaries : probeCicpPrimaries;

        // B2: Normalize.Run reads rgba into a new output (no RunCopy clone);
        // failures / over-budget → Working=null; then legacy mutates rgba in place.
        NormalizedFrame? working = null;
        NormalizeOptions? workingOpts = null;
        if (buildWorking)
        {
            workingOpts = ImagingStillNormalize.OptionsForJxl(
                actualBuffer,
                probeTransfer,
                primaries,
                info.IntensityTarget);
            working = ImagingStillNormalize.TryRun(
                rgba, (int)info.XSize, (int)info.YSize, workingOpts.Value);
            if (working is null)
            {
                workingOpts = null;
            }
        }

        var norm = HdrJxlPresent.NormalizeToScrgb(
            rgba,
            info.IntensityTarget,
            probeTransfer,
            actualBuffer,
            primaries);

        if (info.AlphaBits == 0)
        {
            for (var i = 3; i < rgba.Length; i += 4)
            {
                rgba[i] = 1f;
            }
        }

        return new Frame(
            rgba,
            (int)info.XSize,
            (int)info.YSize,
            norm.IntensityTarget,
            norm.ScaleApplied,
            norm.BufferTransfer,
            working,
            workingOpts);
    }
}
