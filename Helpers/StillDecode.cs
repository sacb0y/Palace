namespace Palace.Helpers;

/// <summary>
/// Stills Windows cannot decode itself (EXR, Radiance HDR, TGA, Clip
/// Studio), plus a sniff for files whose bytes are not an image at all.
/// Pure C#; the thumbnail service encodes the result.
/// </summary>
public static class StillDecode
{
    /// <summary>Decoded pixels (BGRA8, straight alpha) or an embedded encoded image.</summary>
    public sealed record Result(byte[]? Bgra, int Width, int Height, byte[]? EncodedImage, bool ToneMapped);

    public static bool HandlesPath(string? path) =>
        PathSafe.IsExr(path) || PathSafe.IsRadiance(path) || PathSafe.IsTga(path) || PathSafe.IsClip(path)
        || PathSafe.IsPsd(path);

    /// <summary>Above this, a PSD/PSB only yields its embedded thumbnail (composite not loaded).</summary>
    public const long PsdCompositeMaxBytes = 768L * 1024 * 1024;

    public static Result? TryDecodeFile(string path, int maxSide)
    {
        byte[] data;
        try
        {
            if (PathSafe.IsPsd(path) && new FileInfo(path).Length > PsdCompositeMaxBytes)
            {
                // Huge PSB: header + resources only (thumbnail lives there).
                using var fs = File.OpenRead(path);
                data = new byte[32 * 1024 * 1024];
                data = data[..fs.ReadAtLeast(data, data.Length, throwOnEndOfStream: false)];
                var jpeg = PsdFile.TryExtractThumbnailJpeg(data);
                return jpeg is null ? null : new Result(null, 0, 0, jpeg, false);
            }

            data = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return TryDecode(data, PathSafe.Extension(path), maxSide);
    }

    public static Result? TryDecode(byte[] data, string ext, int maxSide)
    {
        if (PathSafe.IsClip(ext))
        {
            var png = ClipFile.TryExtractPreview(data);
            return png is null ? null : new Result(null, 0, 0, png, false);
        }

        if (PathSafe.IsPsd(ext))
        {
            var psd = PsdFile.TryDecodeComposite(data);
            var thumb = PsdFile.TryExtractThumbnailJpeg(data);
            if (psd is null || (thumb is not null && IsUniform(psd.Bgra)))
            {
                // No / placeholder composite (saved without "Maximize compatibility").
                return thumb is null ? null : new Result(null, 0, 0, thumb, false);
            }

            var (pw, ph) = Fit(psd.Width, psd.Height, maxSide);
            var ppx = pw == psd.Width && ph == psd.Height ? psd.Bgra : BoxScaleBgra(psd.Bgra, psd.Width, psd.Height, pw, ph);
            return new Result(ppx, pw, ph, null, false);
        }

        if (PathSafe.IsTga(ext))
        {
            var tga = TgaFile.TryDecode(data);
            if (tga is null)
            {
                return null;
            }

            var (w, h) = Fit(tga.Width, tga.Height, maxSide);
            var px = w == tga.Width && h == tga.Height ? tga.Bgra : BoxScaleBgra(tga.Bgra, tga.Width, tga.Height, w, h);
            return new Result(px, w, h, null, false);
        }

        float[]? rgba = null;
        int sw = 0, sh = 0;
        if (PathSafe.IsExr(ext))
        {
            var exr = ExrFile.TryDecode(data);
            if (exr is not null)
            {
                (rgba, sw, sh) = (exr.Rgba, exr.Width, exr.Height);
            }
        }
        else if (PathSafe.IsRadiance(ext))
        {
            using var ms = new MemoryStream(data, writable: false);
            var hdr = RadianceFile.TryDecode(ms);
            if (hdr is not null)
            {
                (rgba, sw, sh) = (hdr.ScrgbRgba, hdr.Width, hdr.Height);
            }
        }

        return rgba is null ? null : FromScrgb(rgba, sw, sh, maxSide);
    }

    private static bool IsUniform(byte[] bgra)
    {
        var first = BitConverter.ToUInt32(bgra, 0);
        for (var i = 4; i < bgra.Length; i += 4)
        {
            if (BitConverter.ToUInt32(bgra, i) != first)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Scale linear scRGB to fit <paramref name="maxSide"/>, then tone map to BGRA8.</summary>
    public static Result FromScrgb(float[] rgba, int width, int height, int maxSide)
    {
        var (w, h) = Fit(width, height, maxSide);
        var scaled = w == width && h == height ? rgba : HdrPixels.BoxScaleScrgb(rgba, width, height, w, h);
        return new Result(ToneMap.ScrgbToBgra8(scaled, w, h), w, h, null, true);
    }

    public static (int Width, int Height) Fit(int width, int height, int maxSide)
    {
        if (maxSide <= 0 || (width <= maxSide && height <= maxSide))
        {
            return (width, height);
        }

        var scale = Math.Min(maxSide / (double)width, maxSide / (double)height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    public static byte[] BoxScaleBgra(byte[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new byte[dw * dh * 4];
        for (var y = 0; y < dh; y++)
        {
            var y0 = y * sh / dh;
            var y1 = Math.Max(y0 + 1, (y + 1) * sh / dh);
            for (var x = 0; x < dw; x++)
            {
                var x0 = x * sw / dw;
                var x1 = Math.Max(x0 + 1, (x + 1) * sw / dw);
                long b = 0, g = 0, r = 0, a = 0;
                var n = 0;
                for (var yy = y0; yy < y1; yy++)
                {
                    for (var xx = x0; xx < x1; xx++)
                    {
                        var i = (yy * sw + xx) * 4;
                        var al = src[i + 3];
                        b += src[i] * al;
                        g += src[i + 1] * al;
                        r += src[i + 2] * al;
                        a += al;
                        n++;
                    }
                }

                var o = (y * dw + x) * 4;
                if (a > 0)
                {
                    dst[o] = (byte)(b / a);
                    dst[o + 1] = (byte)(g / a);
                    dst[o + 2] = (byte)(r / a);
                }

                dst[o + 3] = (byte)(a / n);
            }
        }

        return dst;
    }

    /// <summary>
    /// True when the header clearly is not the format the extension claims
    /// (e.g. app metadata saved as <c>.bmp</c>). Unknown signatures for
    /// formats without magic (TGA) are given the benefit of the doubt.
    /// </summary>
    public static bool IsNotAnImage(ReadOnlySpan<byte> head, string ext)
    {
        if (head.Length < 4)
        {
            return true;
        }

        var h = head.ToArray();
        bool Starts(ReadOnlySpan<byte> m) => h.AsSpan().StartsWith(m);
        var e = ext.ToLowerInvariant();
        return e switch
        {
            ".bmp" => !Starts("BM"u8),
            ".tif" or ".tiff" => !(Starts("II*\0"u8) || Starts("MM\0*"u8) || Starts("II+\0"u8) || Starts("MM\0+"u8)),
            ".png" => !Starts([0x89, (byte)'P', (byte)'N', (byte)'G']),
            ".jpg" or ".jpeg" => !Starts([0xFF, 0xD8]),
            ".gif" => !Starts("GIF8"u8),
            ".webp" => !(Starts("RIFF"u8) && head.Length >= 12 && head.Slice(8, 4).SequenceEqual("WEBP"u8)),
            ".psd" => !Starts("8BPS"u8),
            ".exr" => !ExrFile.IsExr(head),
            ".hdr" => !RadianceFile.IsRadiance(head),
            ".clip" => !ClipFile.IsClip(head),
            ".ico" => !(Starts([0, 0, 1, 0]) || Starts("GIF8"u8) || Starts([0x89, (byte)'P', (byte)'N', (byte)'G']) || Starts("BM"u8)),
            _ => false
        };
    }

    /// <summary>A neutral "not previewable" tile: dark grey with a lighter diagonal cross.</summary>
    public static Result Placeholder(int size = 256)
    {
        var px = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = (y * size + x) * 4;
                var onCross = Math.Abs(x - y) <= 2 || Math.Abs(x + y - (size - 1)) <= 2;
                var border = x < 3 || y < 3 || x >= size - 3 || y >= size - 3;
                var v = onCross || border ? (byte)110 : (byte)48;
                px[i] = px[i + 1] = px[i + 2] = v;
                px[i + 3] = 255;
            }
        }

        return new Result(px, size, size, null, false);
    }
}
