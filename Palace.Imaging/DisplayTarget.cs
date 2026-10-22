namespace Palace.Imaging;

/// <summary>
/// Display peak / SDR white for tonemap. Pure value — UI providers land in P5.
/// </summary>
public readonly record struct DisplayTarget(
    float PeakNits,
    float SdrWhiteNits,
    bool IsHdrOutput)
{
    /// <summary>Typical SDR panel: 80-nit composition white, peak = white.</summary>
    public static DisplayTarget Sdr80 { get; } = new(80f, 80f, IsHdrOutput: false);

    /// <summary>SDR with BT.2408 paper white as the map target.</summary>
    public static DisplayTarget Sdr203 { get; } = new(203f, 203f, IsHdrOutput: false);

    /// <summary>HDR display at <paramref name="peakNits"/> with 203-nit paper white.</summary>
    public static DisplayTarget Hdr(float peakNits, float sdrWhiteNits = ImagingConstants.RefWhiteNits) =>
        new(peakNits, sdrWhiteNits, IsHdrOutput: true);

    /// <summary>Display peak in working-space units (÷ 203).</summary>
    public float PeakNormalized => PeakNits / ImagingConstants.RefWhiteNits;

    /// <summary>SDR / paper white in working-space units (÷ 203).</summary>
    public float SdrWhiteNormalized => SdrWhiteNits / ImagingConstants.RefWhiteNits;

    /// <summary>
    /// BT.2390 <c>Ld</c> = display peak / SDR white (unitless ratio in nits).
    /// </summary>
    public float Ld => PeakNits / Math.Max(SdrWhiteNits, 1e-3f);

    public int DisplayHash => HashCode.Combine(PeakNits, SdrWhiteNits, IsHdrOutput);
}
