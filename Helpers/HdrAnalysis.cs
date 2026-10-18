namespace Palace.Helpers;

/// <summary>How the HDR swapchain colours a presented still.</summary>
public enum HdrVizMode
{
    /// <summary>Normal present (scale + clip).</summary>
    None = 0,

    /// <summary>SKIV / D2D luminance heatmap (<see cref="HdrHeatmap"/>).</summary>
    Heatmap = 1
}

/// <summary>Smallest standard gamut that contains a pixel's chromaticity.</summary>
public enum GamutKind
{
    Rec709 = 0,
    DciP3 = 1,
    Rec2020 = 2,
    AcesAp1 = 3,
    AcesAp0 = 4,
    Invalid = 5
}

/// <summary>
/// Lightroom-style two-segment histogram axis, shared by the chart, the
/// gradient bar and the heatmap legend. Left: SDR (0 → SDR reference white),
/// binned in sRGB-encoded space so shadows are not crushed. Right: HDR, in
/// whole stops (log2) above SDR white. SDR stills have no HDR segment.
/// Pure math; no WinUI.
/// </summary>
public static class HistogramScale
{
    /// <summary>
    /// SDR reference white for the split: BT.2408 203 nits, which is where
    /// Palace's decoders put SDR white (scRGB 203/80). A reference white, not
    /// a display peak.
    /// </summary>
    public const float SdrWhiteNits = GalleryPresent.SdrReferenceNits;

    public const int SdrBins = 256;
    public const int BinsPerStop = 32;
    public const int MinStops = 1;
    public const int MaxStops = 6;

    /// <summary>Share of the width the SDR segment takes when HDR is shown.</summary>
    public const double HdrSdrFraction = 0.6;

    public static double SdrFraction(int stopCount) => stopCount <= 0 ? 1.0 : HdrSdrFraction;

    public static int BinCount(int stopCount) => SdrBins + (Math.Max(0, stopCount) * BinsPerStop);

    /// <summary>
    /// HDR stops to show: ceil(log2(max / SDR white)) clamped 1…6. SDR content
    /// (or nothing above white) on a non-HDR still → 0.
    /// </summary>
    public static int StopCount(bool hdr, float maxNits, float sdrWhiteNits = SdrWhiteNits)
    {
        if (!hdr)
        {
            return 0;
        }

        if (!(maxNits > sdrWhiteNits) || sdrWhiteNits <= 0)
        {
            return MinStops;
        }

        var stops = (int)Math.Ceiling(Math.Log2(maxNits / sdrWhiteNits) - 1e-6);
        return Math.Clamp(stops, MinStops, MaxStops);
    }

    public static float SrgbOetf(float linear01)
    {
        if (!(linear01 > 0))
        {
            return 0;
        }

        if (linear01 >= 1)
        {
            return 1;
        }

        return linear01 <= 0.0031308f
            ? linear01 * 12.92f
            : (1.055f * MathF.Pow(linear01, 1f / 2.4f)) - 0.055f;
    }

    /// <summary>Bin for a nit value (luma Y, or a channel × 80).</summary>
    public static int Bin(float nits, int stopCount, float sdrWhiteNits = SdrWhiteNits)
    {
        if (!(nits > 0) || sdrWhiteNits <= 0)
        {
            return 0;
        }

        if (nits <= sdrWhiteNits || stopCount <= 0)
        {
            var e = SrgbOetf(nits / sdrWhiteNits);
            return Math.Clamp((int)(e * SdrBins), 0, SdrBins - 1);
        }

        var hdrBins = stopCount * BinsPerStop;
        var s = Math.Log2(nits / sdrWhiteNits);
        return SdrBins + Math.Clamp((int)(s * BinsPerStop), 0, hdrBins - 1);
    }

    /// <summary>0…1 x position of a nit value on the chart.</summary>
    public static double X01(float nits, int stopCount, float sdrWhiteNits = SdrWhiteNits)
    {
        var frac = SdrFraction(stopCount);
        if (!(nits > 0) || sdrWhiteNits <= 0)
        {
            return 0;
        }

        if (nits <= sdrWhiteNits || stopCount <= 0)
        {
            return frac * SrgbOetf(nits / sdrWhiteNits);
        }

        var s = Math.Min(Math.Log2(nits / sdrWhiteNits) / stopCount, 1.0);
        return frac + ((1 - frac) * s);
    }

