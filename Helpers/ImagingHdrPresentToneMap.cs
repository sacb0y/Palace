using Palace.Imaging;

namespace Palace.Helpers;

/// <summary>
/// P5: HDR-display PQ/HLG present via <see cref="Palace.Imaging"/> —
/// BT.2390 EETF with display <see cref="DisplayTarget.Ld"/> → BT.709 linear
/// for the scRGB swapchain. WinUI-free for <c>Palace.Tests</c>. Highlight
/// desat stays structurally dead under Option A (P7 B0 docs); does not invent
/// A1–A4 engagement or deferred Doktor A3 / R1/R3/R4. GPU present still
/// uploads CPU FP16; reference HLSL lives under <c>Palace.Imaging/Shaders</c>
/// for CPU–GPU agreement.
/// </summary>
public static class ImagingHdrPresentToneMap
{
    /// <summary>
    /// Unknown / dummy DXGI peak → 10 000 nits (same as
    /// <see cref="GalleryPresent.RasterizeClipScrgb"/>).
    /// </summary>
    public static DisplayTarget DisplayForPresent(float displayPeakNits)
    {
        var peak = displayPeakNits > 0 && float.IsFinite(displayPeakNits)
            ? displayPeakNits
            : 10_000f;
        return DisplayTarget.Hdr(peak);
    }

    /// <summary>
    /// HDR Option A / BT.2390 → BT.709 linear Working-referred (1.0 ≈ paper /
    /// 203 nits). Caller lifts ×203/80 via <see cref="GalleryPresent.PresentMapImagingHdr"/>.
    /// Null on empty / failed map.
    /// </summary>
    public static float[]? TryMapToPresentScrgb(
        NormalizedFrame working,
        DisplayTarget display,
        float exposureEv = 0f,
        CancellationToken cancellationToken = default)
    {
        if (!display.IsHdrOutput)
        {
            return null;
        }

        if (working.Width <= 0
            || working.Height <= 0
            || working.Rgba.Length < working.PixelCount * 4)
        {
            return null;
        }

        try
        {
            var stats = Analyze.Run(working);
            var p = new ToneMapParams(stats, display, exposureEv);
            var mapped = ToneMapper.MapFrame(working, p);
            return ToBt709Linear(mapped, working.Width, working.Height, cancellationToken);
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
    /// Resolve Working then HDR tonemap → present BT.709 linear. Null → caller
    /// keeps absolute scRGB passthrough.
    /// </summary>
    public static float[]? TryMapFrameToPresentScrgb(
        NormalizedFrame? working,
        ReadOnlySpan<float> scrgbRgba,
        int width,
        int height,
        float displayPeakNits,
        float? metadataPeakNits = null,
        float exposureEv = 0f,
        CancellationToken cancellationToken = default)
    {
        var resolved = ImagingFullToneMap.ResolveWorking(
            working, scrgbRgba, width, height, metadataPeakNits);
        if (resolved is null)
        {
            return null;
        }

        return TryMapToPresentScrgb(
            resolved,
            DisplayForPresent(displayPeakNits),
            exposureEv,
            cancellationToken);
    }

    private static float[] ToBt709Linear(
        float[] mappedRgba,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var count = checked(width * height);
        var output = new float[count * 4];
        for (var i = 0; i < count; i++)
        {
            if ((i & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var o = i * 4;
            Primaries.FromBt2020(
                mappedRgba[o],
                mappedRgba[o + 1],
                mappedRgba[o + 2],
                ColorPrimaries.Bt709,
                out var r,
                out var g,
                out var b);
            output[o] = ColorMath.Sanitize(r);
            output[o + 1] = ColorMath.Sanitize(g);
            output[o + 2] = ColorMath.Sanitize(b);
            output[o + 3] = ColorMath.Sanitize(mappedRgba[o + 3]);
        }

        return output;
    }
}
