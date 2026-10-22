namespace Palace.Imaging;

/// <summary>
/// Display peak / SDR white for tonemap. Pure value — UI providers land in P5.
/// <para>
/// <b>P5 owns</b> probing/wiring Ld on the HDR present path; this type only
/// carries the numbers the library needs.
/// </para>
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

    /// <summary>Working-space display peak (peakNits / 203). P5 supplies the nits.</summary>
    public float Ld => PeakNormalized;

    /// <summary>Spec-style Ld = peak / SDR white (nits ratio).</summary>
    public float LdRatio => PeakNits / Math.Max(SdrWhiteNits, 1e-3f);

    /// <summary>
    /// Process-stable content hash (N4). Do <b>not</b> use
    /// <see cref="HashCode.Combine"/> — its seed is randomized per process.
    /// </summary>
    public int ContentHash
    {
        get
        {
            unchecked
            {
                var p = (int)MathF.Round(PeakNits * 100f);
                var s = (int)MathF.Round(SdrWhiteNits * 100f);
                var h = 17;
                h = (h * 31) + p;
                h = (h * 31) + s;
                h = (h * 31) + (IsHdrOutput ? 1 : 0);
                return h;
            }
        }
    }
}
