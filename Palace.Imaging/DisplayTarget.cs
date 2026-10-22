namespace Palace.Imaging;

/// <summary>
/// Display peak / SDR white for tonemap. Pure value — UI providers land in P5.
/// </summary>
public readonly record struct DisplayTarget(
    float PeakNits,
    float SdrWhiteNits,
    bool IsHdrOutput)
{
    public static DisplayTarget Sdr80 { get; } = new(80f, 80f, IsHdrOutput: false);

    public static DisplayTarget Sdr203 { get; } = new(203f, 203f, IsHdrOutput: false);

    public static DisplayTarget Hdr(float peakNits, float sdrWhiteNits = ImagingConstants.RefWhiteNits) =>
        new(peakNits, sdrWhiteNits, IsHdrOutput: true);

    public float PeakNormalized => PeakNits / ImagingConstants.RefWhiteNits;

    public float SdrWhiteNormalized => SdrWhiteNits / ImagingConstants.RefWhiteNits;

    /// <summary>
    /// BT.2390 display limit in working-space units:
    /// <c>peakNits / RefWhiteNits</c> (absolute working peak), used with Lc.
    /// Spec Ld ratio <c>peak/SDR white</c> is <see cref="LdRatio"/>.
    /// </summary>
    public float Ld => PeakNormalized;

    /// <summary>Spec-style Ld = peak / SDR white (nits ratio).</summary>
    public float LdRatio => PeakNits / Math.Max(SdrWhiteNits, 1e-3f);

    /// <summary>
    /// Stable content hash for cache keys (S6 — do not use randomized
    /// per-process hashes; this is deterministic from the floats).
    /// </summary>
    public int ContentHash
    {
        get
        {
            // Quantize to avoid float bit noise across rebuilds.
            var p = (int)MathF.Round(PeakNits * 100f);
            var s = (int)MathF.Round(SdrWhiteNits * 100f);
            return HashCode.Combine(p, s, IsHdrOutput ? 1 : 0);
        }
    }
}
