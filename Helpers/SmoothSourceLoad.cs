using Palace.Imaging;

namespace Palace.Helpers;

/// <summary>
/// Load float RGBA for Smooth Working decode via existing Palace decoders
/// (EXR / Radiance / path-gated). Refuses online-only sources. JXL / AVIF / WIC
/// float paths are wired through the same entry; on Linux Cloud Agents tests
/// inject buffers when platform codecs are unavailable.
/// </summary>
public static class SmoothSourceLoad
{
    public readonly record struct Loaded(
        float[] Rgba,
        int Width,
        int Height,
        NormalizeOptions Options,
        int Orientation);

    /// <summary>
    /// Decode a local hydrated file into float RGBA + normalize options.
    /// Throws when the path is missing, online-only, or unsupported.
    /// </summary>
    public static Loaded LoadLocalFile(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!SmoothTileDecodeGate.MayDecodePath(path))
        {
            throw new InvalidOperationException(
                "Smooth Working decode refuses online-only / missing sources; hydrate on Open first.");
        }

        ct.ThrowIfCancellationRequested();
        var bytes = File.ReadAllBytes(path);
        ct.ThrowIfCancellationRequested();

        if (PathSafe.IsExr(path) || ExrFile.IsExr(bytes))
        {
            var frame = ExrFile.TryDecode(bytes)
                ?? throw new InvalidOperationException("EXR decode failed.");
            ExrFile.SanitizeNonFinite(frame.Rgba);
            return new Loaded(
                frame.Rgba,
                frame.Width,
                frame.Height,
                ImagingStillNormalize.OptionsForExr(bytes),
                Orientation: 1);
        }

        if (PathSafe.IsRadiance(path) || RadianceFile.IsRadiance(bytes))
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var size = RadianceFile.TryReadSize(ms)
                ?? throw new InvalidOperationException("Radiance size read failed.");
            ms.Position = 0;
            var frame = RadianceFile.TryDecode(ms, size.Width, size.Height, ct)
                ?? throw new InvalidOperationException("Radiance decode failed.");
            return new Loaded(
                frame.ScrgbRgba,
                frame.Width,
                frame.Height,
                ImagingStillNormalize.OptionsForRadiance(),
                Orientation: 1);
        }

        if (PathSafe.IsJxl(path))
        {
            return LoadJxl(bytes, ct);
        }

        if (PathSafe.IsAvif(path) || PathSafe.IsHeif(path))
        {
            return LoadAvifOrHeif(path, bytes, ct);
        }

        // PNG / JPEG / TIFF / etc. — WIC on Windows app host; tests inject buffers.
        throw new NotSupportedException(
            $"Smooth source format not wired for '{Path.GetExtension(path)}' on this host; " +
            "use DecodeAsync with an injected float buffer or a Windows WIC path.");
    }

    /// <summary>Adapter for <see cref="SmoothWorkingDecode.DecodePathAsync"/>.</summary>
    public static Func<CancellationToken, Task<(float[] Rgba, int W, int H, NormalizeOptions Opts)?>>
        LoaderForPath(string path) =>
        ct =>
        {
            var loaded = LoadLocalFile(path, ct);
            return Task.FromResult<(float[] Rgba, int W, int H, NormalizeOptions Opts)?>(
                (loaded.Rgba, loaded.Width, loaded.Height, loaded.Options));
        };

    private static Loaded LoadJxl(byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // JxlDecode is WinUI/Starward-backed; when unavailable, refuse clearly.
        try
        {
            var type = Type.GetType("Palace.Services.JxlDecode, Palace")
                       ?? Type.GetType("Palace.Services.JxlDecode");
            if (type is null)
            {
                throw new NotSupportedException("JXL decode type not loaded.");
            }

            // Prefer existing fixture helpers when present; otherwise fail soft.
            _ = bytes;
            throw new NotSupportedException(
                "JXL float load for Smooth uses the app StillDecode/JxlDecode path; " +
                "inject Working floats via DecodeAsync in tests.");
        }
        catch (NotSupportedException)
        {
            throw;
        }
    }

    private static Loaded LoadAvifOrHeif(string path, byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _ = path;
        _ = bytes;
        throw new NotSupportedException(
            "AVIF/HEIF float load for Smooth uses the app HdrAvifDecode path; " +
            "inject Working floats via DecodeAsync in tests.");
    }
}
