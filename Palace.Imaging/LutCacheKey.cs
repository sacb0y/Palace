namespace Palace.Imaging;

/// <summary>
/// LUT cache key. ExposureEV is <b>not</b> in the key (S2): the LUT is
/// EV-agnostic (PQ-spaced); callers multiply by <c>2^EV</c> before sampling.
/// Includes content/stats hash so DisplayTarget alone is not the only identity.
/// </summary>
public readonly record struct LutCacheKey(
    string FileKey,
    int PipelineVersion,
    int DisplayContentHash,
    string StatsHash,
    int LutSize)
{
    public static LutCacheKey Create(
        string fileKey,
        DisplayTarget display,
        ImageStats stats,
        int lutSize = 256) =>
        new(
            fileKey,
            ImagingConstants.PipelineVersion,
            display.ContentHash,
            stats.StatsHash,
            lutSize);

    public string Token =>
        $"{FileKey}|v{PipelineVersion}|d{DisplayContentHash}|s{StatsHash}|n{LutSize}";
}
