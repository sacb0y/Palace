namespace Palace.Imaging;

/// <summary>
/// LUT cache key. ExposureEV is <b>not</b> in the key (S2): the LUT is
/// EV-agnostic (PQ-spaced); callers multiply by <c>2^EV</c> before sampling.
/// Includes content/stats hash so DisplayTarget alone is not the only identity.
/// <see cref="ImagingPathKind"/> separates thumb vs full keys (P3 / P4).
/// </summary>
public readonly record struct LutCacheKey(
    string FileKey,
    int PipelineVersion,
    int DisplayContentHash,
    string StatsHash,
    int LutSize,
    ImagingPathKind PathKind = ImagingPathKind.Thumb)
{
    public static LutCacheKey Create(
        string fileKey,
        DisplayTarget display,
        ImageStats stats,
        ImagingPathKind pathKind = ImagingPathKind.Thumb,
        int lutSize = 256) =>
        new(
            fileKey,
            ImagingConstants.PipelineVersion,
            display.ContentHash,
            stats.StatsHash,
            lutSize,
            pathKind);

    public string Token =>
        $"{FileKey}|v{PipelineVersion}|d{DisplayContentHash}|s{StatsHash}|n{LutSize}|p{(int)PathKind}";
}
