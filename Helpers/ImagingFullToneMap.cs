using Palace.Imaging;

namespace Palace.Helpers;

/// <summary>
/// P4: full-view SDR tonemap via <see cref="Palace.Imaging"/> —
/// <see cref="ImagingPathKind.Full"/> (direct <see cref="ToneMapper.MapFrame"/>)
/// → BT.709 linear for the scRGB swapchain. WinUI-free for <c>Palace.Tests</c>.
/// Does not wire HDR-display PQ/HLG present (P5), delete legacy Reinhard (P6),
/// or invent deferred Doktor A3 / R1/R3/R4 wording.
/// </summary>
public static class ImagingFullToneMap
{
    /// <summary>
    /// Option A Full → BT.709 linear display-referred (1.0 ≈ paper / display
    /// white). Same linear values thumbs feed into sRGB OETF for JPEG.
    /// Null on empty / failed map.
    /// </summary>
    public static float[]? TryMapToPresentScrgb(
        NormalizedFrame working,
        CancellationToken cancellationToken = default)
    {
        if (working.Width <= 0
            || working.Height <= 0
            || working.Rgba.Length < working.PixelCount * 4)
        {
            return null;
        }

        try
        {
            var stats = Analyze.Run(working);
            var p = ImagingThumbToneMap.ParamsFor(stats, ImagingPathKind.Full);
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
    /// Prefer eager <paramref name="working"/> when size matches present;
    /// else lazy absolute scRGB → Working (present buffers are nits/80).
    /// </summary>
    public static NormalizedFrame? ResolveWorking(
        NormalizedFrame? working,
        ReadOnlySpan<float> scrgbRgba,
        int width,
        int height,
        float? metadataPeakNits = null)
    {
        if (working is not null
            && working.Width == width
            && working.Height == height
            && working.Rgba.Length >= working.PixelCount * 4)
        {
            return working;
        }

        return ImagingThumbToneMap.TryWorkingFromScrgb(
            scrgbRgba, width, height, metadataPeakNits);
    }

    /// <summary>
    /// Resolve Working then Option A Full → present scRGB. Null → caller
    /// keeps legacy Reinhard.
    /// </summary>
    public static float[]? TryMapFrameToPresentScrgb(
        NormalizedFrame? working,
        ReadOnlySpan<float> scrgbRgba,
        int width,
        int height,
        float? metadataPeakNits = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveWorking(
            working, scrgbRgba, width, height, metadataPeakNits);
        return resolved is null
            ? null
            : TryMapToPresentScrgb(resolved, cancellationToken);
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
