using Palace.Imaging;

namespace Palace.Helpers;

/// <summary>
/// P3: mosaic / preview thumbs tonemap via <see cref="Palace.Imaging"/> —
/// lazy <see cref="NormalizedFrame"/> from present scRGB, then thumb LUT →
/// BGRA8 sRGB JPEG. WinUI-free for <c>Palace.Tests</c>. Does not wire HDR
/// present (P5) or invent deferred Doktor R1/R3/R4 wording.
/// </summary>
public static class ImagingThumbToneMap
{
    public const int ThumbLutSize = 256;

    /// <summary>
    /// Lazy Working from absolute present scRGB (1.0 = 80 nits, BT.709).
    /// Used when decode skipped native Working (Fit/thumb box-scale — B2 / S2).
    /// </summary>
    public static NormalizeOptions OptionsForLazyScrgb(float? metadataPeakNits = null) =>
        ImagingStillNormalize.OptionsForJxr(metadataPeakNits);

    /// <summary>
    /// Lazy Working from relative linear RGB (SDR / WideGamut JXL): 1.0 = display
    /// white → Working 1.0 via <see cref="TransferKind.LinearScene"/> @ 203 nits.
    /// Do not use <see cref="OptionsForLazyScrgb"/> here — that maps relative
    /// white to ~80/203 and Option A JPEG ~168 instead of legacy 255.
    /// </summary>
    public static NormalizeOptions OptionsForLazyRelative(float? metadataPeakNits = null) =>
        new(
            TransferKind.LinearScene,
            ColorPrimaries.Bt709,
            WhiteLuminance: ImagingConstants.RefWhiteNits,
            MetadataPeakNits: metadataPeakNits);

    public static NormalizeOptions OptionsForLazyBuffer(
        bool absoluteScrgb,
        float? metadataPeakNits = null) =>
        absoluteScrgb
            ? OptionsForLazyScrgb(metadataPeakNits)
            : OptionsForLazyRelative(metadataPeakNits);

    /// <summary>
    /// Absolute present-scRGB when HDR JXL (PQ/HLG / IT-scaled) or any buffer
    /// whose libjxl <c>ScaleApplied</c> is not 1. Relative SDR / WideGamut keep
    /// <c>ScaleApplied == 1</c>.
    /// </summary>
    public static bool IsAbsoluteScrgbThumbBuffer(HdrKind kind, float scaleApplied) =>
        kind == HdrKind.HdrJxl || Math.Abs(scaleApplied - 1f) > 1e-5f;

    public static ToneMapParams ParamsFor(ImageStats stats, ImagingPathKind pathKind)
    {
        // Thumb and Full share Option A / Sdr203 for now; pathKind is the
        // LutCacheKey / encode branch flag (LUT vs direct MapFrame).
        _ = pathKind;
        return new ToneMapParams(stats, DisplayTarget.Sdr203, ExposureEv: 0f);
    }

    public static NormalizedFrame? TryWorkingFromScrgb(
        ReadOnlySpan<float> scrgbRgba,
        int width,
        int height,
        float? metadataPeakNits = null) =>
        TryWorkingFromBuffer(scrgbRgba, width, height, absoluteScrgb: true, metadataPeakNits);

    public static NormalizedFrame? TryWorkingFromBuffer(
        ReadOnlySpan<float> rgba,
        int width,
        int height,
        bool absoluteScrgb,
        float? metadataPeakNits = null) =>
        ImagingStillNormalize.TryRun(
            rgba,
            width,
            height,
            OptionsForLazyBuffer(absoluteScrgb, metadataPeakNits));

    /// <summary>
    /// Tonemap <paramref name="frame"/> for <paramref name="pathKind"/> and
    /// encode BGRA8 sRGB. Null on empty / failed encode.
    /// </summary>
    public static byte[]? TryEncodeBgra(
        NormalizedFrame frame,
        ImagingPathKind pathKind,
        CancellationToken cancellationToken = default)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Rgba.Length < frame.PixelCount * 4)
        {
            return null;
        }

        try
        {
            var stats = Analyze.Run(frame);
            var p = ParamsFor(stats, pathKind);
            if (pathKind == ImagingPathKind.Thumb)
            {
                var lut = ToneMapLut.Bake(p, ThumbLutSize);
                return EncodeWithLut(frame, lut, cancellationToken);
            }

            var mapped = ToneMapper.MapFrame(frame, p);
            return EncodeLinearWorking(mapped, frame.Width, frame.Height, cancellationToken);
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
    /// Scale buffer to <paramref name="maxSide"/>, build lazy Working, tonemap
    /// with the thumb LUT, fall back to legacy <see cref="ToneMap"/> when Imaging
    /// fails. <paramref name="absoluteScrgb"/> true = Windows/present scRGB
    /// (1.0 = 80 nits); false = relative SDR white → Working 1.0.
    /// </summary>
    public static (byte[] Bgra, int Width, int Height, bool UsedImaging) FromScrgb(
        float[] rgba,
        int width,
        int height,
        int maxSide,
        float? metadataPeakNits = null,
        CancellationToken cancellationToken = default,
        bool absoluteScrgb = true)
    {
        var (w, h) = StillDecode.Fit(width, height, maxSide);
        var scaled = w == width && h == height
            ? rgba
            : HdrPixels.BoxScaleScrgb(rgba, width, height, w, h, cancellationToken);

        var working = TryWorkingFromBuffer(scaled, w, h, absoluteScrgb, metadataPeakNits);
        if (working is not null)
        {
            var bgra = TryEncodeBgra(working, ImagingPathKind.Thumb, cancellationToken);
            if (bgra is not null)
            {
                return (bgra, w, h, true);
            }
        }

        return (ToneMap.ScrgbToBgra8(scaled, w, h, cancellationToken), w, h, false);
    }

    private static byte[] EncodeWithLut(
        NormalizedFrame frame,
        ToneMapLut lut,
        CancellationToken cancellationToken)
    {
        var count = frame.PixelCount;
        var src = frame.Rgba;
        var output = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            if ((i & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var o = i * 4;
            lut.MapPixel(src[o], src[o + 1], src[o + 2], out var r, out var g, out var b);
            WriteSrgbBgra(r, g, b, src[o + 3], output, o);
        }

        return output;
    }

    private static byte[] EncodeLinearWorking(
        float[] mappedRgba,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var count = checked(width * height);
        var output = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            if ((i & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var o = i * 4;
            WriteSrgbBgra(
                mappedRgba[o],
                mappedRgba[o + 1],
                mappedRgba[o + 2],
                mappedRgba[o + 3],
                output,
                o);
        }

        return output;
    }

    private static void WriteSrgbBgra(
        float wr,
        float wg,
        float wb,
        float a,
        byte[] output,
        int offset)
    {
        Primaries.FromBt2020(wr, wg, wb, ColorPrimaries.Bt709, out var r, out var g, out var b);
        output[offset] = ToSrgb8(b);
        output[offset + 1] = ToSrgb8(g);
        output[offset + 2] = ToSrgb8(r);
        output[offset + 3] = (byte)MathF.Round(Math.Clamp(ColorMath.Sanitize(a), 0f, 1f) * 255f);
    }

    private static byte ToSrgb8(float linear)
    {
        var v = Math.Clamp(ColorMath.Sanitize(linear), 0f, 1f);
        return (byte)MathF.Round(ColorMath.SrgbOetf(v) * 255f);
    }
}
