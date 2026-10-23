using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Palace.Tests;

/// <summary>Load checked-in imaging master fixtures (float dumps + Radiance).</summary>
internal static class ImagingFixtureIO
{
    public static string Dir =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "imaging");

    public static float[] LoadF32(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Dir, name));
        var n = bytes.Length / 4;
        var floats = new float[n];
        for (var i = 0; i < n; i++)
        {
            floats[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4));
        }

        return floats;
    }

    /// <summary>Minimal uncompressed Radiance RGBE reader (no RLE runs).</summary>
    public static (float[] Rgba, int W, int H) LoadHdr(string name = "master.hdr")
    {
        var path = Path.Combine(Dir, name);
        var data = File.ReadAllBytes(path);
        var text = Encoding.ASCII.GetString(data);
        var headerEnd = text.IndexOf("\n\n", StringComparison.Ordinal);
        if (headerEnd < 0)
        {
            throw new InvalidDataException("Radiance header");
        }

        var resLine = text[(headerEnd + 2)..].Split('\n')[0].Trim();
        // -Y H +X W
        var parts = resLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var h = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var w = int.Parse(parts[3], CultureInfo.InvariantCulture);
        var pixelStart = headerEnd + 2 + resLine.Length + 1;
        var rgba = new float[w * h * 4];
        var o = 0;
        for (var i = 0; i < w * h; i++)
        {
            var p = pixelStart + (i * 4);
            var rb = data[p];
            var gb = data[p + 1];
            var bb = data[p + 2];
            var eb = data[p + 3];
            if (eb == 0)
            {
                rgba[o] = rgba[o + 1] = rgba[o + 2] = 0;
            }
            else
            {
                var f = Math.Pow(2, eb - 128 - 8);
                rgba[o] = (float)(rb * f);
                rgba[o + 1] = (float)(gb * f);
                rgba[o + 2] = (float)(bb * f);
            }

            rgba[o + 3] = 1f;
            o += 4;
        }

        return (rgba, w, h);
    }
}