    /// <summary>Inverse of <see cref="X01"/>: the nits at an x position.</summary>
    public static float NitsAtX01(double x01, int stopCount, float sdrWhiteNits = SdrWhiteNits)
    {
        var frac = SdrFraction(stopCount);
        x01 = Math.Clamp(x01, 0, 1);
        if (x01 <= frac || stopCount <= 0)
        {
            var e = (float)(frac <= 0 ? 0 : x01 / frac);
            return SrgbEotf(e) * sdrWhiteNits;
        }

        var stops = (x01 - frac) / (1 - frac) * stopCount;
        return (float)(sdrWhiteNits * Math.Pow(2, stops));
    }

    /// <summary>Centre x (0…1) of bin <paramref name="index"/>.</summary>
    public static double BinCenterX01(int index, int stopCount)
    {
        var frac = SdrFraction(stopCount);
        if (index < SdrBins || stopCount <= 0)
        {
            return frac * ((Math.Clamp(index, 0, SdrBins - 1) + 0.5) / SdrBins);
        }

        var hdrBins = stopCount * BinsPerStop;
        var i = Math.Clamp(index - SdrBins, 0, hdrBins - 1);
        return frac + ((1 - frac) * ((i + 0.5) / hdrBins));
    }

    /// <summary>x (0…1) of the dashed line at whole stop <paramref name="stop"/> (1…stopCount).</summary>
    public static double StopX01(int stop, int stopCount)
    {
        var frac = SdrFraction(stopCount);
        if (stopCount <= 0)
        {
            return 1;
        }

        return frac + ((1 - frac) * Math.Clamp(stop, 0, stopCount) / stopCount);
    }

    public static string HoverLabel(double x01, int stopCount, float sdrWhiteNits = SdrWhiteNits)
    {
        var nits = NitsAtX01(x01, stopCount, sdrWhiteNits);
        if (stopCount > 0 && x01 > SdrFraction(stopCount))
        {
            var stops = Math.Log2(nits / sdrWhiteNits);
            return $"HDR +{stops:0.0} stops · {FormatNits(nits)}";
        }

        return $"SDR · {FormatNits(nits)} (SDR white {sdrWhiteNits:0} nits)";
    }

    public static string FormatNits(float nits) =>
        nits >= 100 ? $"{nits:0} nits" : nits >= 1 ? $"{nits:0.#} nits" : $"{nits:0.###} nits";

    private static float SrgbEotf(float e) =>
        e <= 0.04045f ? e / 12.92f : MathF.Pow((e + 0.055f) / 1.055f, 2.4f);
}

/// <summary>
/// SKIV / Microsoft D2DAdvancedColorImages luminance heatmap: 9 stops at
/// 0 / 3.16 / 10 / 31.6 / 100 / 316 / 1000 / 3160 / 10000 nits → black,
/// blue, cyan, green, yellow, orange, red, magenta, white (above 10 000 →
/// white). Interpolated in log10 nits between stops (linear below 3.16).
/// The histogram bar uses the same function, so the colours always match.
/// </summary>
public static class HdrHeatmap
{
    public static readonly float[] StopNits = [0f, 3.16f, 10f, 31.6f, 100f, 316f, 1000f, 3160f, 10000f];

    /// <summary>sRGB-encoded colours (0…1) for <see cref="StopNits"/>.</summary>
    public static readonly (float R, float G, float B)[] StopColors =
    [
        (0f, 0f, 0f),
        (0f, 0f, 1f),
        (0f, 1f, 1f),
        (0f, 1f, 0f),
        (1f, 1f, 0f),
        (1f, 0.5f, 0f),
        (1f, 0f, 0f),
        (1f, 0f, 1f),
        (1f, 1f, 1f)
    ];

    /// <summary>sRGB-encoded (0…1) palette colour for <paramref name="nits"/>.</summary>
    public static (float R, float G, float B) EncodedAt(float nits)
    {
        if (!(nits > 0))
        {
            return StopColors[0];
        }

        if (nits >= StopNits[^1])
        {
            return StopColors[^1];
        }

        var i = 1;
        while (i < StopNits.Length - 1 && nits >= StopNits[i])
        {
            i++;
        }

        var lo = StopNits[i - 1];
        var hi = StopNits[i];
        var t = i == 1
            ? nits / hi
            : (MathF.Log10(nits) - MathF.Log10(lo)) / (MathF.Log10(hi) - MathF.Log10(lo));
        t = Math.Clamp(t, 0f, 1f);
        var a = StopColors[i - 1];
        var b = StopColors[i];
        return (a.R + ((b.R - a.R) * t), a.G + ((b.G - a.G) * t), a.B + ((b.B - a.B) * t));
    }

