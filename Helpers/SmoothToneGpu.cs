using Palace.Imaging;

namespace Palace.Helpers;

/// <summary>
/// Packs Geo <c>psv_tone_cb</c> + ToneMapLut samples for the SmoothImage final
/// PS. Tone is fixed per image+display — never recalculated on zoom.
/// PresentParams follow GalleryPresent / Geo: HDR OutputScale 203/80, clip
/// peak/80, soft negatives; SDR OutputScale 1, or SdrWhite/80 when presenting
/// Full on an HDR panel.
/// </summary>
public static class SmoothToneGpu
{
    public const int DefaultLutSize = 256;

    public readonly record struct PackedTone(
        float[] ToneCb,
        float[] LutSamples,
        float InputMinPq,
        float InputMaxPq);

    /// <summary>
    /// Build tone CB + LUT once per image/display. PresentParams are derived
    /// from <paramref name="display"/> (Geo), not passed by the caller.
    /// </summary>
    /// <param name="fullOnHdrPanel">
    /// When true and <paramref name="display"/> is SDR-tonemap content on an
    /// HDR panel, PresentParams use SdrWhite/80 (Geo). Default false → scale 1.
    /// </param>
    public static PackedTone FromStats(
        ImageStats stats,
        DisplayTarget display,
        float exposureEv = 0f,
        bool fullOnHdrPanel = false,
        ToneMapLut? lut = null)
    {
        var p = new ToneMapParams(stats, display, exposureEv);
        lut ??= ToneMapLut.Bake(p, DefaultLutSize);
        var useHdr = display.IsHdrOutput;
        var outputLimit = useHdr
            ? display.Ld
            : Math.Max(display.PeakNormalized, 1e-6f);

        float outputScale;
        float clipScrgb;
        float softNegatives;
        float paperScrgb;
        if (useHdr)
        {
            // PresentMapImagingHdr: ×203/80, clip peak/80, keep negatives.
            var map = GalleryPresent.PresentMapImagingHdr(display.PeakNits);
            outputScale = map.OutputScale;
            clipScrgb = map.ClipScrgb;
            softNegatives = map.SoftNegatives ? 1f : 0f;
            paperScrgb = outputScale;
        }
        else
        {
            // SDR: OutputScale 1, or SdrWhite/80 when Full on an HDR panel.
            var map = GalleryPresent.PresentMapImagingFull(
                displayIsHdr: fullOnHdrPanel,
                display.SdrWhiteNits);
            outputScale = map.OutputScale;
            clipScrgb = map.ClipScrgb;
            softNegatives = 0f;
            paperScrgb = outputScale;
        }

        var tone = new float[16];
        tone[0] = stats.Lc;
        tone[1] = stats.P50;
        // ToneA.z = display peak (normalized), not image MaxRgb/Max (Geo).
        tone[2] = Math.Max(display.PeakNormalized, 1e-6f);
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
        tone[14] = softNegatives;
        tone[15] = paperScrgb;

        return new PackedTone(tone, lut.Samples, lut.InputMinPq, lut.InputMaxPq);
    }

    /// <summary>
    /// Resolve <see cref="DisplayTarget"/> from swap-chain / monitor HDR probe
    /// (should-fix: not hardcoded Sdr203). Falls back to Sdr203 when unknown.
    /// </summary>
    public static DisplayTarget ResolveDisplayTarget(
        bool? displayIsHdr,
        float displayPeakNits = 0f,
        float sdrWhiteNits = ImagingConstants.RefWhiteNits)
    {
        if (displayIsHdr == true)
        {
            return ImagingHdrPresentToneMap.DisplayForPresent(displayPeakNits);
        }

        if (displayIsHdr == false)
        {
            return sdrWhiteNits <= ImagingConstants.ScrgbNits + 0.5f
                ? DisplayTarget.Sdr80
                : DisplayTarget.Sdr203;
        }

        return DisplayTarget.Sdr203;
    }
}
