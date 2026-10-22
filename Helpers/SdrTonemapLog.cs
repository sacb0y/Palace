using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Optional one-line-per-present diagnostics for SDR tonemap debugging.
/// Off by default. Enable with env <c>PALACE_SDR_TONEMAP_LOG=1</c> (or
/// <see cref="EnabledOverride"/> in tests). Writes
/// <c>sdr-tonemap.log</c> under <see cref="CatalogStore.DataDir"/> — not
/// OutputDebugString.
/// </summary>
public static class SdrTonemapLog
{
    public const string EnvVar = "PALACE_SDR_TONEMAP_LOG";
    public const string FileName = "sdr-tonemap.log";
    public const long DefaultMaxBytes = 256 * 1024;

    /// <summary>Test override: null = read env, true/false force.</summary>
    public static bool? EnabledOverride { get; set; }

    /// <summary>Test override for log path; null = DataDir/FileName.</summary>
    public static string? PathOverride { get; set; }

    private static readonly object Gate = new();

    public static bool IsEnabled
    {
        get
        {
            if (EnabledOverride is { } forced)
            {
                return forced;
            }

            var v = Environment.GetEnvironmentVariable(EnvVar);
            return v is "1" or "true" or "TRUE" or "yes" or "YES";
        }
    }

    public static string ResolvePath() =>
        PathOverride
        ?? Path.Combine(CatalogStore.DataDir(), FileName);

    /// <summary>
    /// One line: decoder, intensity_target, scale, buffer TF, p995, white,
    /// exposure, SdrWhiteLevel, leaf name. Never throws.
    /// </summary>
    public static void TryAppendPresent(
        string? path,
        string decoder,
        float intensityTarget,
        float scale,
        string bufferTransfer,
        float p995,
        float white,
        float exposure,
        float sdrWhiteLevelNits)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var leaf = string.IsNullOrWhiteSpace(path) ? "?" : Path.GetFileName(path);
            var sdr = sdrWhiteLevelNits > 0 ? sdrWhiteLevelNits : GalleryPresent.ScrgbNits;
            var line =
                $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} "
                + $"file={leaf} decoder={decoder} intensity_target={intensityTarget:0.###} "
                + $"scale={scale:0.####} buffer={bufferTransfer} p995={p995:0.####} "
                + $"white={white:0.####} exposure={exposure:0.####} sdrWhite={sdr:0.#}"
                + Environment.NewLine;
            Append(line);
        }
        catch
        {
            // Diagnostics must never break present.
        }
    }

    /// <summary>
    /// Viewer / thumb surface that is not the HDR swapchain SDR Reinhard path.
    /// Same line shape as <see cref="TryAppendPresent"/> so laptop greps match.
    /// <paramref name="decoder"/> values: mosaic-thumb, large-preview,
    /// wic-original, hdr-attempt, hdr-fail, libjxl-thumb, libjxl-thumb-fail,
    /// shell-thumb-skip, libjxl-decode, hdr-display.
    /// </summary>
    public static void TryAppendViewer(
        string? path,
        string decoder,
        string bufferTransfer = "n/a",
        float intensityTarget = 0f,
        float scale = 1f,
        float p995 = 0f) =>
        TryAppendPresent(
            path,
            decoder,
            intensityTarget,
            scale,
            bufferTransfer,
            p995,
            white: 0f,
            exposure: 0f,
            sdrWhiteLevelNits: 0f);

    public static void Append(string line)
    {
        lock (Gate)
        {
            var path = ResolvePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Rotate(path);
            File.AppendAllText(path, line, Encoding.UTF8);
        }
    }

    private static void Rotate(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < DefaultMaxBytes)
            {
                return;
            }

            File.Move(path, path + ".1", overwrite: true);
        }
        catch
        {
            // ignore
        }
    }
}
