using Palace.Helpers;
using Palace.Imaging;

namespace Palace.Services;

/// <summary>
/// App-host float RGBA loaders for Smooth V2 — wires JxlDecode / HdrAvifDecode /
/// HdrWicDecode. Registered into <see cref="SmoothSourceLoad"/> so
/// <c>Palace.Tests</c> stays free of WinUI/WIC. These decoders return upright
/// pixels (libjxl default orientation, AVIF irot/imir, WIC EXIF rotate), so
/// <c>Orientation</c> is 1; view rotation is applied at draw time on top.
/// </summary>
internal static class SmoothSourceAppLoad
{
    public static void Register()
    {
        SmoothSourceLoad.TryLoadJxl = LoadJxl;
        SmoothSourceLoad.TryLoadAvif = LoadAvif;
        SmoothSourceLoad.TryLoadWic = LoadWic;
    }

    private static SmoothSourceLoad.Loaded? LoadJxl(string path, CancellationToken ct)
    {
        var probe = HdrFile.ProbePath(path);
        var frame = JxlDecode.TryDecodeFile(
            path,
            maxPixels: (int)Math.Min(int.MaxValue, SmoothWorkingDecode.MaxSmoothWorkingPixels),
            transfer: probe.Transfer,
            cicpPrimaries: probe.CicpPrimaries,
            buildWorking: false,
            cancellation: ct);
        if (frame is null)
        {
            return null;
        }

        var opts = ImagingStillNormalize.OptionsForJxl(
            frame.BufferTransfer,
            probe.Transfer,
            probe.CicpPrimaries,
            frame.IntensityTarget);
        return new SmoothSourceLoad.Loaded(
            frame.Rgba,
            frame.Width,
            frame.Height,
            opts,
            Orientation: 1);
    }

    private static SmoothSourceLoad.Loaded? LoadAvif(string path, CancellationToken ct)
    {
        var probe = HdrFile.ProbePath(path);
        var frame = HdrAvifDecode.TryLoad(
            path,
            probe,
            viewportPixelWidth: 0,
            viewportPixelHeight: 0,
            scaling: ImageScaling.Actual,
            cancellation: ct,
            buildWorking: false);
        return PreferEncodedOrPresentScrgb(frame, probe);
    }

    private static SmoothSourceLoad.Loaded? LoadWic(string path, CancellationToken ct)
    {
        var probe = HdrFile.ProbePath(path);
        // Sync wait — Smooth decode already runs on a background Task.
        var result = HdrWicDecode.TryLoadAsync(
                path,
                probe,
                viewportPixelWidth: 0,
                viewportPixelHeight: 0,
                scaling: ImageScaling.Actual,
                cancellation: ct,
                buildWorking: false,
                retainEncoded: true)
            .GetAwaiter()
            .GetResult();
        return PreferEncodedOrPresentScrgb(result.Frame, probe);
    }

    /// <summary>
    /// Prefer <c>EncodedRgba + OptionsForEncoded</c> (same kernel as strip /
    /// Imaging buildWorking — HLG OOTF / SourceClipped). Fall back to present
    /// scRGB + <see cref="ImagingStillNormalize.OptionsForPresentScrgb"/> when
    /// encoded was not retained (YUV / identity-GBR pack paths).
    /// </summary>
    private static SmoothSourceLoad.Loaded? PreferEncodedOrPresentScrgb(
        HdrFrame? frame,
        HdrProbe probe)
    {
        if (frame is null || frame.Width < 1 || frame.Height < 1)
        {
            return null;
        }

        if (frame.EncodedRgba is { Length: > 0 } encoded
            && frame.WorkingOptions is { } opts)
        {
            return new SmoothSourceLoad.Loaded(
                encoded,
                frame.Width,
                frame.Height,
                opts,
                Orientation: 1);
        }

        if (frame.ScrgbRgba is null || frame.ScrgbRgba.Length < 4)
        {
            return null;
        }

        // ScrgbRgba is already EOTF'd — never OptionsFromProbe Sdr/Pq (double EOTF).
        return new SmoothSourceLoad.Loaded(
            frame.ScrgbRgba,
            frame.Width,
            frame.Height,
            ImagingStillNormalize.OptionsForPresentScrgb(probe.MaxCllNits),
            Orientation: 1);
    }
}
