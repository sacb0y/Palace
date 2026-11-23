using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Optional one-line-per-present probe for Fenrir HDR zoom QC.
/// Off by default. Enable with env <c>PALACE_HDR_PRESENT_LOG=1</c> (or
/// <see cref="EnabledOverride"/> in tests). Writes
/// <c>hdr-present.log</c> under <see cref="CatalogStore.DataDir"/>.
/// </summary>
public static class HdrPresentLog
{
    public const string EnvVar = "PALACE_HDR_PRESENT_LOG";
    public const string FileName = "hdr-present.log";
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
    /// One line: decode size, panel DIPs, buffer px, outcome, scale, leaf.
    /// Never throws.
    /// </summary>
    public static void TryAppend(
        string? path,
        int decodeW,
        int decodeH,
        double panelDipW,
        double panelDipH,
        int bufferPxW,
        int bufferPxH,
        string outcome,
        double scaleX = 0,
        double scaleY = 0,
        string? detail = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var leaf = string.IsNullOrWhiteSpace(path) ? "?" : Path.GetFileName(path);
            var line =
                $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} "
                + $"file={leaf} decode={decodeW}x{decodeH} "
                + $"panelDip={panelDipW:0.##}x{panelDipH:0.##} "
                + $"bufferPx={bufferPxW}x{bufferPxH} "
                + $"scale={scaleX:0.####}x{scaleY:0.####} "
                + $"result={outcome}";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                line += " detail=" + detail.Replace(' ', '_');
            }

            line += Environment.NewLine;
            Append(line);
        }
        catch
        {
            // Diagnostics must never break present.
        }
    }

    /// <summary>Decode kick (settle or initial) — want size in decode= fields.</summary>
    public static void TryAppendDecodeStart(
        string? path,
        int wantW,
        int wantH,
        string? detail = null) =>
        TryAppend(path, wantW, wantH, 0, 0, 0, 0, "DecodeStart", detail: detail);

    /// <summary>Decode cancelled (navigate / superseded / settle cancel).</summary>
    public static void TryAppendDecodeCancel(
        string? path,
        int wantW,
        int wantH,
        string? detail = null) =>
        TryAppend(path, wantW, wantH, 0, 0, 0, 0, "DecodeCancelled", detail: detail);

    public static string FormatLine(
        string? path,
        int decodeW,
        int decodeH,
        double panelDipW,
        double panelDipH,
        int bufferPxW,
        int bufferPxH,
        string outcome,
        double scaleX = 0,
        double scaleY = 0) =>
        $"file={(string.IsNullOrWhiteSpace(path) ? "?" : Path.GetFileName(path))} "
        + $"decode={decodeW}x{decodeH} "
        + $"panelDip={panelDipW:0.##}x{panelDipH:0.##} "
        + $"bufferPx={bufferPxW}x{bufferPxH} "
        + $"scale={scaleX:0.####}x{scaleY:0.####} "
        + $"result={outcome}";

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
