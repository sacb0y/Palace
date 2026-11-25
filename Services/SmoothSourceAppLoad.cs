using Palace.Helpers;
using Palace.Imaging;

namespace Palace.Services;

/// <summary>
/// App-host float RGBA loaders for Smooth V2 — wires JxlDecode / HdrAvifDecode /
/// HdrWicDecode. Registered into <see cref="SmoothSourceLoad"/> so
/// <c>Palace.Tests</c> stays free of WinUI/WIC.
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
        if (frame?.ScrgbRgba is null || frame.Width < 1 || frame.Height < 1)
        {
            return null;
        }

        return new SmoothSourceLoad.Loaded(
            frame.ScrgbRgba,
            frame.Width,
            frame.Height,
            ImagingStillNormalize.OptionsFromProbe(probe),
            Orientation: 1);
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
                buildWorking: false)
            .GetAwaiter()
            .GetResult();
        var frame = result.Frame;
        if (frame?.ScrgbRgba is null || frame.Width < 1 || frame.Height < 1)
        {
            return null;
        }

        return new SmoothSourceLoad.Loaded(
            frame.ScrgbRgba,
            frame.Width,
            frame.Height,
            ImagingStillNormalize.OptionsFromProbe(probe),
            Orientation: 1);
    }
}