    /// <summary>Linear palette colour × <paramref name="whiteScrgb"/> (scRGB).</summary>
    public static void ScrgbAt(float nits, float whiteScrgb, out float r, out float g, out float b)
    {
        var (er, eg, eb) = EncodedAt(nits);
        r = GalleryPresent.SrgbEotf(er) * whiteScrgb;
        g = GalleryPresent.SrgbEotf(eg) * whiteScrgb;
        b = GalleryPresent.SrgbEotf(eb) * whiteScrgb;
    }

    /// <summary>8-bit sRGB palette colour (for XAML gradient stops).</summary>
    public static (byte R, byte G, byte B) Srgb8At(float nits)
    {
        var (r, g, b) = EncodedAt(nits);
        return (To8(r), To8(g), To8(b));
    }

    private static byte To8(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0, 255);
}

/// <summary>
/// CIE 1931 xy chromaticity helpers and SKIV's gamut-coverage classes
/// (Rec.709, DCI-P3, Rec.2020, ACES AP1, ACES AP0, invalid). Classification
/// is a point-in-triangle test on xy, so it does not depend on each gamut's
/// white point. Input is linear scRGB (BT.709 primaries, negatives kept).
/// </summary>
public static class CieGamut
{
    public readonly record struct Primaries(
        double Rx, double Ry, double Gx, double Gy, double Bx, double By);

    public static readonly Primaries Rec709 = new(0.640, 0.330, 0.300, 0.600, 0.150, 0.060);
    public static readonly Primaries DciP3 = new(0.680, 0.320, 0.265, 0.690, 0.150, 0.060);
    public static readonly Primaries Rec2020 = new(0.708, 0.292, 0.170, 0.797, 0.131, 0.046);
    public static readonly Primaries AcesAp1 = new(0.713, 0.293, 0.165, 0.830, 0.128, 0.044);
    public static readonly Primaries AcesAp0 = new(0.7347, 0.2653, 0.0, 1.0, 0.0001, -0.0770);

    public const double D65X = 0.3127;
    public const double D65Y = 0.3290;

    /// <summary>Below this Y (nits) chromaticity is noise; the pixel counts as Rec.709.</summary>
    public const float BlackNits = 0.05f;

    public static Primaries For(GamutKind kind) => kind switch
    {
        GamutKind.Rec709 => Rec709,
        GamutKind.DciP3 => DciP3,
        GamutKind.Rec2020 => Rec2020,
        GamutKind.AcesAp1 => AcesAp1,
        _ => AcesAp0
    };

    public static string Label(GamutKind kind) => kind switch
    {
        GamutKind.Rec709 => "Rec.709",
        GamutKind.DciP3 => "DCI-P3",
        GamutKind.Rec2020 => "Rec.2020",
        GamutKind.AcesAp1 => "ACES AP1",
        GamutKind.AcesAp0 => "ACES AP0",
        _ => "Invalid"
    };

    /// <summary>scRGB (linear BT.709) → CIE XYZ (D65), Y in scRGB units.</summary>
    public static void ScrgbToXyz(float r, float g, float b, out double x, out double y, out double z)
    {
        x = (0.4123908 * r) + (0.3575843 * g) + (0.1804808 * b);
        y = (0.2126390 * r) + (0.7151687 * g) + (0.0721923 * b);
        z = (0.0193308 * r) + (0.1191948 * g) + (0.9505322 * b);
    }

    /// <summary>xy of an scRGB pixel. False when X+Y+Z ≤ 0 (no chromaticity).</summary>
    public static bool TryXy(float r, float g, float b, out double cx, out double cy)
    {
        ScrgbToXyz(r, g, b, out var x, out var y, out var z);
        var sum = x + y + z;
        if (!(sum > 1e-9))
        {
            cx = 0;
            cy = 0;
            return false;
        }

        cx = x / sum;
        cy = y / sum;
        return true;
    }

