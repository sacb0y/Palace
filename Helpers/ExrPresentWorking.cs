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
    /// Runs the production EXR present/Working sequence on a scene-linear
    /// float buffer. <paramref name="sceneRgba"/> is sanitized then converted
    /// in place; Working (when built) is a separate allocation.
    /// </summary>
    public static Built Run(
        float[] sceneRgba,
        int width,
        int height,
        float[]? chromaticities,
        NormalizeOptions options,
        bool buildWorking)
    {
        ExrFile.SanitizeNonFinite(sceneRgba);

        NormalizedFrame? working = null;
        NormalizeOptions? workingOpts = null;
        if (buildWorking && ImagingStillNormalize.FitsWorkingBudget(width, height))
        {
            try
            {
                working = ImagingStillNormalize.TryRun(sceneRgba, width, height, options);
                if (working is not null)
                {
                    workingOpts = options;
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
