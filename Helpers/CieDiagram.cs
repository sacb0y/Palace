namespace Palace.Helpers;

/// <summary>
/// CIE 1931 xy chromaticity plot (SKIV's "gamut view"): spectral locus,
/// Rec.709 / DCI-P3 / Rec.2020 / ACES AP1 triangles, D65, and the image's pixel
/// density from <see cref="HdrAnalysis"/>. Rendered on the CPU into a
/// premultiplied BGRA buffer (a <c>WriteableBitmap</c> on Windows); pure, so
/// it is tested on Linux and reusable for export.
/// </summary>
public static class CieDiagram
{
    public const int GridSize = 256;
    public const double MaxX = 0.8;
    public const double MaxY = 0.9;

    /// <summary>CIE 1931 2° spectral locus, 380–700 nm.</summary>
    public static readonly (double X, double Y)[] Locus =
    [
        (0.1741, 0.0050), (0.1738, 0.0049), (0.1733, 0.0048), (0.1726, 0.0048),
        (0.1714, 0.0051), (0.1689, 0.0069), (0.1644, 0.0109), (0.1566, 0.0177),
        (0.1440, 0.0297), (0.1241, 0.0578), (0.1096, 0.0868), (0.0913, 0.1327),
        (0.0687, 0.2007), (0.0454, 0.2950), (0.0235, 0.4127), (0.0082, 0.5384),
        (0.0039, 0.6548), (0.0139, 0.7502), (0.0389, 0.8120), (0.0743, 0.8338),
        (0.1142, 0.8262), (0.1547, 0.8059), (0.1929, 0.7816), (0.2296, 0.7543),
        (0.2658, 0.7243), (0.3016, 0.6923), (0.3373, 0.6589), (0.3731, 0.6245),
        (0.4441, 0.5547), (0.5125, 0.4866), (0.5752, 0.4242), (0.6270, 0.3725),
        (0.6658, 0.3340), (0.6915, 0.3083), (0.7079, 0.2920), (0.7190, 0.2809),
        (0.7260, 0.2740), (0.7300, 0.2700), (0.7334, 0.2666), (0.7347, 0.2653)
    ];

    /// <summary>SKIV gamut hues: 709 white, P3 cyan, 2020 green, AP1 yellow, AP0 magenta, invalid red.</summary>
    public static (byte R, byte G, byte B) Hue(GamutKind kind) => kind switch
    {
        GamutKind.Rec709 => (255, 255, 255),
        GamutKind.DciP3 => (0, 255, 255),
        GamutKind.Rec2020 => (0, 255, 0),
        GamutKind.AcesAp1 => (255, 255, 0),
        GamutKind.AcesAp0 => (255, 0, 255),
        _ => (255, 0, 0)
    };

    /// <summary>Density grid cell for xy (row 0 = top, y = <see cref="MaxY"/>).</summary>
    public static bool TryCell(double x, double y, out int cell)
    {
        cell = 0;
        if (!(x >= 0 && x < MaxX && y >= 0 && y < MaxY))
        {
            return false;
        }

        var col = Math.Clamp((int)(x / MaxX * GridSize), 0, GridSize - 1);
        var row = Math.Clamp((int)((MaxY - y) / MaxY * GridSize), 0, GridSize - 1);
        cell = (row * GridSize) + col;
        return true;
    }

