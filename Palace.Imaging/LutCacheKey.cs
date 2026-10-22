namespace Palace.Imaging;

/// <summary>
/// LUT cache key: file + pipeline version + DisplayTarget + size + ExposureEV.
/// </summary>
public readonly record struct LutCacheKey(
    string FileKey,
    int PipelineVersion,
    int DisplayHash,
    int LutSize,
    float ExposureEv)
{
    public static LutCacheKey Create(
        string fileKey,
        DisplayTarget display,
        float exposureEv,
        int lutSize = 256) =>
        new(
            fileKey,
            ImagingConstants.PipelineVersion,
            display.DisplayHash,
            lutSize,
            exposureEv);

    public string Token =>
        $"{FileKey}|v{PipelineVersion}|d{DisplayHash}|n{LutSize}|ev{ExposureEv:G9}";
}
