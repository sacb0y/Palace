using Palace.Imaging;

namespace Palace.Helpers;

/// <summary>
/// Load float RGBA for Smooth Working decode via existing Palace decoders
/// (EXR / Radiance / JXL / AVIF / WIC). Refuses online-only sources. App host
/// registers JXL/AVIF/WIC via <see cref="TryLoadJxl"/> etc.; Linux tests inject
/// buffers or use EXR/Radiance.
/// </summary>
public static class SmoothSourceLoad
{
    public readonly record struct Loaded(
        float[] Rgba,
        int Width,
        int Height,
        NormalizeOptions Options,
        int Orientation);

    /// <summary>Optional app-host JXL loader (wired from SmoothSourceAppLoad).</summary>
    public static Func<string, CancellationToken, Loaded?>? TryLoadJxl { get; set; }

    /// <summary>Optional app-host AVIF/HEIF loader.</summary>
    public static Func<string, CancellationToken, Loaded?>? TryLoadAvif { get; set; }

    /// <summary>Optional app-host WIC float loader (PNG/JPEG/TIFF/…).</summary>
    public static Func<string, CancellationToken, Loaded?>? TryLoadWic { get; set; }

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
            // Full native decode — no box downscale over 64MP (strip/tile later).
            var frame = RadianceFile.TryDecode(ms, destWidth: 0, destHeight: 0, ct)
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
            return TryLoadJxl?.Invoke(path, ct)
                ?? throw new NotSupportedException(
                    "JXL Smooth load requires app-host SmoothSourceAppLoad.Register().");
        }

        if (PathSafe.IsAvif(path) || PathSafe.IsHeif(path))
        {
            return TryLoadAvif?.Invoke(path, ct)
                ?? throw new NotSupportedException(
                    "AVIF/HEIF Smooth load requires app-host SmoothSourceAppLoad.Register().");
        }

        var wic = TryLoadWic?.Invoke(path, ct);
        if (wic is not null)
        {
            return wic.Value;
        }

        throw new NotSupportedException(
            $"Smooth source format not wired for '{Path.GetExtension(path)}' on this host; " +
            "use DecodeAsync with an injected float buffer or register WIC via SmoothSourceAppLoad.");
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

    /// <summary>
    /// Same as <see cref="LoaderForPath"/> but also surfaces EXIF orientation
    /// for the view transform (tiles stay unbaked).
    /// </summary>
    public static Func<CancellationToken, Task<(float[] Rgba, int W, int H, NormalizeOptions Opts, int Orientation)?>>
        LoaderForPathWithOrientation(string path) =>
        ct =>
        {
            var loaded = LoadLocalFile(path, ct);
            return Task.FromResult<(float[] Rgba, int W, int H, NormalizeOptions Opts, int Orientation)?>(
                (loaded.Rgba, loaded.Width, loaded.Height, loaded.Options, loaded.Orientation));
        };
}
