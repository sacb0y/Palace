namespace Palace.Imaging;

/// <summary>Full-res analyze result. ExposureEV does not affect these.</summary>
public readonly record struct ImageStats(
    float P50,
    float P99995,
    float Max,
    float MaxRgb,
    float Lc,
    float RefWhite,
    float? MetadataPeak,
    bool SourceClipped,
    float SourceClipRatio)
{
    /// <summary>Stable hash for thumb-vs-full and LUT keys (stats portion).</summary>
    public string StatsHash
    {
        get
        {
            // Quantize lightly so float noise does not churn the key.
            static int Q(float v) => (int)MathF.Round(v * 1e5f);
            return $"{Q(P50):x}:{Q(P99995):x}:{Q(Max):x}:{Q(MaxRgb):x}:{Q(Lc):x}";
        }
    }
}