    public static bool Contains(in Primaries p, double x, double y, double eps = 1e-4)
    {
        var d1 = Cross(x, y, p.Rx, p.Ry, p.Gx, p.Gy);
        var d2 = Cross(x, y, p.Gx, p.Gy, p.Bx, p.By);
        var d3 = Cross(x, y, p.Bx, p.By, p.Rx, p.Ry);
        var neg = d1 < -eps || d2 < -eps || d3 < -eps;
        var pos = d1 > eps || d2 > eps || d3 > eps;
        return !(neg && pos);
    }

    public static GamutKind Classify(double x, double y)
    {
        if (Contains(Rec709, x, y))
        {
            return GamutKind.Rec709;
        }

        if (Contains(DciP3, x, y))
        {
            return GamutKind.DciP3;
        }

        if (Contains(Rec2020, x, y))
        {
            return GamutKind.Rec2020;
        }

        if (Contains(AcesAp1, x, y))
        {
            return GamutKind.AcesAp1;
        }

        return Contains(AcesAp0, x, y) ? GamutKind.AcesAp0 : GamutKind.Invalid;
    }

    /// <summary>Classify an scRGB pixel; near-black counts as Rec.709.</summary>
    public static GamutKind ClassifyScrgb(float r, float g, float b, float yNits)
    {
        if (yNits < BlackNits && r >= 0 && g >= 0 && b >= 0)
        {
            return GamutKind.Rec709;
        }

        if (!TryXy(r, g, b, out var x, out var y))
        {
            return GamutKind.Invalid;
        }

        return Classify(x, y);
    }

    private static double Cross(double px, double py, double ax, double ay, double bx, double by) =>
        ((px - bx) * (ay - by)) - ((ax - bx) * (py - by));
}

public readonly record struct GamutCoverage(
    double Rec709,
    double DciP3,
    double Rec2020,
    double AcesAp1,
    double AcesAp0,
    double Invalid)
{
    public static GamutCoverage Empty { get; } = new(0, 0, 0, 0, 0, 0);

    public double this[GamutKind kind] => kind switch
    {
        GamutKind.Rec709 => Rec709,
        GamutKind.DciP3 => DciP3,
        GamutKind.Rec2020 => Rec2020,
        GamutKind.AcesAp1 => AcesAp1,
        GamutKind.AcesAp0 => AcesAp0,
        _ => Invalid
    };

    /// <summary>Share of pixels outside Rec.709 (0…100).</summary>
    public double WideGamut => DciP3 + Rec2020 + AcesAp1 + AcesAp0 + Invalid;

    /// <summary>"Rec.709 92.1% · DCI-P3 6.2% · …", non-zero classes only (Rec.709 always).</summary>
    public string Summary()
    {
        var parts = new List<string>();
        foreach (var kind in Enum.GetValues<GamutKind>())
        {
            var v = this[kind];
            if (kind == GamutKind.Rec709 || v >= 0.05)
            {
                parts.Add($"{CieGamut.Label(kind)} {v:0.0}%");
            }
        }

        return string.Join(" · ", parts);
    }
}

/// <summary>Light figures from one analysis pass (nits are CIE Y unless noted).</summary>
public readonly record struct HdrLightStats(
    float MaxCllScrgb,
    char MaxCllChannel,
    float MaxNits,
    float AvgNits,
    float MinNits,
    float P99Nits,
    float FallNits,
    double AboveSdrWhitePercent)
{
    public static HdrLightStats Empty { get; } = new(0, ' ', 0, 0, 0, 0, 0, 0);

    public float MaxCllNits => MaxCllScrgb * GalleryPresent.ScrgbNits;
}

/// <summary>Everything the histogram, Info card, gamut plot and heatmap need.</summary>
public sealed class HdrAnalysisResult
{
    // No required members: the XAML compiler activates this type (it is a
    // DependencyProperty value type) with a bare new().
    public static HdrAnalysisResult Empty { get; } = new()
    {
        Histogram = GalleryHistogramBins.Empty,
        Light = HdrLightStats.Empty,
        Gamut = GamutCoverage.Empty,
        CieDensity = []
    };

    public GalleryHistogramBins Histogram { get; init; } = GalleryHistogramBins.Empty;
    public HdrLightStats Light { get; init; } = HdrLightStats.Empty;
    public GamutCoverage Gamut { get; init; } = GamutCoverage.Empty;

    /// <summary><see cref="CieDiagram.GridSize"/>² sample counts over xy (row 0 = top).</summary>
    public int[] CieDensity { get; init; } = [];

