using Palace.Imaging;

namespace Palace.Helpers;

/// <summary>
/// Packs Geo <c>psv_tone_cb</c> + optional ToneMapLut samples for the SmoothImage
/// final PS. Tone params are fixed per image/display — never recalculated on zoom.
/// </summary>
public static class SmoothToneGpu
{
    public const int DefaultLutSize = 256;

    public readonly record struct PackedTone(
        float[] ToneCb,
        float[]? LutSamples,
        float InputMinPq,
        float InputMaxPq);

    public static PackedTone FromStats(
        ImageStats stats,
        DisplayTarget display,
        float outputScale,
        float clipScrgb,
        float paperScrgb,
        float exposureEv = 0f,
        bool softNegatives = true,
        ToneMapLut? lut = null)
    {
        var p = new ToneMapParams(stats, display, exposureEv);
        lut ??= ToneMapLut.Bake(p, DefaultLutSize);
        var useHdr = display.IsHdrOutput;
        var outputLimit = useHdr
            ? display.Ld
            : Math.Max(display.PeakNormalized, 1e-6f);

        var tone = new float[16];
        tone[0] = stats.Lc;
        tone[1] = stats.P50;
        tone[2] = Math.Max(stats.MaxRgb, stats.Max);
        tone[3] = display.Ld;
        tone[4] = exposureEv;
        tone[5] = p.ExposureGain;
        tone[6] = useHdr ? 1f : 0f;
        tone[7] = outputLimit;
        tone[8] = lut.InputMinPq;
        tone[9] = lut.InputMaxPq;
        tone[10] = lut.Size;
        tone[11] = 1f; // HasLut
        tone[12] = outputScale;
        tone[13] = clipScrgb;
        tone[14] = softNegatives ? 1f : 0f;
        tone[15] = paperScrgb;

        return new PackedTone(tone, lut.Samples, lut.InputMinPq, lut.InputMaxPq);
    }
}