    public static bool InsideLocus(double x, double y)
    {
        var inside = false;
        var n = Locus.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var (xi, yi) = Locus[i];
            var (xj, yj) = Locus[j];
            if ((yi > y) != (yj > y) && x < ((xj - xi) * (y - yi) / (yj - yi)) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>Premultiplied BGRA, <paramref name="size"/>² pixels.</summary>
    public static byte[] RenderBgra(ReadOnlySpan<int> density, int size = GridSize)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        var px = new byte[size * size * 4];
        var max = 0;
        foreach (var c in density)
        {
            if (c > max)
            {
                max = c;
            }
        }

        var hasDensity = density.Length == GridSize * GridSize && max > 0;
        var logMax = MathF.Log(1 + max);
        for (var py = 0; py < size; py++)
        {
            var y = MaxY - ((py + 0.5) / size * MaxY);
            for (var pxi = 0; pxi < size; pxi++)
            {
                var x = (pxi + 0.5) / size * MaxX;
                if (!InsideLocus(x, y))
                {
                    continue;
                }

                var (r, g, b) = ChromaColor(x, y);
                var dim = 0.30f;
                var cr = r * dim;
                var cg = g * dim;
                var cb = b * dim;
                if (hasDensity)
                {
                    var col = Math.Clamp(pxi * GridSize / size, 0, GridSize - 1);
                    var row = Math.Clamp(py * GridSize / size, 0, GridSize - 1);
                    var count = density[(row * GridSize) + col];
                    if (count > 0)
                    {
                        var t = 0.45f + (0.55f * MathF.Log(1 + count) / logMax);
                        cr = cr + ((1 - cr) * t);
                        cg = cg + ((1 - cg) * t);
                        cb = cb + ((1 - cb) * t);
                    }
                }

                Put(px, size, pxi, py, To8(cr), To8(cg), To8(cb));
            }
        }

        DrawPolyline(px, size, Locus, closed: true, (150, 150, 150));
        DrawTriangle(px, size, CieGamut.AcesAp1, Hue(GamutKind.AcesAp1));
        DrawTriangle(px, size, CieGamut.Rec2020, Hue(GamutKind.Rec2020));
        DrawTriangle(px, size, CieGamut.DciP3, Hue(GamutKind.DciP3));
        DrawTriangle(px, size, CieGamut.Rec709, Hue(GamutKind.Rec709));
        var (wx, wy) = ToPixel(CieGamut.D65X, CieGamut.D65Y, size);
        for (var d = -2; d <= 2; d++)
        {
            Put(px, size, wx + d, wy, 255, 255, 255);
            Put(px, size, wx, wy + d, 255, 255, 255);
        }

        return px;
    }

    public static (int X, int Y) ToPixel(double x, double y, int size) =>
        ((int)Math.Round(x / MaxX * size), (int)Math.Round((MaxY - y) / MaxY * size));

    /// <summary>Normalized linear-light sRGB-encoded colour for a chromaticity (display only).</summary>
    private static (float R, float G, float B) ChromaColor(double x, double y)
    {
        if (y <= 1e-6)
        {
            return (0, 0, 0);
        }

        var X = x / y;
        var Z = (1 - x - y) / y;
        var r = (3.2404542 * X) - 1.5371385 - (0.4985314 * Z);
        var g = (-0.9692660 * X) + 1.8760108 + (0.0415560 * Z);
        var b = (0.0556434 * X) - 0.2040259 + (1.0572252 * Z);
        r = Math.Max(0, r);
        g = Math.Max(0, g);
        b = Math.Max(0, b);
        var m = Math.Max(r, Math.Max(g, b));
        if (m <= 0)
        {
            return (0, 0, 0);
        }

        return (
            HistogramScale.SrgbOetf((float)(r / m)),
            HistogramScale.SrgbOetf((float)(g / m)),
            HistogramScale.SrgbOetf((float)(b / m)));
    }

    private static void DrawTriangle(byte[] px, int size, CieGamut.Primaries p, (byte R, byte G, byte B) c) =>
        DrawPolyline(px, size, [(p.Rx, p.Ry), (p.Gx, p.Gy), (p.Bx, p.By)], closed: true, c);

    private static void DrawPolyline(
        byte[] px, int size, (double X, double Y)[] pts, bool closed, (byte R, byte G, byte B) c)
    {
        var n = pts.Length;
        for (var i = 0; i < (closed ? n : n - 1); i++)
        {
            var a = ToPixel(pts[i].X, pts[i].Y, size);
            var b = ToPixel(pts[(i + 1) % n].X, pts[(i + 1) % n].Y, size);
            var steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            for (var s = 0; s <= steps; s++)
            {
                var t = steps == 0 ? 0 : s / (double)steps;
                Put(px, size, (int)Math.Round(a.X + ((b.X - a.X) * t)), (int)Math.Round(a.Y + ((b.Y - a.Y) * t)), c.R, c.G, c.B);
            }
        }
    }

    private static void Put(byte[] px, int size, int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || y < 0 || x >= size || y >= size)
        {
            return;
        }

        var o = ((y * size) + x) * 4;
        px[o] = b;
        px[o + 1] = g;
        px[o + 2] = r;
        px[o + 3] = 255;
    }

    private static byte To8(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0, 255);
}