    public bool IsHdr { get; init; }
    public int SampleCount => Histogram.SampleCount;
    public bool HasSamples => Histogram.HasSamples;
}

/// <summary>
/// One pass over a presented still (linear scRGB, negatives kept) that
/// produces the Lightroom-style histogram bins, SKIV light stats (MaxCLL +
/// channel, max / avg / min, P99), Palace's FALL, gamut coverage and the
/// CIE 1931 xy density. Off WinUI and thread-safe; callers run it on the
/// thread pool. Luma is <see cref="PixelNits"/> — the same CIE Y as Info and
/// the heatmap, so the histogram's HDR section and heatmap colours agree.
/// Reusable for export (same inputs, same numbers).
/// </summary>
public static class HdrAnalysis
{
    public const int DefaultMaxSamples = 262_144;
    public const double P99Fraction = 0.99;

    /// <summary>CIE Y nits for a presented scRGB pixel in the source's primaries (matches Info).</summary>
    public static float PixelNits(float r, float g, float b, int? cicpPrimaries) =>
        GalleryPresent.LumaNitsFromPresentedScrgb(r, g, b, cicpPrimaries);

    public static HdrAnalysisResult Analyze(
        float[] rgba,
        int width,
        int height,
        int? cicpPrimaries,
        bool hdr,
        int maxSamples = DefaultMaxSamples,
        float sdrWhiteNits = HistogramScale.SdrWhiteNits)
    {
        if (rgba is null || width <= 0 || height <= 0)
        {
            return HdrAnalysisResult.Empty;
        }

        long pixelsL = (long)width * height;
        if (pixelsL > int.MaxValue / 4 || rgba.Length < pixelsL * 4)
        {
            return HdrAnalysisResult.Empty;
        }

        var pixels = (int)pixelsL;

        // Full pass: MaxCLL (+ channel) and FALL are cheap and must not miss
        // a single bright pixel to sampling.
        var maxR = 0f;
        var maxG = 0f;
        var maxB = 0f;
        double fallSum = 0;
        for (var i = 0; i < pixels; i++)
        {
            var o = i * 4;
            var r = Finite(rgba[o]);
            var g = Finite(rgba[o + 1]);
            var b = Finite(rgba[o + 2]);
            if (r > maxR)
            {
                maxR = r;
            }

            if (g > maxG)
            {
                maxG = g;
            }

            if (b > maxB)
            {
                maxB = b;
            }

            var m = Math.Max(r, Math.Max(g, b));
            if (m > 0)
            {
                fallSum += m;
            }
        }

        var stride = GalleryHistogram.SampleStride(pixels, maxSamples);
        var sampleCap = (pixels + stride - 1) / stride;
        var ys = new float[sampleCap];
        var n = 0;
        var maxY = 0f;
        for (var i = 0; i < pixels; i += stride)
        {
            var o = i * 4;
            var y = PixelNits(Finite(rgba[o]), Finite(rgba[o + 1]), Finite(rgba[o + 2]), cicpPrimaries);
            ys[n++] = y;
            if (y > maxY)
            {
                maxY = y;
            }
        }

        var maxCll = Math.Max(maxR, Math.Max(maxG, maxB));
        var stopCount = HistogramScale.StopCount(
            hdr, Math.Max(maxY, maxCll * GalleryPresent.ScrgbNits), sdrWhiteNits);
        var acc = new Accumulator(stopCount, sdrWhiteNits);
        n = 0;
        for (var i = 0; i < pixels; i += stride)
        {
            var o = i * 4;
            acc.Add(Finite(rgba[o]), Finite(rgba[o + 1]), Finite(rgba[o + 2]), ys[n++]);
        }

        var channel = maxCll <= 0 ? ' ' : maxR >= maxG && maxR >= maxB ? 'R' : maxG >= maxB ? 'G' : 'B';
        return acc.Build(ys, n, maxCll, channel, (float)(fallSum / pixels * GalleryPresent.ScrgbNits), hdr);
    }

