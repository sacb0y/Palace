using ImageMagick;
using Palace.Helpers;

namespace Palace.Services;

/// <summary>
/// Magick.NET → <see cref="HdrFrame"/> for HDR JXR / OpenEXR / TGA, and
/// PSD → JPEG mosaic thumbs. Thin glue only — present stays
/// <see cref="HdrSwapchainPresenter"/>; thumbs stay <see cref="ThumbnailService"/>.
/// Never opens online-only originals. AVIF stays libavif.
/// </summary>
internal static class MagickDecode
{
    public static bool CanPresent(string path, HdrProbe probe) =>
        !CloudFile.IsOnlineOnly(path)
        && CloudFile.Exists(path)
        && (probe.Kind is HdrKind.HdrJxr or HdrKind.HdrExr or HdrKind.MagickTga
            || PathSafe.IsJxr(path)
            || PathSafe.IsExr(path)
            || PathSafe.IsTga(path));

    public static bool CanThumb(string path) =>
        PathSafe.IsPsd(path)
        && CloudFile.Exists(path)
        && !CloudFile.IsOnlineOnly(path)
        && ScanContent.MayGenerateScanThumbnail(path);

    public static HdrFrame? TryLoad(
        string path,
        HdrProbe probe,
        int viewportPixelWidth,
        int viewportPixelHeight,
        ImageScaling scaling,
        CancellationToken cancellation)
    {
        if (!CanPresent(path, probe))
        {
            return null;
        }

        try
        {
            cancellation.ThrowIfCancellationRequested();
            using var image = new MagickImage(path);
            image.AutoOrient();
            var nativeW = (int)image.Width;
            var nativeH = (int)image.Height;
            if (nativeW <= 0 || nativeH <= 0 || nativeW > 16384 || nativeH > 16384)
            {
                return null;
            }

            var (decodeW, decodeH) = GalleryPresent.PresentDecodeSize(
                nativeW, nativeH, viewportPixelWidth, viewportPixelHeight, scaling);
            if (decodeW > 0
                && decodeH > 0
                && (decodeW < nativeW || decodeH < nativeH))
            {
                image.FilterType = FilterType.Mitchell;
                image.Resize((uint)decodeW, (uint)decodeH);
            }

            cancellation.ThrowIfCancellationRequested();
            EnsureScrgb(image);
            var rgba = ExtractScrgbRgba(image, out var maxNits, out var avgNits, out var minNits, out var maxScrgb);
            if (rgba is null)
            {
                return null;
            }

            return new HdrFrame
            {
                ScrgbRgba = rgba,
                Width = (int)image.Width,
                Height = (int)image.Height,
                NativeWidth = nativeW,
                NativeHeight = nativeH,
                MaxNits = maxNits,
                AvgNits = avgNits,
                MinNits = minNits,
                MaxScrgb = maxScrgb
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public static HdrStats? TryMeasure(
        string path,
        HdrProbe probe,
        int viewportPixelWidth,
        int viewportPixelHeight,
        CancellationToken cancellation)
    {
        var frame = TryLoad(
            path,
            probe,
            viewportPixelWidth,
            viewportPixelHeight,
            ImageScaling.Fit,
            cancellation);
        return frame is null
            ? null
            : new HdrStats(
                frame.MaxNits,
                frame.AvgNits,
                frame.MinNits,
                frame.MaxScrgb,
                frame.NativeWidth,
                frame.NativeHeight);
    }

    /// <summary>
    /// Flatten / composite PSD → JPEG under the Palace thumb root. Caller
    /// supplies the dest path (<see cref="ThumbFileName"/>).
    /// </summary>
    public static bool TryWritePsdJpegThumb(string path, string destJpeg, int maxSide)
    {
        if (!CanThumb(path) || string.IsNullOrWhiteSpace(destJpeg) || maxSide <= 0)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destJpeg)!);
            using var image = new MagickImage(path);
            image.AutoOrient();
            if (image.Width == 0 || image.Height == 0)
            {
                return false;
            }

            var geometry = new MagickGeometry((uint)maxSide)
            {
                Greater = true,
                Less = false
            };
            image.Thumbnail(geometry);
            image.Format = MagickFormat.Jpeg;
            image.Quality = 85;
            image.Write(destJpeg);
            return File.Exists(destJpeg);
        }
        catch
        {
            try
            {
                if (File.Exists(destJpeg))
                {
                    File.Delete(destJpeg);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }

            return false;
        }
    }

    private static void EnsureScrgb(MagickImage image)
    {
        if (image.ColorSpace != ColorSpace.scRGB)
        {
            image.ColorSpace = ColorSpace.scRGB;
        }

        if (!image.HasAlpha)
        {
            image.Alpha(AlphaOption.Opaque);
        }
    }

    private static float[]? ExtractScrgbRgba(
        MagickImage image,
        out float maxNits,
        out float avgNits,
        out float minNits,
        out float maxScrgb)
    {
        maxNits = 0;
        avgNits = 0;
        minNits = 0;
        maxScrgb = 0;

        var width = (int)image.Width;
        var height = (int)image.Height;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        using var pixels = image.GetPixels();
        var values = pixels.GetValues();
        if (values is null || values.Length == 0)
        {
            return null;
        }

        var channels = (int)image.ChannelCount;
        if (channels < 3)
        {
            return null;
        }

        var scale = 1f / (float)Quantum.Max;
        var count = width * height;
        var rgba = new float[count * 4];
        var maxY = 0f;
        var minY = float.MaxValue;
        var sumY = 0.0;
        var peak = 0f;

        for (var i = 0; i < count; i++)
        {
            var src = i * channels;
            var r = (float)values[src] * scale;
            var g = (float)values[src + 1] * scale;
            var b = (float)values[src + 2] * scale;
            var a = channels >= 4 ? (float)values[src + 3] * scale : 1f;
            var dst = i * 4;
            rgba[dst] = r;
            rgba[dst + 1] = g;
            rgba[dst + 2] = b;
            rgba[dst + 3] = a;

            peak = Math.Max(peak, Math.Max(r, Math.Max(g, b)));
            var y = GalleryPresent.LuminanceY(
                r * GalleryPresent.ScrgbNits,
                g * GalleryPresent.ScrgbNits,
                b * GalleryPresent.ScrgbNits,
                bt2020: false);
            maxY = Math.Max(maxY, y);
            minY = Math.Min(minY, y);
            sumY += y;
        }

        maxScrgb = peak;
        maxNits = maxY;
        minNits = minY == float.MaxValue ? 0 : minY;
        avgNits = count > 0 ? (float)(sumY / count) : 0;
        return rgba;
    }
}
