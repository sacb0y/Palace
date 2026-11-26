using Palace.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Palace.Services;

/// <summary>
/// Streamed WIC strip decode for Smooth ≥64MP stills (PNG/JPEG/BMP/TIFF).
/// Opens the decoder once and fills horizontal bands via
/// <see cref="BitmapTransform.Bounds"/> (WICRect strips of
/// <see cref="SmoothWorkingDecode.DefaultStripRows"/> rows) — never a
/// full-image float buffer. Prefers Rgba16 → float; falls back to Rgba8.
/// </summary>
internal static class SmoothWicStripDecode
{
    /// <summary>
    /// Probe pixel size without a full decode. Returns null when the path is
    /// missing or WIC cannot open it.
    /// </summary>
    public static async Task<(int Width, int Height)?> TryGetSizeAsync(
        string path,
        CancellationToken ct = default)
    {
        if (!SmoothWorkingDecode.IsStripCapablePath(path) || !File.Exists(path))
        {
            return null;
        }

        ct.ThrowIfCancellationRequested();
        var file = await StorageFile.GetFileFromPathAsync(path);
        ct.ThrowIfCancellationRequested();
        using var stream = await file.OpenReadAsync();
        ct.ThrowIfCancellationRequested();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        ct.ThrowIfCancellationRequested();
        return ((int)decoder.PixelWidth, (int)decoder.PixelHeight);
    }

    /// <summary>
    /// Decode over-budget strip-capable file → RGBA16F L0 tiles. Cancels with
    /// <paramref name="decode"/>. Disposes the WIC stream when finished.
    /// </summary>
    public static async Task<SmoothWorkingDecodeResult?> DecodeFileStripAsync(
        SmoothWorkingDecode decode,
        string path,
        CancellationToken external = default)
    {
        ArgumentNullException.ThrowIfNull(decode);
        if (!SmoothWorkingDecode.IsStripCapablePath(path))
        {
            throw new NotSupportedException(
                "Strip decode is not available for this format: " + Path.GetExtension(path));
        }

        var file = await StorageFile.GetFileFromPathAsync(path);
        external.ThrowIfCancellationRequested();
        using var stream = await file.OpenReadAsync();
        external.ThrowIfCancellationRequested();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        external.ThrowIfCancellationRequested();
        var width = (int)decoder.PixelWidth;
        var height = (int)decoder.PixelHeight;
        if (width < 1 || height < 1)
        {
            return null;
        }

        var route = SmoothWorkingDecode.ClassifyFileRoute(path, width, height);
        if (route != SmoothWorkingDecode.WorkingRoute.Strip)
        {
            // Within budget should use DecodePathAsync; unsupported should error.
            return null;
        }

        var gate = new object();
        return await decode.DecodeStripToRgba16FAsync(
            width,
            height,
            (y0, rows, dest) =>
            {
                lock (gate)
                {
                    external.ThrowIfCancellationRequested();
                    FillStripCore(decoder, width, height, y0, rows, dest, external);
                }
            },
            external).ConfigureAwait(false);
    }

    private static void FillStripCore(
        BitmapDecoder decoder,
        int width,
        int height,
        int y0,
        int rows,
        float[] dest,
        CancellationToken ct)
    {
        // Sync wait — Smooth decode already runs on a background Task.
        // Bounds = WICRect strip; Exif ignored (tiles stay unbaked; view rotates).
        if (rows < 1 || y0 < 0 || y0 + rows > height)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var transform = new BitmapTransform
        {
            Bounds = new BitmapBounds
            {
                X = 0,
                Y = (uint)y0,
                Width = (uint)width,
                Height = (uint)rows
            }
        };

        ct.ThrowIfCancellationRequested();
        PixelDataProvider pixels;
        var used16 = false;
        try
        {
            pixels = decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba16,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage)
                .AsTask(ct)
                .GetAwaiter()
                .GetResult();
            used16 = true;
        }
        catch
        {
            pixels = decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage)
                .AsTask(ct)
                .GetAwaiter()
                .GetResult();
        }

        var bytes = pixels.DetachPixelData();
        var need = width * rows * 4;
        if (dest.Length < need)
        {
            throw new ArgumentException("Strip dest too small.", nameof(dest));
        }

        if (used16)
        {
            // Rgba16 unorm → float 0..1 (SDR PNG/JPEG strip path).
            if (bytes.Length < need * 2)
            {
                throw new InvalidOperationException("WIC Rgba16 strip short.");
            }

            for (var i = 0; i < need; i++)
            {
                var u = (ushort)(bytes[i * 2] | (bytes[(i * 2) + 1] << 8));
                dest[i] = u / 65535f;
            }
        }
        else
        {
            if (bytes.Length < need)
            {
                throw new InvalidOperationException("WIC Rgba8 strip short.");
            }

            for (var i = 0; i < need; i++)
            {
                dest[i] = bytes[i] / 255f;
            }
        }
    }
}
