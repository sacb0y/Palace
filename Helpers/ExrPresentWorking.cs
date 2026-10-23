using Palace.Imaging;

namespace Palace.Helpers;

/// <summary>
/// R4: EXR present order for P2 — sanitize → optional Working → in-place
/// ConvertToRec709 → present stats. WinUI-free so <c>Palace.Tests</c> can
/// cover the sequence (HdrWicDecode is not in the Linux test project).
/// Mutates <c>sceneRgba</c> in place after Working is built (S1).
/// </summary>
public static class ExrPresentWorking
{
    public readonly record struct Built(
        float[] PresentRgba,
        RadianceFrame Stats,
        NormalizedFrame? Working,
        NormalizeOptions? WorkingOptions);

    /// <summary>
    /// Production path: <see cref="ImagingStillNormalize.OptionsForExr"/> runs
    /// inside the Working try/catch so header/options failures never kill present.
    /// </summary>
    public static Built Run(
        float[] sceneRgba,
        int width,
        int height,
        float[]? chromaticities,
        ReadOnlySpan<byte> exrFile,
        bool buildWorking) =>
        RunCore(sceneRgba, width, height, chromaticities, buildWorking, exrFile, options: null);

    /// <summary>Test path with pre-built options (same order as production).</summary>
    public static Built Run(
        float[] sceneRgba,
        int width,
        int height,
        float[]? chromaticities,
        NormalizeOptions options,
        bool buildWorking) =>
        RunCore(sceneRgba, width, height, chromaticities, buildWorking, exrFile: default, options);

    private static Built RunCore(
        float[] sceneRgba,
        int width,
        int height,
        float[]? chromaticities,
        bool buildWorking,
        ReadOnlySpan<byte> exrFile,
        NormalizeOptions? options)
    {
        ExrFile.SanitizeNonFinite(sceneRgba);

        NormalizedFrame? working = null;
        NormalizeOptions? workingOpts = null;
        if (buildWorking && ImagingStillNormalize.FitsWorkingBudget(width, height))
        {
            try
            {
                // Doktor: OptionsForExr must be inside try/catch (lazy).
                var opts = options ?? ImagingStillNormalize.OptionsForExr(exrFile);
                working = ImagingStillNormalize.TryRun(sceneRgba, width, height, opts);
                if (working is not null)
                {
                    workingOpts = opts;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                working = null;
                workingOpts = null;
            }
        }

        ExrFile.ConvertToRec709(sceneRgba, chromaticities);
        var stats = RadianceFile.FrameFromScrgb(sceneRgba, width, height);
        return new Built(sceneRgba, stats, working, workingOpts);
    }
}
