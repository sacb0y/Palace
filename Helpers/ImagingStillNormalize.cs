using Palace.Imaging;

namespace Palace.Helpers;

/// <summary>
/// P2 glue: map still-decode metadata → <see cref="NormalizeOptions"/> and run
/// <see cref="Normalize"/> into BT.2020 linear working space (1.0 = 203 nits).
/// WinUI-free so <c>Palace.Tests</c> can cover it. Does not replace the legacy
/// scRGB present path (P3–P5); callers attach <see cref="NormalizedFrame"/>
/// beside <c>HdrFrame.ScrgbRgba</c>.
/// </summary>
public static class ImagingStillNormalize
{
    /// <summary>CICP primaries → Imaging enum (11 = JXL P3, 12 = Display P3).</summary>
    public static ColorPrimaries PrimariesFromCicp(int? cicp) =>
        cicp switch
        {
            9 => ColorPrimaries.Bt2020,
            11 or 12 => ColorPrimaries.DisplayP3,
            _ => ColorPrimaries.Bt709,
        };

    public static TransferKind TransferFromHdr(HdrTransfer transfer) =>
        transfer switch
        {
            HdrTransfer.Pq => TransferKind.Pq,
            HdrTransfer.Hlg => TransferKind.Hlg,
            HdrTransfer.Scrgb => TransferKind.Scrgb,
            HdrTransfer.Linear => TransferKind.LinearScene,
            _ => TransferKind.Sdr,
        };

    /// <summary>
    /// libjxl TARGET_DATA / probe → Imaging transfer. Still-PQ/HLG stay encoded;
    /// linear Relative uses <see cref="TransferKind.LinearScene"/> with
    /// intensity_target as white luminance.
    /// </summary>
    public static TransferKind TransferFromJxlBuffer(
        JxlBufferTransfer actualBuffer,
        HdrTransfer probeTransfer)
    {
        if (actualBuffer is JxlBufferTransfer.Pq
            || (actualBuffer == JxlBufferTransfer.Unknown && probeTransfer == HdrTransfer.Pq))
        {
            return TransferKind.Pq;
        }

        if (actualBuffer is JxlBufferTransfer.Hlg
            || (actualBuffer == JxlBufferTransfer.Unknown && probeTransfer == HdrTransfer.Hlg))
        {
            return TransferKind.Hlg;
        }

        if (actualBuffer == JxlBufferTransfer.Linear
            || (actualBuffer == JxlBufferTransfer.Unknown && probeTransfer == HdrTransfer.Linear))
        {
            return TransferKind.LinearScene;
        }

        return TransferFromHdr(probeTransfer);
    }

    public static NormalizeOptions OptionsFromProbe(HdrProbe probe) =>
        probe.Kind switch
        {
            HdrKind.HdrJxr => new NormalizeOptions(
                TransferKind.Scrgb,
                ColorPrimaries.Bt709,
                MetadataPeakNits: probe.MaxCllNits),
            HdrKind.HdrRadiance => new NormalizeOptions(
                TransferKind.LinearScene,
                ColorPrimaries.Bt709,
                MetadataPeakNits: probe.MaxCllNits),
            HdrKind.HdrExr => new NormalizeOptions(
                TransferKind.LinearScene,
                ColorPrimaries.Bt709,
                MetadataPeakNits: probe.MaxCllNits),
            _ when probe.Transfer is HdrTransfer.Pq or HdrTransfer.Hlg => new NormalizeOptions(
                TransferFromHdr(probe.Transfer),
                PrimariesFromCicp(probe.CicpPrimaries),
                MetadataPeakNits: probe.MaxCllNits),
            _ when probe.Transfer == HdrTransfer.Scrgb => new NormalizeOptions(
                TransferKind.Scrgb,
                ColorPrimaries.Bt709,
                MetadataPeakNits: probe.MaxCllNits),
            _ => new NormalizeOptions(
                TransferKind.Sdr,
                PrimariesFromCicp(probe.CicpPrimaries),
                MetadataPeakNits: probe.MaxCllNits),
        };

    /// <summary>
    /// OpenEXR: scene-linear buffer (before Rec.709 convert) + header
    /// whiteLuminance / chromaticities when present.
    /// </summary>
    public static NormalizeOptions OptionsForExr(ReadOnlySpan<byte> file)
    {
        Chromaticities? chrom = null;
        if (ExrHeader.TryReadChromaticities(file, out var c))
        {
            chrom = c;
        }

        float? white = null;
        if (ExrHeader.TryReadWhiteLuminance(file, out var nits))
        {
            white = nits;
        }

        return new NormalizeOptions(
            TransferKind.LinearScene,
            Primaries: ColorPrimaries.Bt709,
            WhiteLuminance: white,
            Chromaticities: chrom);
    }

    public static NormalizeOptions OptionsForRadiance() =>
        new(TransferKind.LinearScene, ColorPrimaries.Bt709);

    public static NormalizeOptions OptionsForJxr(float? metadataPeakNits = null) =>
        new(TransferKind.Scrgb, ColorPrimaries.Bt709, MetadataPeakNits: metadataPeakNits);

    public static NormalizeOptions OptionsForJxl(
        JxlBufferTransfer actualBuffer,
        HdrTransfer probeTransfer,
        int? cicpPrimaries,
        float intensityTarget)
    {
        var transfer = TransferFromJxlBuffer(actualBuffer, probeTransfer);
        var primaries = PrimariesFromCicp(cicpPrimaries);
        if (transfer is TransferKind.Pq or TransferKind.Hlg)
        {
            // PQ/HLG masters are BT.2020 when primaries are unspecified.
            if (cicpPrimaries is null or 2)
            {
                primaries = ColorPrimaries.Bt2020;
            }

            return new NormalizeOptions(transfer, primaries);
        }

        if (transfer == TransferKind.LinearScene)
        {
            // libjxl Relative linear: 1.0 = intensity_target nits (PQ/HLG probe).
            float? white = null;
            if (probeTransfer is HdrTransfer.Pq or HdrTransfer.Hlg)
            {
                white = intensityTarget > 0f
                    ? intensityTarget
                    : probeTransfer == HdrTransfer.Hlg ? 1000f : 10_000f;
            }
            else if (intensityTarget > 0f)
            {
                white = intensityTarget;
            }

            return new NormalizeOptions(
                TransferKind.LinearScene,
                primaries,
                WhiteLuminance: white);
        }

        return new NormalizeOptions(transfer, primaries);
    }

    public static NormalizeOptions OptionsForEncoded(
        HdrTransfer transfer,
        int? cicpPrimaries,
        float? metadataPeakNits = null) =>
        new(
            TransferFromHdr(transfer),
            transfer is HdrTransfer.Pq or HdrTransfer.Hlg
                && cicpPrimaries is null or 2
                ? ColorPrimaries.Bt2020
                : PrimariesFromCicp(cicpPrimaries),
            MetadataPeakNits: metadataPeakNits);

    public static NormalizedFrame Run(
        ReadOnlySpan<float> rgba,
        int width,
        int height,
        NormalizeOptions options) =>
        Normalize.Run(rgba, width, height, options);

    /// <summary>
    /// Clone <paramref name="rgba"/> then normalize — use when the buffer will
    /// still be mutated for the legacy scRGB present path (JXL).
    /// </summary>
    public static NormalizedFrame RunCopy(
        ReadOnlySpan<float> rgba,
        int width,
        int height,
        NormalizeOptions options)
    {
        var copy = rgba.ToArray();
        return Normalize.Run(copy, width, height, options);
    }
}