    /// <summary>8-bit sRGB stills (GIF composite, packed fallback) via the same pass.</summary>
    public static HdrAnalysisResult AnalyzePacked8(
        byte[] data,
        int width,
        int height,
        HdrPackedFormat format,
        int maxSamples = DefaultMaxSamples)
    {
        if (data is null || width <= 0 || height <= 0 || !HdrPixels.HasPackedData(data, format, width, height))
        {
            return HdrAnalysisResult.Empty;
        }

        var pixels = width * height;
        var stride = GalleryHistogram.SampleStride(pixels, maxSamples);
        var count = (pixels + stride - 1) / stride;
        var rgba = new float[count * 4];
        var k = 0;
        for (var i = 0; i < pixels; i += stride)
        {
            HdrPixels.Read(data, i, format, out var er, out var eg, out var eb, out var ea);
            HdrColor.EncodedRgbToScrgb(er, eg, eb, HdrTransfer.Srgb, 1, out var sr, out var sg, out var sb);
            rgba[k++] = sr;
            rgba[k++] = sg;
            rgba[k++] = sb;
            rgba[k++] = ea;
        }

        return Analyze(rgba, count, 1, 1, hdr: false, maxSamples: count);
    }

    private static float Finite(float v) => float.IsFinite(v) ? v : 0f;

    /// <summary>Exact percentile of the first <paramref name="count"/> values (sorts in place).</summary>
    public static float Percentile(float[] values, int count, double fraction)
    {
        if (values is null || count <= 0)
        {
            return 0;
        }

        Array.Sort(values, 0, count);
        var idx = (int)Math.Ceiling(fraction * count) - 1;
        return values[Math.Clamp(idx, 0, count - 1)];
    }

    private sealed class Accumulator
    {
        private readonly int _stopCount;
        private readonly float _white;
        private readonly int[] _luma;
        private readonly int[] _red;
        private readonly int[] _green;
        private readonly int[] _blue;
        private readonly int[] _gamut = new int[6];
        private readonly int[] _cie = new int[CieDiagram.GridSize * CieDiagram.GridSize];
        private int _samples;
        private int _above;
        private double _sumY;
        private float _minY = float.MaxValue;
        private float _maxY;

        public Accumulator(int stopCount, float white)
        {
            _stopCount = stopCount;
            _white = white;
            var bins = HistogramScale.BinCount(stopCount);
            _luma = new int[bins];
            _red = new int[bins];
            _green = new int[bins];
            _blue = new int[bins];
        }

        public void Add(float r, float g, float b, float yNits)
        {
            _samples++;
            _luma[HistogramScale.Bin(yNits, _stopCount, _white)]++;
            _red[HistogramScale.Bin(r * GalleryPresent.ScrgbNits, _stopCount, _white)]++;
            _green[HistogramScale.Bin(g * GalleryPresent.ScrgbNits, _stopCount, _white)]++;
            _blue[HistogramScale.Bin(b * GalleryPresent.ScrgbNits, _stopCount, _white)]++;
            var y = Math.Max(0f, yNits);
            _sumY += y;
            if (y < _minY)
            {
                _minY = y;
            }

            if (y > _maxY)
            {
                _maxY = y;
            }

            if (yNits > _white)
            {
                _above++;
            }

            _gamut[(int)CieGamut.ClassifyScrgb(r, g, b, yNits)]++;
            if (yNits >= CieGamut.BlackNits && CieGamut.TryXy(r, g, b, out var cx, out var cy)
                && CieDiagram.TryCell(cx, cy, out var cell))
            {
                _cie[cell]++;
            }
        }

        public HdrAnalysisResult Build(float[] ys, int count, float maxCll, char channel, float fallNits, bool hdr)
        {
            if (_samples == 0)
            {
                return HdrAnalysisResult.Empty;
            }

            var p99 = Percentile(ys, count, P99Fraction);
            double pct(int v) => v * 100.0 / _samples;
            return new HdrAnalysisResult
            {
                IsHdr = hdr,
                Histogram = new GalleryHistogramBins
                {
                    Luma = _luma,
                    Red = _red,
                    Green = _green,
                    Blue = _blue,
                    SampleCount = _samples,
                    BinCount = _luma.Length,
                    StopCount = _stopCount,
                    SdrWhiteNits = _white
                },
                Light = new HdrLightStats(
                    maxCll,
                    channel,
                    _maxY,
                    (float)(_sumY / _samples),
                    _minY == float.MaxValue ? 0 : _minY,
                    p99,
                    fallNits,
                    pct(_above)),
                Gamut = new GamutCoverage(
                    pct(_gamut[0]), pct(_gamut[1]), pct(_gamut[2]),
                    pct(_gamut[3]), pct(_gamut[4]), pct(_gamut[5])),
                CieDensity = _cie
            };
        }
    }
}
